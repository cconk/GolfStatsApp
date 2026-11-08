using GolfStatsApp.Server.Services;
using GolfStatsApp.Shared.Models;

namespace GolfStatsApp.Tests;

public class GolfSimulationServiceTests
{
    private static SimulationRequest Request(string captain = "A", int benchCount = 1)
    {
        var request = new SimulationRequest
        {
            EventType = "major",
            Captain = captain,
            Players =
            {
                new GolfPlayer { Name = "A", Birdies = 4.5, Bogeys = 2.0, CutProbability = 0.9 },
                new GolfPlayer { Name = "B", Birdies = 4.0, Bogeys = 2.5, CutProbability = 0.8 },
            }
        };
        for (var i = 0; i < benchCount; i++)
            request.BenchPlayers.Add(new BenchPlayer { Name = $"Bench{i}", Birdies = 3.8, Bogeys = 2.6, CutProbability = 0.7 });
        return request;
    }

    [Fact]
    public async Task Percentiles_AreOrdered()
    {
        var service = new GolfSimulationService(new Random(42));
        var result = await service.RunSimulation(Request(), 2000);

        Assert.True(result.Percentile10th <= result.Percentile50th);
        Assert.True(result.Percentile50th <= result.Percentile90th);
        Assert.True(result.Percentile50th > 0);
    }

    [Fact]
    public async Task SameSeed_ProducesSameResult()
    {
        var a = await new GolfSimulationService(new Random(7)).RunSimulation(Request(), 1000);
        var b = await new GolfSimulationService(new Random(7)).RunSimulation(Request(), 1000);

        Assert.Equal(a.Percentile10th, b.Percentile10th);
        Assert.Equal(a.Percentile50th, b.Percentile50th);
        Assert.Equal(a.Percentile90th, b.Percentile90th);
    }

    [Fact]
    public async Task ReportsPercentilesForEveryPlayerIncludingBench()
    {
        var result = await new GolfSimulationService(new Random(1)).RunSimulation(Request(benchCount: 2), 500);

        var names = result.PlayerPercentiles.Select(p => p.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "A", "B", "Bench0", "Bench1" }, names);
        Assert.Single(result.PlayerPercentiles, p => p.IsCaptain);
    }

    [Fact]
    public async Task Captain_ScoresRoughlyDouble()
    {
        var request = new SimulationRequest
        {
            Captain = "Cap",
            Players =
            {
                new GolfPlayer { Name = "Cap", Birdies = 4, Bogeys = 2, CutProbability = 1 },
                new GolfPlayer { Name = "Other", Birdies = 4, Bogeys = 2, CutProbability = 1 },
            }
        };

        var result = await new GolfSimulationService(new Random(3)).RunSimulation(request, 5000);
        var cap = result.PlayerPercentiles.Single(p => p.Name == "Cap").Percentile50th;
        var other = result.PlayerPercentiles.Single(p => p.Name == "Other").Percentile50th;

        Assert.InRange(cap / other, 1.6, 2.4);
    }

    [Fact]
    public async Task GuaranteedCut_MeansNoBenchPlayersInSample()
    {
        var request = Request();
        request.Players.ForEach(p => p.CutProbability = 1.0);

        var result = await new GolfSimulationService(new Random(5)).RunSimulation(request, 500);

        Assert.DoesNotContain(result.PlayerResults, r => r.Name.StartsWith("Bench"));
        Assert.Equal(2, result.PlayerResults.Count);
    }

    [Fact]
    public async Task BetterPlayers_ScoreHigher()
    {
        GolfPlayer P(string n, double birdies, double bogeys) => new() { Name = n, Birdies = birdies, Bogeys = bogeys, CutProbability = 1 };
        var strong = new SimulationRequest { Players = { P("S1", 5, 1.5), P("S2", 5, 1.5) } };
        var weak = new SimulationRequest { Players = { P("W1", 2.5, 3.5), P("W2", 2.5, 3.5) } };

        var service = new GolfSimulationService(new Random(11));
        var strongResult = await service.RunSimulation(strong, 2000);
        var weakResult = await service.RunSimulation(weak, 2000);

        Assert.True(strongResult.Percentile50th > weakResult.Percentile50th);
    }
}
