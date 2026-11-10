using Microsoft.Extensions.AI;
using SeldonLattice.Domain.Lattice;
using SeldonLattice.Domain.Options;
using SeldonLattice.Domain.Utilities;

namespace SeldonLattice.Infrastructure.Services;

/// <summary>
/// This is a GEOMETRIC PROXY. It cannot distinguish a synthesis that articulates
///   both poles without merging them from one that merely splits the difference - that
///   distinction needs the claims/dissensus channel, not embeddings. The proxy is
///   measured here so the discrimination question can be asked and answered empirically
///   (does it separate held tension from pole restatement independently of advance?).
/// </summary>
public sealed class AscentQualityScorer(
    IEmbeddingGenerator<string, Embedding<float>> embeddingService,
    AscentScorerMode mode = AscentScorerMode.Legacy,
    float betweennessFloor = 0.60f)
{
    public const float BetweennessWeight = 0.5f;
    public const float AdvanceWeight = 0.5f;

    public sealed record AscentQuality(
        float Betweenness,
        float Advance,
        float AscentScore,
        float? JoinNovelty = null,
        float? MediationDeviation = null,
        AscentScorerMode Mode = AscentScorerMode.Legacy,
        float IrreducibleTension = 0f)
    {
        public override string ToString() =>
            $"betweenness={Betweenness:F3},advance={Advance:F3},score={AscentScore:F3}" +
            $",irreducibleTension={IrreducibleTension:F3}" +
            (JoinNovelty is not null ? $",joinNovelty={JoinNovelty:F3}" : "") +
            (MediationDeviation is not null ? $",mediationDeviation={MediationDeviation:F3}" : "") +
            $",mode={Mode}";
    }

    public async Task<AscentQuality?> ComputeAsync(
        string currentTheme,
        string antithesisReply,
        string synthesisThesis,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(synthesisThesis))
            return null;

        try
        {
            var thesisEmb = (await embeddingService.GenerateAsync([currentTheme], cancellationToken: cancellationToken))[0].Vector.ToArray();
            var antithesisEmb = (await embeddingService.GenerateAsync([antithesisReply], cancellationToken: cancellationToken))[0].Vector.ToArray();
            var synthesisEmb = (await embeddingService.GenerateAsync([synthesisThesis], cancellationToken: cancellationToken))[0].Vector.ToArray();

            var thesisSim = SimdVectorUtility.CosineSimilarity(synthesisEmb, thesisEmb);
            var antithesisSim = SimdVectorUtility.CosineSimilarity(synthesisEmb, antithesisEmb);

            var midpoint = SimdVectorUtility.AverageEmbeddings(thesisEmb, antithesisEmb);
            var midSim = SimdVectorUtility.CosineSimilarity(synthesisEmb, midpoint);

            var betweenness = midSim;                                            // how far the synthesis "is between" the poles
            var advance = (1f - thesisSim) + (1f - antithesisSim);              // how far it goes beyond both (0..2)
            var advance01 = Math.Clamp(advance / 2f, 0f, 1f);

            // Mode selection:
            // Legacy - equal-weight blend (inverted on pole restatement; telemetry continuity only).
            // Spec - advance as objective, betweenness floor as hard constraint.
            var score = mode switch
            {
                AscentScorerMode.Spec when betweenness < betweennessFloor => 0f, // not grounded in the poles at all
                AscentScorerMode.Spec => advance01,
                _ => Math.Clamp(BetweennessWeight * betweenness + AdvanceWeight * advance01, 0f, 1f),
            };

            // S1 concept-lattice telemetry over the same intent vectors.
            var joinNovelty = ConceptLattice.JoinNovelty(synthesisEmb, thesisEmb, antithesisEmb);
            var mediationDeviation = ConceptLattice.MediationDeviation(synthesisEmb, thesisEmb, antithesisEmb);

            // IrreducibleTension - the negative-dialectic axis, independent of the ascent
            // objective. High only when the synthesis is grounded in the disagreement
            // (betweenness) AND has not collapsed onto either pole (1 - max pole similarity).
            // See the class doc for the states this admits and rejects.
            var maxPoleSimilarity = MathF.Max(thesisSim, antithesisSim);
            var irreducibleTension = Math.Clamp(betweenness * (1f - maxPoleSimilarity), 0f, 1f);

            return new AscentQuality(betweenness, advance01, score, joinNovelty, mediationDeviation, mode, irreducibleTension);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Embedding provider hiccup: degrade to null so callers can handle the unavailable score.
            return null;
        }
    }
}