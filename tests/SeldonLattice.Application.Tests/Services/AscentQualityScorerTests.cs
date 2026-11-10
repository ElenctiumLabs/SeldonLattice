using Microsoft.Extensions.AI;
using SeldonLattice.Application.Evaluation;
using SeldonLattice.Domain.Options;
using SeldonLattice.Infrastructure.Services;
using Xunit;

namespace SeldonLattice.Application.Tests.Services;

/// <summary>
/// Deterministic embedding generator reusing the FNV-1a hash approach
/// from the embedding test stub. Unit tests only.
/// </summary>
internal sealed class TestEmbeddingService : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int Dim = 32;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken ct = default)
    {
        var results = values.Select(v => new Embedding<float>(Embed(v))).ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null) return null;
        if (serviceType == typeof(EmbeddingGeneratorMetadata))
            return new EmbeddingGeneratorMetadata(nameof(TestEmbeddingService));
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose() { }

    private static float[] Embed(string text)
    {
        var v = new float[Dim];
        if (string.IsNullOrEmpty(text)) return v;
        foreach (var token in text.ToLowerInvariant()
                     .Split([' ', '\n', '\t', ',', '.', '!', '?', ';', ':'], StringSplitOptions.RemoveEmptyEntries)
                     .Where(w => w.Length > 2))
        {
            uint h = 2166136261u;
            foreach (var c in token) { h ^= c; h *= 16777619u; }
            var idx = (int)(h % Dim);
            v[idx] += ((h >> 31) & 1) == 0 ? 1f : -1f;
        }
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        if (norm > 0f) for (var i = 0; i < Dim; i++) v[i] /= norm;
        return v;
    }
}

public class AscentQualityScorerTests
{
    [Fact]
    public async Task Compute_NonEmpty_ReturnsBoundedScores()
    {
        var scorer = new AscentQualityScorer(new TestEmbeddingService());

        AscentQualityScorer.AscentQuality? result = await scorer.ComputeAsync(
            currentTheme: "the dialectical method resolves contradictions",
            antithesisReply: "but contradictions can be irreconcilable",
            synthesisThesis: "contradictions are resolved through iterative negation");

        Assert.NotNull(result);
        Assert.InRange(result.Betweenness, 0f, 1f);
        Assert.InRange(result.Advance, 0f, 1f);
        Assert.InRange(result.AscentScore, 0f, 1f);
    }

    [Fact]
    public async Task Compute_EmptySynthesis_ReturnsNull()
    {
        var scorer = new AscentQualityScorer(new TestEmbeddingService());

        var result = await scorer.ComputeAsync("a", "b", "");

        Assert.Null(result); // degrade-by-design: no accepted synthesis to score
    }

    [Fact]
    public async Task RestatingAPole_YieldsHighBetweenness()
    {
        var scorer = new AscentQualityScorer(new TestEmbeddingService());

        // A synthesis identical to the current theme restates that pole: the embedding
        // sits on the sphere between the two poles, so betweenness is bounded below by
        // the Santamaría bound 0.5: cos(mid, pole) = (1+cos(poles))/√(2+2cos(poles)) ≥ 0.5.
        var result = await scorer.ComputeAsync(
            currentTheme: "the dialectical method resolves contradictions",
            antithesisReply: "contradictions can also become irreconcilable",
            synthesisThesis: "the dialectical method resolves contradictions");

        Assert.NotNull(result);
        Assert.InRange(result.Betweenness, 0.5f, 1f);
        Assert.InRange(result.AscentScore, 0f, 1f);
    }

    // Regression tests
    // On real nomic embeddings + real syntheses, that the
    // Legacy blend is INVERTED on pole restatement (pole restatements score higher than
    // real syntheses; AUC 0.234) while the Spec mode (advance as objective, betweenness
    // floor as hard constraint) separates them perfectly (AUC 1.000). These tests pin
    // the ordering property with deterministic vectors.

    /// <summary>Deterministic embedding service with exact, hand-constructed vectors.</summary>
    private sealed class FixedVectorEmbeddingService(Dictionary<string, float[]> vectors)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken ct = default)
        {
            var results = values
                .Select(v => new Embedding<float>(
                    vectors.TryGetValue(v, out var vec) ? vec : throw new KeyNotFoundException(v)))
                .ToList();
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceKey is not null) return null;
            if (serviceType == typeof(EmbeddingGeneratorMetadata))
                return new EmbeddingGeneratorMetadata(nameof(FixedVectorEmbeddingService));
            return serviceType.IsInstanceOfType(this) ? this : null;
        }

        public void Dispose() { }
    }

    private static (float[] Thesis, float[] Antithesis, float[] RealSynthesis, float[] PoleRestatement)
        BuildVectors()
    {
        // Geometry mirrors what we measured on real nomic embeddings:
        // same-domain poles are strongly correlated (cos -- 0.7), which is what makes a
        // pole restatement's betweenness saturate (cos to midpoint -- 0.92) and lets that
        // saturation outweigh its near-zero advance in the Legacy blend.
        float[] norm(float x, float y, float z)
        {
            var n = MathF.Sqrt(x * x + y * y + z * z);
            return [x / n, y / n, z / n];
        }

        var thesis = new float[] { 1f, 0f, 0f };              // pole A            (cos(A,B) = 0.700)
        var antithesis = norm(0.7f, 0.714f, 0f);              // pole B
        // Real synthesis: balanced across both poles + a genuinely new z-dimension.
        // bet = 0.601 (passes the 0.60 floor), advance = 0.446 Spec 0.446, Legacy 0.523.
        var realSynthesis = norm(0.3f, 0.7f, 0.5f);
        // Pole restatement: the antithesis verbatim. bet saturates at 0.922, advance
        // collapses to 0.150 Spec 0.150, Legacy 0.536 - HIGHER than the real synthesis.
        var poleRestatement = antithesis;
        return (thesis, antithesis, realSynthesis, poleRestatement);
    }

    [Fact]
    public async Task LegacyMode_PoleRestatement_OutScoresRealSynthesis()
    {
        // Pins the measured pathology: the Legacy blend REWARDS pole restatement.
        // If a future change makes Legacy discriminate, this test fails - that is the
        // signal to retire Legacy entirely.
        var (thesis, antithesis, realSyn, poleRestate) = BuildVectors();
        var svc = new FixedVectorEmbeddingService(new Dictionary<string, float[]>
        {
            ["theme"] = thesis,
            ["antith"] = antithesis,
            ["realSyn"] = realSyn,
            ["poleRestate"] = poleRestate,
        });

        var scorer = new AscentQualityScorer(svc, AscentScorerMode.Legacy);
        var real = await scorer.ComputeAsync("theme", "antith", "realSyn");
        var restated = await scorer.ComputeAsync("theme", "antith", "poleRestate");

        Assert.NotNull(real);
        Assert.NotNull(restated);
        // Betweenness saturates for the restatement (it IS a pole high cos to midpoint).
        Assert.True(restated.Betweenness > real.Betweenness,
            $"expected restated.Betweenness ({restated.Betweenness:F3}) > real.Betweenness ({real.Betweenness:F3})");
        // ...and that saturation outweighs the zero advance in the equal-weight blend.
        Assert.True(restated.AscentScore > real.AscentScore,
            $"Legacy inversion not reproduced: restated ({restated.AscentScore:F3}) should out-score real ({real.AscentScore:F3})");
    }

    [Fact]
    public async Task SpecMode_PoleRestatement_ScoresStrictlyLowerThanRealSynthesis()
    {
        var (thesis, antithesis, realSyn, poleRestate) = BuildVectors();
        var svc = new FixedVectorEmbeddingService(new Dictionary<string, float[]>
        {
            ["theme"] = thesis,
            ["antith"] = antithesis,
            ["realSyn"] = realSyn,
            ["poleRestate"] = poleRestate,
        });

        var scorer = new AscentQualityScorer(svc, AscentScorerMode.Spec, betweennessFloor: 0.60f);
        var real = await scorer.ComputeAsync("theme", "antith", "realSyn");
        var restated = await scorer.ComputeAsync("theme", "antith", "poleRestate");

        Assert.NotNull(real);
        Assert.NotNull(restated);

        // The restatement is grounded (betweenness 0.922 passes the floor) but its
        // advance collapses (identical to pole B ⇒ cos = 1 toward that pole):
        // Spec score = advance = 0.150, far below the real synthesis's 0.446.
        Assert.InRange(restated.AscentScore, 0.14f, 0.16f);
        // The real synthesis introduces the z-dimension: passes the floor and advances.
        Assert.True(real.AscentScore > restated.AscentScore,
            $"Spec mode must separate: real ({real.AscentScore:F3}) > restated ({restated.AscentScore:F3})");
        Assert.True(real.AscentScore > 0.40f,
            $"real synthesis should carry meaningful advance, got {real.AscentScore:F3}");
    }

    [Fact]
    public async Task SpecMode_UngroundedSynthesis_IsZeroed()
    {
        var (thesis, antithesis, _, _) = BuildVectors();
        // A synthesis orthogonal to the pole midpoint: high advance but no grounding -
        // must be zeroed by the betweenness floor, not rewarded by advance.
        var ungrounded = new float[] { 0f, 0f, -1f };
        var svc = new FixedVectorEmbeddingService(new Dictionary<string, float[]>
        {
            ["theme"] = thesis,
            ["antith"] = antithesis,
            ["ungrounded"] = ungrounded,
        });

        var scorer = new AscentQualityScorer(svc, AscentScorerMode.Spec, betweennessFloor: 0.60f);
        var result = await scorer.ComputeAsync("theme", "antith", "ungrounded");

        Assert.NotNull(result);
        Assert.True(result.Advance > 0.5f, "orthogonal synthesis should show high advance");
        Assert.True(result.Betweenness < 0.60f, "orthogonal synthesis should fail the floor");
        Assert.Equal(0f, result.AscentScore, 3);
    }

    //  IrreducibleTension (negative-dialectic axis, 2026-10-11) 
    // The ascent objective rewards departure from the poles. These tests pin the second
    // axis: a synthesis that HOLDS the contradiction (grounded in both, captured by
    // neither) must score high even when its ascent score is low, and a pole restatement
    // must score ~0 regardless of how well it satisfies the ascent objective.

    [Fact]
    public async Task IrreducibleTension_PoleRestatement_IsNearZero()
    {
        var (thesis, antithesis, _, poleRestate) = BuildVectors();
        var svc = new FixedVectorEmbeddingService(new Dictionary<string, float[]>
        {
            ["theme"] = thesis,
            ["antith"] = antithesis,
            ["poleRestate"] = poleRestate,
        });

        var scorer = new AscentQualityScorer(svc, AscentScorerMode.Spec);
        var restated = await scorer.ComputeAsync("theme", "antith", "poleRestate");

        Assert.NotNull(restated);
        // Identical to pole B ⇒ max pole similarity 1.0 ⇒ tension 0, whatever betweenness says.
        Assert.InRange(restated.IrreducibleTension, 0f, 0.02f);
    }

    [Fact]
    public async Task IrreducibleTension_UngroundedSynthesis_IsNearZero()
    {
        var (thesis, antithesis, _, _) = BuildVectors();
        var ungrounded = new float[] { 0f, 0f, -1f };
        var svc = new FixedVectorEmbeddingService(new Dictionary<string, float[]>
        {
            ["theme"] = thesis,
            ["antith"] = antithesis,
            ["ungrounded"] = ungrounded,
        });

        var scorer = new AscentQualityScorer(svc, AscentScorerMode.Spec);
        var result = await scorer.ComputeAsync("theme", "antith", "ungrounded");

        Assert.NotNull(result);
        // Far from both poles and not between them ⇒ no grounding ⇒ no tension to hold.
        Assert.InRange(result.IrreducibleTension, 0f, 0.30f);
    }

    [Fact]
    public async Task IrreducibleTension_HeldTension_OutScoresPoleRestatement()
    {
        // The core Adornian claim, as a test: a synthesis that articulates both poles
        // without collapsing onto either scores higher on the negative-dialectic axis than
        // one that merely restates a pole - INCLUDING in the case where the ascent
        // objective ranks them the other way (Legacy mode's inversion).
        var (thesis, antithesis, realSyn, poleRestate) = BuildVectors();
        var svc = new FixedVectorEmbeddingService(new Dictionary<string, float[]>
        {
            ["theme"] = thesis,
            ["antith"] = antithesis,
            ["realSyn"] = realSyn,
            ["poleRestate"] = poleRestate,
        });

        var scorer = new AscentQualityScorer(svc, AscentScorerMode.Legacy);
        var real = await scorer.ComputeAsync("theme", "antith", "realSyn");
        var restated = await scorer.ComputeAsync("theme", "antith", "poleRestate");

        Assert.NotNull(real);
        Assert.NotNull(restated);
        // Legacy ranks the restatement HIGHER on ascent (the inversion)...
        Assert.True(restated.AscentScore > real.AscentScore);
        // ...while the negative axis ranks it strictly lower. The two axes disagree, which
        // is exactly the point: tension is not a restatement of advance.
        Assert.True(real.IrreducibleTension > restated.IrreducibleTension,
            $"expected held tension ({real.IrreducibleTension:F3}) > restatement ({restated.IrreducibleTension:F3})");
    }

    [Fact]
    public async Task IrreducibleTension_IsAlwaysComputed_AndBounded()
    {
        // Requirement: scored on EVERY synthesis, not conditionally. Any non-empty
        // synthesis returns a bounded value.
        var scorer = new AscentQualityScorer(new TestEmbeddingService());
        var result = await scorer.ComputeAsync(
            "the dialectical method resolves contradictions",
            "but contradictions can be irreconcilable",
            "contradictions are held open rather than resolved");

        Assert.NotNull(result);
        Assert.InRange(result.IrreducibleTension, 0f, 1f);
        // Present in the formatted result for downstream analysis.
        Assert.Contains("irreducibleTension=", result.ToString());
    }

    [Fact]
    public async Task IrreducibleTension_IsIndependentOfAscentScore()
    {
        // Independence sanity check: across the constructed geometries the two axes must
        // not move in lockstep. If this ever fails, the negative measure has collapsed
        // into a restatement of the ascent objective and should be reconsidered.
        var (thesis, antithesis, realSyn, poleRestate) = BuildVectors();
        var ungrounded = new float[] { 0f, 0f, -1f };
        var svc = new FixedVectorEmbeddingService(new Dictionary<string, float[]>
        {
            ["theme"] = thesis,
            ["antith"] = antithesis,
            ["realSyn"] = realSyn,
            ["poleRestate"] = poleRestate,
            ["ungrounded"] = ungrounded,
        });

        var scorer = new AscentQualityScorer(svc, AscentScorerMode.Spec);
        var results = new List<AscentQualityScorer.AscentQuality>();
        foreach (var s in new[] { "realSyn", "poleRestate", "ungrounded" })
        {
            var q = await scorer.ComputeAsync("theme", "antith", s);
            Assert.NotNull(q);
            results.Add(q);
        }

        var ascents = results.Select(r => (double)r.AscentScore).ToList();
        var tensions = results.Select(r => (double)r.IrreducibleTension).ToList();
        var ascentMean = ascents.Average();
        var tensionMean = tensions.Average();
        var covariance = ascents.Zip(tensions, (a, t) => (a - ascentMean) * (t - tensionMean)).Sum();
        var ascentVariance = ascents.Sum(a => Math.Pow(a - ascentMean, 2));
        var tensionVariance = tensions.Sum(t => Math.Pow(t - tensionMean, 2));
        var denominator = Math.Sqrt(ascentVariance * tensionVariance);
        double? r_ = denominator == 0 ? null : covariance / denominator;

        // Perfect correlation would mean redundancy; require it to be clearly below 1.
        Assert.True(r_ is null || Math.Abs(r_.Value) < 0.99,
            $"tension and ascent are indistinguishable (r={r_})");
    }

    [Fact]
    public async Task DefaultMode_IsLegacy_UntilSpecValidated()
    {
        // Guard: the default must remain Legacy until cross-module validation of Spec
        // completes and LowAscentThreshold is re-derived.
        var scorer = new AscentQualityScorer(new TestEmbeddingService());
        var result = await scorer.ComputeAsync(
            "the dialectical method resolves contradictions",
            "but contradictions can be irreconcilable",
            "contradictions are resolved through iterative negation");

        Assert.NotNull(result);
        Assert.Equal(AscentScorerMode.Legacy, result.Mode);

        var defaultOptions = new DialecticsOptions();
        Assert.Equal(AscentScorerMode.Legacy, defaultOptions.AscentScorerMode);
        Assert.Equal(0.60f, defaultOptions.AscentBetweennessFloor, 3);
    }

    [Fact]
    public void DialecticsJson_StringEnum_BindsToSpec()
    {
        // Verifies the settings.db flip path: EngineConfigService deserialises
        // FrameworkSettings.DialecticsJson with PropertyNameCaseInsensitive and (since
        // the Spec flip) a JsonStringEnumConverter. This must bind "Spec" (and the
        // camelCase variant, and the numeric form) to the enum - without the converter
        // the string form throws and silently falls back to Legacy defaults.
        var opts = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

        var json = """{ "ascentPressureEnabled": true, "AscentScorerMode": "Spec", "AscentBetweennessFloor": 0.60 }""";
        var result = System.Text.Json.JsonSerializer.Deserialize<DialecticsOptions>(json, opts);

        Assert.NotNull(result);
        Assert.Equal(AscentScorerMode.Spec, result.AscentScorerMode);
        Assert.Equal(0.60f, result.AscentBetweennessFloor, 3);

        // camelCase key (as serialized by the settings API round-trip) must also bind
        var jsonCamel = """{ "ascentScorerMode": "Spec", "ascentBetweennessFloor": 0.6 }""";
        var resultCamel = System.Text.Json.JsonSerializer.Deserialize<DialecticsOptions>(jsonCamel, opts);
        
        Assert.NotNull(resultCamel);
        Assert.Equal(AscentScorerMode.Spec, resultCamel.AscentScorerMode);

        // numeric form (binder default without converter) must still bind with converter present
        var jsonNum = """{ "AscentScorerMode": 1 }""";
        var resultNum = System.Text.Json.JsonSerializer.Deserialize<DialecticsOptions>(jsonNum, opts);
        Assert.NotNull(resultNum);
        Assert.Equal(AscentScorerMode.Spec, resultNum.AscentScorerMode);
    }

    [Fact]
    public void AppsettingsConfigBinder_BindsSpecFromString()
    {
        // The ascent services resolve DialecticsOptions via IOptions singletons bound
        // from the appsettings 'Dialectics' section (NOT from settings.db - that path
        // only feeds /api/dialectics/config). The .NET 8 configuration binder must
        // parse the enum from its string form for the appsettings flip to take effect.
        var dict = new Dictionary<string, string?>
        {
            ["Dialectics:AscentScorerMode"] = "Spec",
            ["Dialectics:AscentBetweennessFloor"] = "0.60",
        };
        var provider = new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationProvider(
            new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource { InitialData = dict! });
        var config = new Microsoft.Extensions.Configuration.ConfigurationRoot(
            new List<Microsoft.Extensions.Configuration.IConfigurationProvider> { provider });

        var bound = new DialecticsOptions();
        Microsoft.Extensions.Configuration.ConfigurationBinder.Bind(
            config.GetSection("Dialectics"), bound);

        Assert.Equal(AscentScorerMode.Spec, bound.AscentScorerMode);
        Assert.Equal(0.60f, bound.AscentBetweennessFloor, 3);
    }
}