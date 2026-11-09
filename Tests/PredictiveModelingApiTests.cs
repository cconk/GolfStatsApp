using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GolfStatsApp.Server.PgaTour;
using GolfStatsApp.Server.Services;
using GolfStatsApp.Server.Services.Prediction;
using GolfStatsApp.Shared;
using GolfStatsApp.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GolfStatsApp.Tests;

/// <summary>In-memory PGA Tour data source; flip <see cref="Down"/> to simulate an outage.</summary>
public class FakePgaDataSource : IPgaDataSource
{
    public static readonly int Year = DateTime.UtcNow.Year;
    public static readonly string TargetId = $"R{Year}999";
    public static readonly string PastA = $"R{Year - 1}010";
    public static readonly string PastB = $"R{Year - 1}020";
    public static readonly string PastC = $"R{Year - 1}030";

    public bool Down { get; set; }
    public int ResultCalls;

    public static readonly (string Id, string Name, double Sg)[] Players =
    {
        ("46046", "Scottie Scheffler", 2.8),
        ("28237", "Rory McIlroy", 2.0),
        ("50525", "Collin Morikawa", 1.2),
        ("39971", "Sungjae Im", 0.6),
        ("34098", "Russell Henley", 0.4),
        ("52955", "Ludvig Åberg", 1.5),
        ("10000", "Average Joe", 0.0),
        ("10001", "Average Jane", -0.2),
    };

    private void Check()
    {
        if (Down) throw new HttpRequestException("PGA Tour API down (test)");
    }

    public IReadOnlyList<StatCatalogEntry> GetStatCatalog() => new List<StatCatalogEntry>
    {
        new(StatRoles.SgTotal, "SG: Total", "Strokes Gained", ""),
        new(StatRoles.BirdieAverage, "Birdie Average", "Scoring", ""),
        new(StatRoles.BogeyAvoidance, "Bogey Avoidance", "Scoring", ""),
        new("99999", "Longest Drive", "Off The Tee", ""),
    };

    public Task<string> GetCurrentTournamentIdAsync(CancellationToken ct) => Task.FromResult(TargetId);

    public Task<List<PlayerDirectoryEntry>> GetPlayersAsync(CancellationToken ct)
    {
        Check();
        return Task.FromResult(Players.Select(p => new PlayerDirectoryEntry { PlayerId = p.Id, DisplayName = p.Name, IsActive = true }).ToList());
    }

    public Task<StatRankings> GetStatAsync(string statId, int? year, CancellationToken ct)
    {
        Check();
        var rows = Players.Select((p, i) => new StatRankingRow
        {
            StatId = statId, Rank = i + 1, PlayerId = p.Id, PlayerName = p.Name,
            Values = new Dictionary<string, JsonElement> { ["avg"] = Str((statId == StatRoles.SgTotal ? p.Sg : 3.5 + p.Sg / 4).ToString("0.000")) },
        }).ToList();
        return Task.FromResult(new StatRankings(statId, null, null, Year, new[] { "avg" }, rows));
    }

    public Task<List<PlayerResultRow>> GetPlayerResultsAsync(string playerId, int? season, CancellationToken ct)
    {
        Interlocked.Increment(ref ResultCalls);
        Check();
        var y = season ?? Year;
        if (y != Year - 1 || playerId == "10001") return Task.FromResult(new List<PlayerResultRow>());
        var sg = Players.First(p => p.Id == playerId).Sg;
        var under = (int)Math.Round(sg * 2);
        return Task.FromResult(new List<PlayerResultRow>
        {
            Result(PastA, "Alpha Open", "T5", 68 - under, 68 - under, 70, 70, $"Mar 1, {y}"),
            Result(PastB, "Bravo Classic", "CUT", 74, 75, null, null, $"Apr 1, {y}"),
            Result(PastC, "Charlie Invitational", "1", 66, 67, 68, 66 - under, $"May 1, {y}"),
        }.Where(r => playerId != "10000" || r.TournamentId == PastB).ToList());
    }

    private static PlayerResultRow Result(string id, string name, string pos, int? r1, int? r2, int? r3, int? r4, string date)
    {
        var values = new Dictionary<string, JsonElement>
        {
            ["tournament_name"] = Str(name), ["pos"] = Str(pos), ["end_date"] = Str(date),
        };
        var rounds = new[] { r1, r2, r3, r4 };
        var total = 0;
        for (var i = 0; i < 4; i++)
            if (rounds[i] is { } r)
            {
                values[$"r{i + 1}"] = Str(r.ToString());
                total += r - 72;
            }
        values["to_par"] = Str(total.ToString());
        return new PlayerResultRow { Season = (Year - 1).ToString(), TournamentId = id, Values = values };
    }

    public Task<List<ScheduleEvent>> GetScheduleAsync(int year, CancellationToken ct)
    {
        Check();
        if (year != Year) return Task.FromResult(new List<ScheduleEvent>());
        return Task.FromResult(new List<ScheduleEvent>
        {
            new() { TournamentId = TargetId, TournamentName = "Test Championship", DisplayDate = $"Dec 28, {year}", Status = "UPCOMING" },
            new() { TournamentId = $"R{year}018", TournamentName = "Zurich Classic of New Orleans", DisplayDate = $"Apr 24, {year}", Status = "COMPLETED" },
        });
    }

    public Task<List<FieldPlayer>> GetFieldAsync(string tournamentId, CancellationToken ct)
    {
        Check();
        return Task.FromResult(Players.Select(p => new FieldPlayer { PlayerId = p.Id, DisplayName = p.Name }).ToList());
    }

    public Task<List<TournamentLeaderboardRow>> GetLeaderboardAsync(string tournamentId, CancellationToken ct) =>
        Task.FromResult(new List<TournamentLeaderboardRow>());

    private static JsonElement Str(string s) => JsonSerializer.SerializeToElement(s);
}

public class FakeFallbackPlayerSource : IFallbackPlayerSource
{
    public Task<List<GolfPlayerData>> GetPlayersAsync() => Task.FromResult(FakePgaDataSource.Players
        .Select(p => new GolfPlayerData { PlayerId = p.Id, PlayerName = p.Name, Birdies = 3.5 + p.Sg / 4, Bogeys = 2.5 - p.Sg / 4 }).ToList());
}

public class PredictionApiFactory : ApiFactory
{
    public FakePgaDataSource Pga { get; } = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPgaDataSource>();
            services.AddSingleton<IPgaDataSource>(Pga);
            services.RemoveAll<IFallbackPlayerSource>();
            services.AddSingleton<IFallbackPlayerSource, FakeFallbackPlayerSource>();
        });
    }
}

public class PredictiveModelingApiTests : IClassFixture<PredictionApiFactory>
{
    private const string Base = "api/predictive-modeling";
    private readonly PredictionApiFactory _factory;

    public PredictiveModelingApiTests(PredictionApiFactory factory)
    {
        _factory = factory;
        _factory.Pga.Down = false;
    }

    private HttpClient Client() => _factory.CreateClient();

    private static SimulateRequest Request(int sims = 500, int? seed = 123, params string[] ids) => new()
    {
        TournamentId = FakePgaDataSource.TargetId,
        Golfers = (ids.Length == 0 ? FakePgaDataSource.Players.Select(p => p.Id) : ids).Select(id => new GolferSelection { PlayerId = id }).ToList(),
        Stats = new() { new StatSelection { StatId = StatRoles.SgTotal, Weight = 60 }, new StatSelection { StatId = StatRoles.BirdieAverage, Weight = 40 } },
        Simulations = sims,
        Seed = seed,
    };

    // ---------------------------------------------------------------- search & catalog

    [Theory]
    [InlineData("scheff", "46046")]
    [InlineData("MCILROY", "28237")]
    [InlineData("aberg", "52955")]
    [InlineData("46046", "46046")]
    public async Task Golfers_SearchByNameOrId(string query, string expectedId)
    {
        var results = await Client().GetFromJsonAsync<List<GolferSearchResult>>($"{Base}/golfers?query={Uri.EscapeDataString(query)}");
        Assert.Contains(results!, r => r.PlayerId == expectedId);
    }

    [Fact]
    public async Task Golfers_NoMatch_ReturnsEmpty()
    {
        var results = await Client().GetFromJsonAsync<List<GolferSearchResult>>($"{Base}/golfers?query=zzzzzz");
        Assert.Empty(results!);
    }

    [Theory]
    [InlineData("golfers?query=a")]
    [InlineData("golfers?query=average&limit=0")]
    [InlineData("golfers?query=average&limit=101")]
    public async Task Golfers_InvalidQuery_Returns400(string url) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await Client().GetAsync($"{Base}/{url}")).StatusCode);

    [Fact]
    public async Task Golfers_LimitIsHonored()
    {
        var results = await Client().GetFromJsonAsync<List<GolferSearchResult>>($"{Base}/golfers?query=average&limit=1");
        Assert.Single(results!);
    }

    [Fact]
    public async Task Stats_MarksSupportedStats()
    {
        var stats = await Client().GetFromJsonAsync<List<PredictionStatInfo>>($"{Base}/stats");
        Assert.True(stats!.Single(s => s.StatId == StatRoles.SgTotal).Supported);
        Assert.False(stats!.Single(s => s.StatId == "99999").Supported);
    }

    [Fact]
    public async Task Tournaments_ReturnsScheduleWithCutRules()
    {
        var list = await Client().GetFromJsonAsync<List<PredictionTournament>>($"{Base}/tournaments?scope=all&year={FakePgaDataSource.Year}");
        var target = list!.Single(t => t.TournamentId == FakePgaDataSource.TargetId);
        Assert.True(target.HasCut);
        Assert.Equal(65, target.CutTopN);
        Assert.False(list!.Single(t => t.Name.StartsWith("Zurich")).Supported);
    }

    // ---------------------------------------------------------------- events

    [Fact]
    public async Task Events_ReturnsOnlyParticipatedEvents_WithStatuses()
    {
        var events = await Client().GetFromJsonAsync<List<GolferEvent>>($"{Base}/golfers/10000/events?fromSeason={FakePgaDataSource.Year - 1}&toSeason={FakePgaDataSource.Year}");
        var single = Assert.Single(events!);
        Assert.Equal(FakePgaDataSource.PastB, single.TournamentId);
        Assert.Equal("MC", single.Status);
        Assert.Equal(2, single.RoundsPlayed);
    }

    [Fact]
    public async Task Events_NewestFirst_AndWrittenBackToDatabase()
    {
        var client = Client();
        var url = $"{Base}/golfers/28237/events?fromSeason={FakePgaDataSource.Year - 1}&toSeason={FakePgaDataSource.Year}";
        var past = new[] { FakePgaDataSource.PastC, FakePgaDataSource.PastB, FakePgaDataSource.PastA };
        var events = (await client.GetFromJsonAsync<List<GolferEvent>>(url))!.Where(e => past.Contains(e.TournamentId)).ToList();
        Assert.Equal(past, events.Select(e => e.TournamentId));
        Assert.Equal(4, events[0].Rounds.Count);

        // API outage: served from DB snapshots written through on the previous call.
        _factory.Pga.Down = true;
        var fromDb = (await client.GetFromJsonAsync<List<GolferEvent>>(url))!.Where(e => past.Contains(e.TournamentId)).ToList();
        Assert.Equal(3, fromDb.Count);
        Assert.All(fromDb, e => Assert.Equal("db", e.Source));
    }

    [Fact]
    public async Task Events_FilterByEventType()
    {
        var events = await Client().GetFromJsonAsync<List<GolferEvent>>($"{Base}/golfers/46046/events?fromSeason={FakePgaDataSource.Year - 1}&eventType=major");
        Assert.All(events!, e => Assert.Equal("major", e.EventType));
        Assert.DoesNotContain(events!, e => e.TournamentId == FakePgaDataSource.PastA);
    }

    [Fact]
    public async Task Events_UnknownGolfer_Returns404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await Client().GetAsync($"{Base}/golfers/77777/events")).StatusCode);

    // ---------------------------------------------------------------- simulate

    [Theory]
    [InlineData(1)]
    [InlineData(10_000)]
    public async Task Simulate_AcceptsBounds(int sims)
    {
        var response = await Client().PostAsJsonAsync($"{Base}/simulate", Request(sims));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<SimulateResponse>())!;
        Assert.Equal(sims, result.SimulationsRun);
        Assert.Equal(1.0, result.Golfers.Sum(g => g.PWin), 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_001)]
    public async Task Simulate_RejectsOutOfRangeSimulations(int sims)
    {
        var response = await Client().PostAsJsonAsync($"{Base}/simulate", Request(sims));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("   at ", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Simulate_RejectsNoGolfers()
    {
        var req = Request();
        req.Golfers.Clear();
        Assert.Equal(HttpStatusCode.BadRequest, (await Client().PostAsJsonAsync($"{Base}/simulate", req)).StatusCode);
    }

    [Fact]
    public async Task Simulate_RejectsUnknownGolferAndStat()
    {
        var req = Request(ids: new[] { "46046", "88888" });
        req.Stats.Add(new StatSelection { StatId = "nope" });
        var response = await Client().PostAsJsonAsync($"{Base}/simulate", req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("88888", body);
        Assert.Contains("nope", body);
    }

    [Fact]
    public async Task Simulate_RejectsNoInputs()
    {
        var req = Request();
        req.Stats.Clear();
        req.Performance = new PerformanceSelection
        {
            CurrentTournament = new() { Enabled = false },
            RecentForm = new() { Enabled = false },
            Historical = new() { Enabled = false },
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await Client().PostAsJsonAsync($"{Base}/simulate", req)).StatusCode);
    }

    [Fact]
    public async Task Simulate_UnknownTournament_Returns404()
    {
        var req = Request();
        req.TournamentId = $"R{FakePgaDataSource.Year}555";
        Assert.Equal(HttpStatusCode.NotFound, (await Client().PostAsJsonAsync($"{Base}/simulate", req)).StatusCode);
    }

    [Fact]
    public async Task Simulate_SameSeed_IsReproducible()
    {
        var client = Client();
        var a = await (await client.PostAsJsonAsync($"{Base}/simulate", Request(2_000, 99))).Content.ReadFromJsonAsync<SimulateResponse>();
        var b = await (await client.PostAsJsonAsync($"{Base}/simulate", Request(2_000, 99))).Content.ReadFromJsonAsync<SimulateResponse>();
        Assert.Equal(99, a!.Seed);
        Assert.Equal(a.Golfers.Select(g => (g.PlayerId, g.PWin, g.MeanScoreToPar)), b!.Golfers.Select(g => (g.PlayerId, g.PWin, g.MeanScoreToPar)));
    }

    [Fact]
    public async Task Simulate_StrongerGolferRatedHigher()
    {
        var result = await (await Client().PostAsJsonAsync($"{Base}/simulate", Request(3_000))).Content.ReadFromJsonAsync<SimulateResponse>();
        var scheffler = result!.Golfers.Single(g => g.PlayerId == "46046");
        var jane = result.Golfers.Single(g => g.PlayerId == "10001");
        Assert.True(scheffler.PWin > jane.PWin);
        Assert.True(scheffler.MeanFinish < jane.MeanFinish);
    }

    [Fact]
    public async Task Simulate_ExplicitEvents_OverrideSources_AndAppearInSummary()
    {
        var req = Request(ids: new[] { "46046", "28237" });
        req.Golfers[0].Events = new() { new EventSelection { TournamentId = FakePgaDataSource.PastA, Rounds = new() { 1, 2 } } };
        var result = (await (await Client().PostAsJsonAsync($"{Base}/simulate", req)).Content.ReadFromJsonAsync<SimulateResponse>())!;

        var summary = result.InputsSummary.Golfers.Single(g => g.PlayerId == "46046");
        Assert.True(summary.ExplicitEvents);
        var ev = Assert.Single(summary.Events);
        Assert.Equal(2, ev.Rounds);

        var other = result.InputsSummary.Golfers.Single(g => g.PlayerId == "28237");
        Assert.False(other.ExplicitEvents);
        Assert.InRange(other.Events.Count, 3, PredictionLimits.DefaultLastNEvents); // default last N
    }

    [Fact]
    public async Task Simulate_NonParticipatedEvent_Returns400()
    {
        var req = Request(ids: new[] { "10000" });
        req.Golfers[0].Events = new() { new EventSelection { TournamentId = FakePgaDataSource.PastA } };
        Assert.Equal(HttpStatusCode.BadRequest, (await Client().PostAsJsonAsync($"{Base}/simulate", req)).StatusCode);
    }

    [Fact]
    public async Task Simulate_GolferWithoutEvents_WarnsAndUsesStats()
    {
        var result = (await (await Client().PostAsJsonAsync($"{Base}/simulate", Request(ids: new[] { "10001", "46046" }))).Content.ReadFromJsonAsync<SimulateResponse>())!;
        Assert.Contains(result.Golfers.Single(g => g.PlayerId == "10001").Warnings, w => w.Contains("stats only"));
    }

    [Fact]
    public async Task Simulate_ApiDown_DegradesWithWarningsInsteadOf500()
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{Base}/simulate", Request())).StatusCode); // warm DB/cache

        _factory.Pga.Down = true;
        var req = Request();
        req.Stats = new() { new StatSelection { StatId = StatRoles.BogeyAvoidance } };
        var response = await client.PostAsJsonAsync($"{Base}/simulate", req);
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable, response.StatusCode.ToString());
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = (await response.Content.ReadFromJsonAsync<SimulateResponse>())!;
            Assert.NotEmpty(result.InputsSummary.Warnings);
        }
    }

    [Fact]
    public async Task Simulate_ConcurrencyLimit_Returns429()
    {
        var limiter = _factory.Services.GetRequiredService<SimulationConcurrencyLimiter>();
        var leases = new List<IDisposable>();
        try
        {
            for (var i = 0; i < 10; i++)
                if (limiter.TryAcquire($"hog-{i}") is { } lease) leases.Add(lease);

            var response = await Client().PostAsJsonAsync($"{Base}/simulate", Request());
            Assert.Equal((HttpStatusCode)429, response.StatusCode);
        }
        finally
        {
            leases.ForEach(l => l.Dispose());
        }
    }

    // ---------------------------------------------------------------- export

    [Fact]
    public async Task Export_CsvAndXlsx_MatchResults_AndEscapeFormulas()
    {
        var client = Client();
        var result = (await (await client.PostAsJsonAsync($"{Base}/simulate", Request(200))).Content.ReadFromJsonAsync<SimulateResponse>())!;
        result.Golfers[0].PlayerName = "=HYPERLINK(\"evil\")";

        var csv = await client.PostAsJsonAsync($"{Base}/export?format=csv", result);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        var text = await csv.Content.ReadAsStringAsync();
        Assert.Contains(result.Golfers[1].PlayerName, text);
        Assert.DoesNotContain("\n=HYPERLINK", text);
        Assert.DoesNotContain(",=HYPERLINK", text);

        var xlsx = await client.PostAsJsonAsync($"{Base}/export?format=xlsx", result);
        Assert.Contains("spreadsheetml", xlsx.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Base}/export?format=pdf", result)).StatusCode);
    }
}
