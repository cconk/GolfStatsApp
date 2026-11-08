using System.Text;
using GolfStatsApp.Server.Data;
using GolfStatsApp.Server.Services;
using GolfStatsApp.Shared.Models;

namespace GolfStatsApp.Tests;

public class FantasyScoringTests
{
    [Theory]
    [InlineData(2, 5, 8)]
    [InlineData(3, 5, 5)]
    [InlineData(3, 4, 2)]
    [InlineData(4, 4, 1)]
    [InlineData(5, 4, -1)]
    [InlineData(6, 4, -3)]
    [InlineData(9, 4, -3)]
    public void HolePoints_FollowScoringTable(int strokes, int par, double expected) =>
        Assert.Equal(expected, FantasyScoring.HolePoints(strokes, par));

    [Fact]
    public void RoundBonuses_LowAndSecondLowIncludeTies_AndBogeyFree()
    {
        var bonuses = FantasyScoring.RoundBonuses(new[]
        {
            Round("a", -6, bogeyFree: true), Round("b", -6), Round("c", -4), Round("d", -2, bogeyFree: true)
        });

        Assert.Equal(13, bonuses["a"]);
        Assert.Equal(10, bonuses["b"]);
        Assert.Equal(5, bonuses["c"]);
        Assert.Equal(3, bonuses["d"]);
    }

    [Fact]
    public void AutoSwap_ReplacesCutStartersByBenchPriority_UsingOnlyBenchWhoMadeCut()
    {
        var slots = new List<LineupSlot>
        {
            Slot("s1", true), Slot("s2", true),
            Slot("b1", false, 1), Slot("b2", false, 2)
        };
        var cut = new HashSet<string> { "s1", "s2", "b1" };

        var round3 = FantasyScoring.StartersForRound(slots, 3, true, cut.Contains);

        Assert.Equal(new[] { "b2", "s2" }, round3);
        Assert.Equal(new[] { "s1", "s2" }, FantasyScoring.StartersForRound(slots, 2, true, cut.Contains));
    }

    [Fact]
    public void StartsUsed_CountsEveryGolferThatStartedAnyRound_Once()
    {
        var slots = new List<LineupSlot>
        {
            Slot("a", true), Slot("b", false, 1),
            Slot("b", true, from: 3), Slot("a", false, 1, from: 3)
        };

        var used = FantasyScoring.StartsUsed(slots, 3, false, _ => false);

        Assert.Equal(new[] { "a", "b" }, used.OrderBy(x => x));
        Assert.Equal(new[] { "a" }, FantasyScoring.StartsUsed(slots, 2, false, _ => false));
    }

    [Fact]
    public void ScoreEntry_DoublesCaptain_AndAddsFedExBonusInRound4()
    {
        var entry = new TeamTournamentEntry { CaptainPlayerId = "a", Slots = { Slot("a", true), Slot("b", true) } };
        var rounds = new Dictionary<(string, int), GolferRound>();
        for (var r = 1; r <= 4; r++)
        {
            rounds[("a", r)] = new GolferRound { PlayerId = "a", Round = r, Holes = 18, HolePoints = 20 };
            rounds[("b", r)] = new GolferRound { PlayerId = "b", Round = r, Holes = 18, HolePoints = 10 };
        }
        var golfers = new Dictionary<string, TournamentGolfer>
        {
            ["a"] = new() { PlayerId = "a", Position = "1" },
            ["b"] = new() { PlayerId = "b", Position = "T2" },
            ["c"] = new() { PlayerId = "c", Position = "T2" }
        };
        var data = new FantasyScoring.TournamentData(4, true, true, 1, "pga", rounds,
            new Dictionary<(string, int), double> { [("a", 1)] = 10 }, golfers);

        var scores = FantasyScoring.ScoreEntry(entry, data).ToDictionary(s => s.PlayerId);

        Assert.Equal(160, scores["a"].HolePoints);
        Assert.Equal(20, scores["a"].BonusPoints);
        Assert.Equal(100, scores["a"].FedExPoints);
        Assert.Equal(40, scores["b"].HolePoints);
        Assert.Equal(24.5, scores["b"].FedExPoints);
    }

    [Fact]
    public void ScoreEntry_NoFedExBonusAfterSegment3()
    {
        var entry = new TeamTournamentEntry { Slots = { Slot("a", true) } };
        var data = new FantasyScoring.TournamentData(4, true, true, 4, "pga",
            new Dictionary<(string, int), GolferRound>(), new Dictionary<(string, int), double>(),
            new Dictionary<string, TournamentGolfer> { ["a"] = new() { PlayerId = "a", Position = "1" } });

        Assert.Equal(0, FantasyScoring.ScoreEntry(entry, data).Single().FedExPoints);
    }

    private static GolferRound Round(string id, int toPar, bool bogeyFree = false) =>
        new() { PlayerId = id, Round = 1, Holes = 18, ToPar = toPar, BogeyFree = bogeyFree };

    private static LineupSlot Slot(string id, bool starter, int priority = 0, int from = 1) =>
        new() { PlayerId = id, PlayerName = id, IsStarter = starter, BenchPriority = priority, FromRound = from };
}

public class ExportServiceTests
{
    private static readonly string[] Headers = { "Name", "Score" };

    [Fact]
    public void Csv_EscapesCommasAndQuotes()
    {
        var bytes = ExportService.ToCsv(Headers, new[] { (IReadOnlyList<object?>)new object?[] { "Smith, \"Bob\"", 1.5 } });
        var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');

        Assert.Equal("Name,Score\r\n\"Smith, \"\"Bob\"\"\",1.5\r\n".Replace("\r\n", Environment.NewLine), text);
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("-5", "-5")]
    [InlineData("plain", "plain")]
    public void Csv_NeutralisesFormulaInjection(string input, string expected) =>
        Assert.Equal(expected, ExportService.Escape(input));

    [Fact]
    public void Xlsx_ProducesZipFile()
    {
        var bytes = ExportService.ToXlsx("Sheet", Headers, new[] { (IReadOnlyList<object?>)new object?[] { "A", 2 } });

        Assert.True(bytes.Length > 100);
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
    }
}
