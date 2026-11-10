using Microsoft.Extensions.Logging.Abstractions;
using SeldonLattice.Application.Services;
using SeldonLattice.Domain.Options;
using Xunit;

namespace SeldonLattice.Application.Tests.QualityGates;

/// <summary>
/// Tests for the quality-score length bracket and its alignment with the configured
/// dialectician reply length.
///
/// The motivating failure (2026-10-11): the prompt asked dialecticians for
/// ~80-word replies while the length bracket only awarded points from 400 words up. Every reply
/// therefore lost the same 10 points and the score read as "stuck at 70%". The score was not
/// stuck - a component was structurally unearnable. These tests pin both the arithmetic and the
/// misalignment detection so it is reported rather than rediscovered from a flat distribution.
/// </summary>
public class QualityScoreLengthBracketTests
{
    private static DialogueQualityGates Build(QualityGateOptions? options = null)
        => new(
            options ?? new QualityGateOptions(),
            NullLogger<DialogueQualityGates>.Instance);

    /// <summary>
    /// Prose with a disagreement indicator (so content scores its 20) and no ceremony words.
    /// Length is controlled by truncating a repeated neutral clause.
    /// </summary>
    private static string Response(int words)
    {
        var sb = new System.Text.StringBuilder("However, the claim is mistaken.");
        while (sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < words)
            sb.Append(" The argument is incomplete and its premise is unsupported.");
        var parts = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Take(words));
    }

    private static readonly QualityGateOptions AlignedBracket = new()
    {
        OptimalLengthMin = 60, OptimalLengthMax = 200,
        AcceptableLengthMax = 300, PaddedLengthMax = 450, BloatedLengthMax = 800,
    };

    /// <summary>
    /// Length points for a response of the given size, isolated by differencing against a
    /// 20-word response of the SAME content. That keeps these tests about the length bracket
    /// rather than about CountNewArguments.
    /// </summary>
    private static int LengthPoints(int words, QualityGateOptions opts)
    {
        var gates = Build(opts);
        var at = gates.AnalyzeResponse(Response(words), null, 1).QualityScore;
        var at20 = gates.AnalyzeResponse(Response(20), null, 1).QualityScore;
        return (int)Math.Round(at - at20);
    }

    [Fact]
    public void LengthBracket_AwardsFullPoints_InsideOptimalBand()
    {
        // 88 words is the real observed reply length and sits inside 60-200.
        Assert.Equal(10, LengthPoints(88, AlignedBracket));
    }

    [Fact]
    public void LengthBracket_AwardsNothing_BelowOptimalMin_OldBracket()
    {
        // The regression as arithmetic: an 88-word reply against the OLD 400-word floor
        // earns no length points at all.
        var oldBracket = new QualityGateOptions { OptimalLengthMin = 400, OptimalLengthMax = 800 };

        Assert.Equal(0, LengthPoints(88, oldBracket));
    }

    [Fact]
    public void LengthBracket_RealignsScore_OldVsNew()
    {
        // The fix, stated as the delta it produces on the same 88-word reply.
        var oldBracket = new QualityGateOptions { OptimalLengthMin = 400, OptimalLengthMax = 800 };
        var before = Build(oldBracket).AnalyzeResponse(Response(88), null, 1).QualityScore;
        var after = Build(AlignedBracket).AnalyzeResponse(Response(88), null, 1).QualityScore;

        Assert.Equal(10, (int)Math.Round(after - before));
    }

    [Fact]
    public void LengthBracket_StepsDownThroughTheBrackets()
    {
        Assert.Equal(10, LengthPoints(150, AlignedBracket));
        Assert.Equal(7, LengthPoints(250, AlignedBracket));
        Assert.Equal(4, LengthPoints(400, AlignedBracket));
        Assert.Equal(2, LengthPoints(700, AlignedBracket));
        Assert.Equal(0, LengthPoints(900, AlignedBracket));
    }

    [Fact]
    public void Misalignment_IsDetected_WhenTargetIsBelowBracket()
    {
        var gates = Build(new QualityGateOptions { OptimalLengthMin = 400 });

        Assert.True(gates.IsLengthBracketUnreachable(80, out var reason));
        Assert.Contains("unreachable", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("400", reason);
        Assert.Contains("80", reason);
    }

    [Fact]
    public void Misalignment_IsNotReported_WhenAligned()
    {
        var gates = Build(new QualityGateOptions { OptimalLengthMin = 60 });

        Assert.False(gates.IsLengthBracketUnreachable(80, out var reason));
        Assert.Empty(reason);
    }

    [Fact]
    public void Misalignment_IgnoresUnsetTarget()
    {
        var gates = Build(new QualityGateOptions { OptimalLengthMin = 400 });

        // 0 means "not configured" - do not warn on it.
        Assert.False(gates.IsLengthBracketUnreachable(0, out _));
    }

    [Fact]
    public void DefaultBracket_IsUnreachableForDefaultReplyLength()
    {
        // Guards the shipped defaults: DialecticsOptions defaults target 80 words while
        // QualityGateOptions defaults start at 400. If both defaults ever converge, this test
        // should be updated deliberately rather than silently passing.
        var gates = Build(new QualityGateOptions());          // default bracket
        var dia = new DialecticsOptions();                    // default word targets

        Assert.Equal(400, new QualityGateOptions().OptimalLengthMin);
        Assert.Equal(80, dia.DialecticianTargetWordCount);
        Assert.True(gates.IsLengthBracketUnreachable(dia.DialecticianTargetWordCount, out _),
            "defaults are still misaligned - appsettings QualityGates must override OptimalLengthMin");
    }
}
