using System.Net;
using System.Net.Http.Json;
using GolfStatsApp.Shared;
using GolfStatsApp.Shared.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace GolfStatsApp.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"golfstats-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
                ["Admin:Emails:0"] = "admin@example.com"
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }
}

public class ApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ApiTests(ApiFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task<HttpClient> Register(ApiTests t, string email, string name)
    {
        var client = t.NewClient();
        var response = await client.PostAsJsonAsync("api/auth/register",
            new AuthRequest { Email = email, Password = "Passw0rd!", DisplayName = name });
        response.EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Teams_AreSeededAndPubliclyReadable()
    {
        var teams = await NewClient().GetFromJsonAsync<List<TeamStats>>("api/teams");

        Assert.NotNull(teams);
        Assert.NotEmpty(teams);
    }

    [Fact]
    public async Task Teams_WriteRequiresAdmin()
    {
        var anonymous = NewClient();
        var response = await anonymous.PostAsJsonAsync("api/teams", new TeamStats { TeamName = "Nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanCreateEditAndDeleteTeam_AndNonAdminCannot()
    {
        var admin = await EnsureAdmin();
        var user = await Register(this, $"user-{Guid.NewGuid():N}@example.com", "Regular");

        var forbidden = await user.PostAsJsonAsync("api/teams", new TeamStats { TeamName = "Sneaky" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var name = $"Team {Guid.NewGuid():N}";
        var created = await admin.PostAsJsonAsync("api/teams", new TeamStats { TeamName = name, Wins = 2 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var team = (await created.Content.ReadFromJsonAsync<TeamStats>())!;

        var duplicate = await admin.PostAsJsonAsync("api/teams", new TeamStats { TeamName = name });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        team.Wins = 5;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"api/teams/{team.Id}", team)).StatusCode);
        var fetched = await NewClient().GetFromJsonAsync<TeamStats>($"api/teams/{team.Id}");
        Assert.Equal(5, fetched!.Wins);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"api/teams/{team.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await NewClient().GetAsync($"api/teams/{team.Id}")).StatusCode);
    }

    [Fact]
    public async Task Export_ReturnsCsvAndXlsx()
    {
        var client = NewClient();

        var csv = await client.GetAsync("api/teams/export?format=csv");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Team,Overall Avg", await csv.Content.ReadAsStringAsync());

        var xlsx = await client.GetAsync("api/teams/export?format=xlsx");
        Assert.Contains("spreadsheetml", xlsx.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("api/teams/export?format=pdf")).StatusCode);
    }

    [Fact]
    public async Task Leagues_CreateJoinAndStartDraft()
    {
        var owner = await Register(this, $"owner-{Guid.NewGuid():N}@example.com", "Owner");
        var friend = await Register(this, $"friend-{Guid.NewGuid():N}@example.com", "Friend");

        var created = await owner.PostAsJsonAsync("api/leagues", new CreateLeagueRequest { Name = "Sunday Skins", PlayersPerTeam = 2 });
        var league = (await created.Content.ReadFromJsonAsync<LeagueSummary>())!;
        Assert.Equal(6, league.JoinCode.Length);

        // Can't start alone.
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"api/leagues/{league.Id}/draft/start", null)).StatusCode);

        var joined = await friend.PostAsJsonAsync("api/leagues/join", new JoinLeagueRequest { JoinCode = league.JoinCode.ToLowerInvariant() });
        joined.EnsureSuccessStatusCode();

        // Only the owner can start.
        Assert.Equal(HttpStatusCode.Forbidden, (await friend.PostAsync($"api/leagues/{league.Id}/draft/start", null)).StatusCode);

        var started = await owner.PostAsync($"api/leagues/{league.Id}/draft/start", null);
        started.EnsureSuccessStatusCode();
        var state = (await started.Content.ReadFromJsonAsync<DraftState>())!;
        Assert.Equal(DraftStatuses.InProgress, state.League.DraftStatus);
        Assert.Equal(4, state.TotalPicks);
        Assert.Equal(new[] { 1, 2 }, state.Members.Select(m => m.DraftPosition!.Value).OrderBy(x => x));
        Assert.NotNull(state.CurrentMemberId);

        var board = await owner.GetFromJsonAsync<List<LeaderboardEntry>>($"api/leagues/{league.Id}/leaderboard");
        Assert.Equal(2, board!.Count);
    }

    [Fact]
    public async Task Leagues_AreHiddenFromNonMembers()
    {
        var owner = await Register(this, $"o-{Guid.NewGuid():N}@example.com", "Owner");
        var outsider = await Register(this, $"x-{Guid.NewGuid():N}@example.com", "Outsider");

        var league = (await (await owner.PostAsJsonAsync("api/leagues", new CreateLeagueRequest { Name = "Private" }))
            .Content.ReadFromJsonAsync<LeagueSummary>())!;

        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"api/leagues/{league.Id}/draft")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.DeleteAsync($"api/leagues/{league.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await NewClient().GetAsync("api/leagues")).StatusCode);
    }

    [Fact]
    public async Task Login_RejectsBadPassword()
    {
        var email = $"login-{Guid.NewGuid():N}@example.com";
        await Register(this, email, "Login");

        var bad = await NewClient().PostAsJsonAsync("api/auth/login", new AuthRequest { Email = email, Password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

        var good = NewClient();
        (await good.PostAsJsonAsync("api/auth/login", new AuthRequest { Email = email, Password = "Passw0rd!" })).EnsureSuccessStatusCode();
        var me = await good.GetFromJsonAsync<UserInfo>("api/auth/me");
        Assert.True(me!.IsAuthenticated);
    }

    private const string AdminEmail = "admin@example.com";

    private async Task<HttpClient> EnsureAdmin()
    {
        var client = NewClient();
        var request = new AuthRequest { Email = AdminEmail, Password = "Passw0rd!", DisplayName = "Admin" };
        var response = await client.PostAsJsonAsync("api/auth/register", request);
        if (!response.IsSuccessStatusCode)
            response = await client.PostAsJsonAsync("api/auth/login", request);
        response.EnsureSuccessStatusCode();
        Assert.True((await response.Content.ReadFromJsonAsync<UserInfo>())!.IsAdmin);
        return client;
    }
}

