using SeldonLattice.Application.Services;
using SeldonLattice.Domain.Models;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class FractalEchoTests
{
    private const string Root = "What is the nature of justice?";
    private const string Current = "Is justice a matter of law or of right reason?";

    [Fact]
    public void ShouldEcho_True_WhenGateSustainedAndThemeDiffersFromRoot()
    {
        Assert.True(FractalEcho.ShouldEcho(Current, Root, isSustained: true));
    }

    [Fact]
    public void ShouldEcho_False_WhenNotSustained()
    {
        Assert.False(FractalEcho.ShouldEcho(Current, Root, isSustained: false));
    }

    [Fact]
    public void ShouldEcho_False_WhenRootIsBlank()
    {
        Assert.False(FractalEcho.ShouldEcho(Current, "   ", isSustained: true));
        Assert.False(FractalEcho.ShouldEcho(Current, null, isSustained: true));
    }

    [Fact]
    public void ShouldEcho_False_WhenAlreadyEchoedForThisRoot()
    {
        // The cross-level guard: a prior sustained level already returned to this root.
        Assert.False(FractalEcho.ShouldEcho(Current, Root, isSustained: true, alreadyEchoed: true));
    }

    [Fact]
    public void ShouldEcho_False_WhenCurrentThemeIsAlreadyTheRoot()
    {
        // The immediate-loop guard: the previous echo made the root the current theme, and
        // this level was sustained too. Echoing again would repeat the same question until
        // the level ceiling.
        Assert.False(FractalEcho.ShouldEcho(Root, Root, isSustained: true));
        Assert.False(FractalEcho.ShouldEcho($"  {Root}  ", Root, isSustained: true));
        Assert.False(FractalEcho.ShouldEcho(Root.ToUpperInvariant(), Root, isSustained: true));
    }

    [Fact]
    public void Create_ReturnsEchoEventCarryingTheRootAsContentAndNextTheme()
    {
        var echo = FractalEcho.Create(Root, level: 3);

        Assert.NotNull(echo);
        Assert.Equal(FractalEcho.EventType, echo!.Type);
        Assert.Equal(FractalEcho.SpeakerId, echo.SpeakerId);
        Assert.Equal(Root, echo.Content);
        Assert.Equal(Root, echo.NextTheme);
        Assert.Equal(3, echo.Level);
        Assert.Equal(-1, echo.Turn);
        Assert.True(echo.IsSustained);
        Assert.Equal(DialecticRole.Synthesis, echo.DialecticRole);
    }

    [Fact]
    public void Create_TrimsTheRootQuestion()
    {
        var echo = FractalEcho.Create($"  {Root}  ", level: 0);

        Assert.NotNull(echo);
        Assert.Equal(Root, echo!.Content);
        Assert.Equal(Root, echo.NextTheme);
    }

    [Fact]
    public void Create_ReturnsNull_WhenRootIsBlank()
    {
        Assert.Null(FractalEcho.Create(null, level: 1));
        Assert.Null(FractalEcho.Create("   ", level: 1));
    }

    // ResolveNextTheme: the root is the next rung only when the echo actually fired.
    // Chaining on every sustained rung re-asked the root until the level ceiling
    // (28 returns to root against 3 echo events in the 2026-11-09 batch).

    [Fact]
    public void ResolveNextTheme_ReturnsRoot_WhenEchoEmitted()
    {
        Assert.Equal(Root, FractalEcho.ResolveNextTheme(echoEmitted: true, Root, ascensionNextTheme: null));
        Assert.Equal(Root, FractalEcho.ResolveNextTheme(echoEmitted: true, Root, ascensionNextTheme: "ignored"));
    }

    [Fact]
    public void ResolveNextTheme_ReturnsAscensionTheme_WhenNoEcho()
    {
        // The gate validated a candidate, or sustained without an echo: chain to the proposal.
        Assert.Equal(Current, FractalEcho.ResolveNextTheme(echoEmitted: false, Root, Current));
    }

    [Fact]
    public void ResolveNextTheme_ReturnsEmpty_WhenSustainedWithNoEchoAndNoCandidate()
    {
        // The sustained path clears the candidate, so a rung that cannot return to the root
        // carries no next theme and the caller hands over instead of chaining.
        Assert.Equal(string.Empty, FractalEcho.ResolveNextTheme(echoEmitted: false, Root, null));
        Assert.Equal(string.Empty, FractalEcho.ResolveNextTheme(echoEmitted: false, Root, string.Empty));
    }

    [Fact]
    public void ResolveNextTheme_FallsBackToAscensionTheme_WhenEchoEmittedButRootIsBlank()
    {
        // Defensive: ShouldEcho never fires without a usable root, but the rule must not
        // return a blank root as the next rung if it ever does.
        Assert.Equal(Current, FractalEcho.ResolveNextTheme(echoEmitted: true, null, Current));
        Assert.Equal(Current, FractalEcho.ResolveNextTheme(echoEmitted: true, "   ", Current));
    }

    [Fact]
    public void ResolveNextTheme_DoesNotTreatABlankAscensionThemeAsAValidRung()
    {
        // No echo and no candidate means no next rung at all, not a blank string that the
        // frontend would fall back to the synthesis thesis for.
        Assert.Equal(string.Empty, FractalEcho.ResolveNextTheme(echoEmitted: false, Root, "   "));
    }
}
