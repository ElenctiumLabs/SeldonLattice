using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeldonLattice.Application.Evaluation;

/// <summary>
/// One synthesis with a known ground-truth label, for measuring whether a marker
/// separates canned output from genuine output.
/// </summary>
/// <param name="Label">
/// Ground truth. <c>true</c> = the synthesis is a genuine product of the debate;
/// <c>false</c> = canned/fallback text that has nothing to do with it.
/// </param>
public sealed record LabelledSynthesis(
    bool IsReal,
    string SessionId,
    int Level,
    float AscentScore,
    float IrreducibleTension,
    string? RemainingDissensus,
    float? RepeatsPremise,
    string Content = "");

/// <summary>
/// How well one marker separates the two classes.
///
/// <para>
/// <see cref="Auc"/> is the Mann–Whitney statistic: the probability that a randomly chosen
/// REAL synthesis scores above a randomly chosen CANNED one. It is threshold-free, tolerates
/// ties, and - critically for this experiment - it carries DIRECTION. 0.5 means no
/// discrimination. Below 0.5 means the marker is inverted: it scores canned output higher,
/// which is precisely the failure the geometric proxy exhibits.
/// </para>
///
/// <para>
/// <see cref="CannedPresenceRate"/> / <see cref="RealPresenceRate"/> are reported for
/// presence-style markers (<c>remaining_dissensus</c> is a string that may be null or empty),
/// where the mean is not meaningful but "did the marker appear at all" is.
/// </para>
/// </summary>
public sealed record MarkerDiscrimination(
    [property: JsonPropertyName("marker")] string Marker,
    [property: JsonPropertyName("canned_count")] int CannedCount,
    [property: JsonPropertyName("real_count")] int RealCount,
    [property: JsonPropertyName("canned_mean")] double? CannedMean,
    [property: JsonPropertyName("real_mean")] double? RealMean,
    [property: JsonPropertyName("canned_presence_rate")] double CannedPresenceRate,
    [property: JsonPropertyName("real_presence_rate")] double RealPresenceRate,
    [property: JsonPropertyName("auc")] double Auc,
    [property: JsonPropertyName("discriminates")] bool Discriminates,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("verdict")] string Verdict);

/// <summary>The full discrimination report across the proxy and the real markers.</summary>
public sealed record MarkerDiscriminationReport(
    [property: JsonPropertyName("generated_at_utc")] DateTime GeneratedAtUtc,
    [property: JsonPropertyName("total_samples")] int TotalSamples,
    [property: JsonPropertyName("canned_samples")] int CannedSamples,
    [property: JsonPropertyName("real_samples")] int RealSamples,
    [property: JsonPropertyName("markers")] IReadOnlyList<MarkerDiscrimination> Markers,
    [property: JsonPropertyName("proxy_fails")] bool ProxyFails,
    [property: JsonPropertyName("any_marker_succeeds")] bool AnyMarkerSucceeds,
    [property: JsonPropertyName("summary")] string Summary);

/// <summary>
/// Measures whether the real negative-dialectic markers separate canned syntheses from genuine
/// ones, and whether the geometric proxy does.
///
/// <para>
/// WHY THIS EXISTS. The claim is that the real markers discriminate where <c>IrreducibleTension</c> and
/// <c>AscentScore</c> cannot. The measured finding it responds to was that
/// both geometric metrics scored CANNED
/// syntheses HIGHER than genuine ones (tension 0.2261 vs 0.2032, ascent 0.3602 vs 0.3211).
/// </para>
///
/// <para>
/// This type is deliberately label-driven and takes its samples from the caller, so it can be
/// run against a controlled fixture (a repeatable test) rather than only against a live corpus.
/// The original experiment depended on one session that no longer exists in the store; a
/// fixture-based measurement cannot be lost the same way.
/// </para>
///
/// <para>
/// It makes no claim about WHICH marker is right, and it does not remove the proxy. It reports
/// what separates and what does not, so the decision can be made on evidence.
/// </para>
/// </summary>
public sealed class MarkerDiscriminationEvaluator
{
    /// <summary>
    /// Distance from 0.5 at which an AUC counts as discriminating rather than noise.
    /// 0.75/0.25 is a deliberately high bar: with the sample sizes these experiments produce
    /// (single digits per class), a lower threshold would report chance as signal.
    /// </summary>
    public const double DiscriminationThreshold = 0.75;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public MarkerDiscriminationReport Evaluate(IEnumerable<LabelledSynthesis> samples)
    {
        var all = samples.ToList();
        var canned = all.Where(s => !s.IsReal).ToList();
        var real = all.Where(s => s.IsReal).ToList();

        var markers = new List<MarkerDiscrimination>
        {
            // The two geometric proxies, as the baseline the markers must beat.
            Numeric("irreducible_tension (proxy)", canned, real, s => s.IrreducibleTension),
            Numeric("ascent_score (proxy)", canned, real, s => s.AscentScore),

            // The two moderator-sourced real markers.
            //
            // repeats_premise is nullable, so absence is handled twice over: the presence gate
            // excludes samples without a value from the AUC, and the value projection falls back
            // to 0 only for those already excluded. Without the gate, a missing marker would
            // read as a score of 0 and drag the "real" mean down for the wrong reason.
            Numeric("repeats_premise (real)", canned, real,
                s => s.RepeatsPremise ?? 0f,
                presenceWhen: s => s.RepeatsPremise.HasValue),
            Presence("remaining_dissensus (real)", canned, real,
                s => !string.IsNullOrWhiteSpace(s.RemainingDissensus)),
        };

        var proxyFails = markers
            .Where(m => m.Marker.Contains("(proxy)", StringComparison.Ordinal))
            .Any(m => !m.Discriminates);

        var anyMarkerSucceeds = markers
            .Where(m => m.Marker.Contains("(real)", StringComparison.Ordinal))
            .Any(m => m.Discriminates);

        return new MarkerDiscriminationReport(
            DateTime.UtcNow,
            all.Count,
            canned.Count,
            real.Count,
            markers,
            proxyFails,
            anyMarkerSucceeds,
            BuildSummary(canned.Count, real.Count, markers, proxyFails, anyMarkerSucceeds));
    }

    public string ToJson(MarkerDiscriminationReport report) => JsonSerializer.Serialize(report, JsonOptions);

    /// <summary>
    /// Builds the summary sentence. Written to be read literally in a test failure: it names
    /// which markers discriminate, which do not, and in which direction.
    /// </summary>
    private static string BuildSummary(
        int cannedCount, int realCount,
        IReadOnlyList<MarkerDiscrimination> markers,
        bool proxyFails, bool anyMarkerSucceeds)
    {
        if (cannedCount == 0 || realCount == 0)
            return $"Cannot measure discrimination: need both classes (canned={cannedCount}, real={realCount}).";

        var lines = markers.Select(m =>
            $"  {m.Marker}: AUC={m.Auc:F3} ({m.Direction}) - {m.Verdict}");

        var conclusion = (proxyFails, anyMarkerSucceeds) switch
        {
            (true, true) => "The proxy does NOT separate the classes; at least one real marker DOES.",
            (true, false) => "The proxy does NOT separate the classes, and neither does any real marker. " +
                             "The markers are not yet a validated replacement.",
            (false, true) => "The proxy DOES separate the classes on this sample, and so does at least one " +
                             "real marker. Check the sample before concluding the proxy is redundant.",
            (false, false) => "The proxy DOES separate the classes on this sample. The premise that the markers " +
                              "are needed to do so is not supported here - check the sample before concluding.",
        };

        return $"{cannedCount} canned vs {realCount} real.\n{string.Join("\n", lines)}\n{conclusion}";
    }

    /// <summary>
    /// A marker with a numeric value per sample. AUC is computed over the samples where the
    /// marker is present; <paramref name="presenceWhen"/> (when given) decides presence.
    /// </summary>
    private static MarkerDiscrimination Numeric(
        string name, List<LabelledSynthesis> canned, List<LabelledSynthesis> real,
        Func<LabelledSynthesis, float> value,
        Func<LabelledSynthesis, bool>? presenceWhen = null)
    {
        var cannedVals = canned.Where(s => presenceWhen?.Invoke(s) ?? true).Select(value).ToList();
        var realVals = real.Where(s => presenceWhen?.Invoke(s) ?? true).Select(value).ToList();

        var auc = Auc(realVals, cannedVals);
        var cannedMean = cannedVals.Count > 0 ? cannedVals.Average() : (double?)null;
        var realMean = realVals.Count > 0 ? realVals.Average() : (double?)null;

        var (discriminates, direction, verdict) = Judge(auc, cannedVals.Count, realVals.Count, name);

        return new MarkerDiscrimination(
            name, canned.Count, real.Count, cannedMean, realMean,
            Rate(canned, presenceWhen), Rate(real, presenceWhen),
            auc, discriminates, direction, verdict);
    }

    /// <summary>
    /// A marker measured by presence alone (a string that is null/empty or populated). AUC is
    /// computed over the 0/1 indicator, so it is directly comparable with the numeric markers.
    /// </summary>
    private static MarkerDiscrimination Presence(
        string name, List<LabelledSynthesis> canned, List<LabelledSynthesis> real,
        Func<LabelledSynthesis, bool> present)
    {
        var cannedFlags = canned.Select(s => present(s) ? 1f : 0f).ToList();
        var realFlags = real.Select(s => present(s) ? 1f : 0f).ToList();

        var auc = Auc(realFlags, cannedFlags);
        var (discriminates, direction, verdict) = Judge(auc, cannedFlags.Count, realFlags.Count, name);

        return new MarkerDiscrimination(
            name, canned.Count, real.Count,
            cannedFlags.Count > 0 ? cannedFlags.Average() : null,
            realFlags.Count > 0 ? realFlags.Average() : null,
            Rate(canned, present), Rate(real, present),
            auc, discriminates, direction, verdict);
    }

    private static double Rate(List<LabelledSynthesis> set, Func<LabelledSynthesis, bool>? present)
        => set.Count == 0 ? 0
           : (double)set.Count(s => present?.Invoke(s) ?? true) / set.Count;

    private static (bool discriminates, string direction, string verdict) Judge(
        double auc, int cannedCount, int realCount, string name)
    {
        // Fewer than two samples in either class cannot establish anything.
        if (cannedCount < 2 || realCount < 2)
            return (false, "insufficient",
                $"Not measurable: need >=2 samples per class (canned={cannedCount}, real={realCount}).");

        if (auc >= DiscriminationThreshold)
            return (true, "real>canned", $"Separates: real scores above canned (AUC {auc:F3}).");

        if (auc <= 1 - DiscriminationThreshold)
            return (false, "canned>real",
                $"INVERTED: canned scores above real (AUC {auc:F3}). This is the proxy's failure mode.");

        return (false, "none",
            $"Does not separate: AUC {auc:F3} is within noise of 0.5.");
    }

    /// <summary>
    /// Mann–Whitney U as a probability: P(a random real value > a random canned value), with
    /// ties counted as 0.5. Returns 0.5 when either class is empty.
    /// </summary>
    internal static double Auc(IReadOnlyList<float> real, IReadOnlyList<float> canned)
    {
        if (real.Count == 0 || canned.Count == 0)
            return 0.5;

        double wins = 0;
        foreach (var r in real)
        {
            foreach (var c in canned)
            {
                if (r > c) wins += 1;
                else if (r == c) wins += 0.5;
            }
        }
        return wins / (real.Count * (double)canned.Count);
    }
}
