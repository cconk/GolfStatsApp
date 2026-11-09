using System.Diagnostics;
using GolfStatsApp.Server.Services.Prediction;
using GolfStatsApp.Shared.Models;

namespace GolfStatsApp.Tests;

public class MonteCarloEngineTests
{
    private static readonly MonteCarloEngine Engine = new();

    private static List<GolferInputs> Field(int count, double topSkill = 0)
    {
        var list = new List<GolferInputs>();
        for (var i = 0; i < count; i++)
            list.Add(new GolferInputs { PlayerId = $"P{i}", PlayerName = $"Player {i}", Skill = i == 0 ? topSkill : 0, Sigma = 2.8 });
        return list;
    }

    [Fact]
    public void SameSeed_ProducesIdenticalResults()
    {
        var golfers = Field(30, 1.5);
        var config = new EngineConfig { Simulations = 2_000, Seed = 42, CutTopN = 15 };

        var a = Engine.Run(golfers, config);
        var b = Engine.Run(golfers, config);

        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].PWin, b[i].PWin);
            Assert.Equal(a[i].MeanScoreToPar, b[i].MeanScoreToPar);
            Assert.Equal(a[i].P50, b[i].P50);
            Assert.Equal(a[i].FinishBuckets, b[i].FinishBuckets);
        }
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentResults()
    {
        var golfers = Field(20);
        var a = Engine.Run(golfers, new EngineConfig { Simulations = 1_000, Seed = 1 });
        var b = Engine.Run(golfers, new EngineConfig { Simulations = 1_000, Seed = 2 });
        Assert.Contains(Enumerable.Range(0, a.Count), i => a[i].PWin != b[i].PWin);
    }

    [Fact]
    public void SingleAverageGolfer_WithFieldFiller_HasRealisticOdds()
    {
        var options = new PredictiveModelingOptions();
        var golfers = new List<GolferInputs> { new() { PlayerId = "solo", PlayerName = "Solo", Skill = 0, Sigma = 2.8 } };
        golfers.AddRange(FieldFiller.Generate(155, 5, options));
        Assert.Equal(156, golfers.Count);
        Assert.Equal(155, golfers.Select(g => g.PlayerId).Distinct().Count(id => id.StartsWith(FieldFiller.IdPrefix)));

        var r = Engine.Run(golfers, new EngineConfig { Simulations = 4_000, Seed = 3, CutTopN = 65 })[0];

        Assert.InRange(r.PWin, 0, 0.05);
        Assert.InRange(r.PMakeCut, 0.2, 0.8);
    }

    [Fact]
    public void Probabilities_SumToOne()
    {
        var results = Engine.Run(Field(156, 1), new EngineConfig { Simulations = 2_000, Seed = 7, CutTopN = 65 });

        Assert.Equal(1.0, results.Sum(r => r.PWin), 3);
        Assert.Equal(5.0, results.Sum(r => r.PTop5), 3);
        Assert.Equal(10.0, results.Sum(r => r.PTop10), 3);
        foreach (var r in results)
        {
            Assert.Equal(1.0, r.FinishBuckets.Values.Sum(), 6);
            Assert.InRange(r.PMakeCut, 0, 1);
            Assert.True(r.P10 <= r.P50 && r.P50 <= r.P90);
        }
    }

    [Fact]
    public void StrongerGolfer_WinsMoreOften()
    {
        var results = Engine.Run(Field(40, 2.5), new EngineConfig { Simulations = 5_000, Seed = 3 });
        var others = results.Skip(1).Max(r => r.PWin);
        Assert.True(results[0].PWin > others * 2);
        Assert.True(results[0].MeanFinish < results.Skip(1).Average(r => r.MeanFinish));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(PredictionLimits.MaxSimulations)]
    public void SimulationBounds_Accepted(int sims)
    {
        var results = Engine.Run(Field(10), new EngineConfig { Simulations = sims, Seed = 5 });
        Assert.Equal(1.0, results.Sum(r => r.PWin), 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PredictionLimits.MaxSimulations + 1)]
    public void SimulationBounds_Rejected(int sims) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Engine.Run(Field(5), new EngineConfig { Simulations = sims }));

    [Fact]
    public void NoGolfers_Rejected() =>
        Assert.Throws<ArgumentException>(() => Engine.Run(new List<GolferInputs>(), new EngineConfig()));

    [Fact]
    public void Cut_AppliesTopNAndTies()
    {
        var results = Engine.Run(Field(100), new EngineConfig { Simulations = 2_000, Seed = 11, CutTopN = 50 });
        var expectedMade = results.Sum(r => r.PMakeCut);
        Assert.InRange(expectedMade, 50, 65); // ties retained, so at least 50 make the cut
        Assert.All(results, r => Assert.Equal(1 - r.PMakeCut, r.FinishBuckets[FinishBucketNames.MissedCut], 6));
    }

    [Fact]
    public void NoCut_EveryoneMakesCut()
    {
        var results = Engine.Run(Field(30), new EngineConfig { Simulations = 500, Seed = 4, CutTopN = null });
        Assert.All(results, r => Assert.Equal(1.0, r.PMakeCut));
    }

    [Fact]
    public void FixedRounds_AreUsedAsPlayed()
    {
        var golfers = Field(10);
        golfers[0].FixedRounds = new int?[] { -10, -10, -10, -10 };
        var results = Engine.Run(golfers, new EngineConfig { Simulations = 500, Seed = 9, CutTopN = null });

        Assert.Equal(-40, results[0].MeanScoreToPar);
        Assert.Equal(-40, results[0].P10);
        Assert.Equal(1.0, results[0].PWin);
    }

    [Fact]
    public void PartialFixedRounds_OnlyRemainingSimulated()
    {
        var golfers = Field(10);
        golfers[0].FixedRounds = new int?[] { -8, -8, null, null };
        var results = Engine.Run(golfers, new EngineConfig { Simulations = 1_000, Seed = 9, CutTopN = 5 });

        Assert.Equal(1.0, results[0].PMakeCut);
        Assert.True(results[0].PWin > 0.9);
    }

    [Fact]
    public void Cancellation_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Engine.Run(Field(20), new EngineConfig { Simulations = 10_000 }, cts.Token));
    }

    [Fact]
    public void Benchmark_FullField_NonGating()
    {
        var sw = Stopwatch.StartNew();
        Engine.Run(Field(156, 1), new EngineConfig { Simulations = 10_000, Seed = 1 });
        sw.Stop();
        // NFR-1 target is 10 s on a dev machine; use a generous bound so CI noise doesn't fail the suite.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60), $"Full-field run took {sw.Elapsed}");
    }
}
