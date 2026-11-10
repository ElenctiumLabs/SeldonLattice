using SeldonLattice.Application.Services;
using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Models.Cycle;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class MoveSchedulerTests
{
    [Fact]
    public void Suggest_ReturnsNull_WhenMetricsNull()
    {
        Assert.Null(MoveScheduler.Suggest(null));
    }

    [Fact]
    public void Suggest_ReturnsNull_WhenNewArgumentCountGreaterThanZero()
    {
        var metrics = new ResponseQualityMetrics
        {
            NewArgumentCount = 1,
            SelfSimilarity = 0.8
        };
        Assert.Null(MoveScheduler.Suggest(metrics));
    }

    [Fact]
    public void Suggest_ReturnsNull_WhenSelfSimilarityBelowOrEqualThreshold()
    {
        var metricsEqual = new ResponseQualityMetrics
        {
            NewArgumentCount = 0,
            SelfSimilarity = 0.35
        };
        Assert.Null(MoveScheduler.Suggest(metricsEqual));

        var metricsLow = new ResponseQualityMetrics
        {
            NewArgumentCount = 0,
            SelfSimilarity = 0.1
        };
        Assert.Null(MoveScheduler.Suggest(metricsLow));
    }

    [Fact]
    public void Suggest_ReturnsFormalDistinction_WhenZeroNewArgumentsAndHighSelfSimilarity()
    {
        var metrics = new ResponseQualityMetrics
        {
            NewArgumentCount = 0,
            SelfSimilarity = 0.36
        };
        Assert.Equal(RhetoricalMove.FormalDistinction, MoveScheduler.Suggest(metrics));
    }
}
