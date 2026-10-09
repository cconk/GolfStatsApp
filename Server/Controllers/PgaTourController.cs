using System;
using System.Threading;
using System.Threading.Tasks;
using GolfStatsApp.Server.PgaTour;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace GolfStatsApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PgaTourController : ControllerBase
    {
        private static readonly TimeSpan Live = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan Daily = TimeSpan.FromHours(6);

        private readonly PgaTourApiClient _client;
        private readonly IMemoryCache _cache;

        public PgaTourController(PgaTourApiClient client, IMemoryCache cache)
        {
            _client = client;
            _cache = cache;
        }

        [HttpGet("tournaments/current")]
        public Task<ActionResult> CurrentTournament(string tour = "R", CancellationToken ct = default) =>
            Cached($"current:{tour}", Daily, async () => new { tournamentId = await _client.GetCurrentTournamentIdAsync(tour, ct) });

        [HttpGet("tournaments/{tournamentId}/leaderboard")]
        public Task<ActionResult> Leaderboard(string tournamentId, CancellationToken ct) =>
            Cached($"leaderboard:{tournamentId}", Live, () => _client.GetLeaderboardAsync(tournamentId, ct));

        [HttpGet("tournaments/{tournamentId}/field")]
        public Task<ActionResult> Field(string tournamentId, CancellationToken ct) =>
            Cached($"field:{tournamentId}", Live, () => _client.GetFieldAsync(tournamentId, ct: ct));

        [HttpGet("tournaments/{tournamentId}/odds")]
        public Task<ActionResult> Odds(string tournamentId, CancellationToken ct) =>
            Cached($"odds:{tournamentId}", Live, () => _client.GetOddsAsync(tournamentId, ct));

        [HttpGet("tournaments/{tournamentId}/course-stats")]
        public Task<ActionResult> CourseStats(string tournamentId, CancellationToken ct) =>
            Cached($"coursestats:{tournamentId}", Live, () => _client.GetCourseStatsAsync(tournamentId, ct));

        [HttpGet("tournaments/{tournamentId}/players/{playerId}/scorecard")]
        public Task<ActionResult> Scorecard(string tournamentId, string playerId, CancellationToken ct) =>
            Cached($"scorecard:{tournamentId}:{playerId}", Live, () => _client.GetScorecardAsync(tournamentId, playerId, ct));

        [HttpGet("schedule/{year:int}")]
        public Task<ActionResult> Schedule(int year, string tour = "R", CancellationToken ct = default) =>
            Cached($"schedule:{year}:{tour}", Daily, () => _client.GetScheduleAsync(year, tour, ct));

        [HttpGet("players")]
        public Task<ActionResult> Players(string tour = "R", CancellationToken ct = default) =>
            Cached($"players:{tour}", Daily, () => _client.GetPlayersAsync(tour, ct));

        [HttpGet("players/{playerId}/profile")]
        public Task<ActionResult> PlayerProfile(string playerId, CancellationToken ct) =>
            Cached($"profile:{playerId}", Daily, () => _client.GetPlayerProfileAsync(playerId, ct));

        [HttpGet("players/{playerId}/results")]
        public Task<ActionResult> PlayerResults(string playerId, int? season = null, CancellationToken ct = default) =>
            Cached($"results:{playerId}:{season}", Daily, () => _client.GetPlayerResultsAsync(playerId, season, ct));

        [HttpGet("players/{playerId}/stats")]
        public Task<ActionResult> PlayerStats(string playerId, CancellationToken ct) =>
            Cached($"pstats:{playerId}", Daily, () => _client.GetPlayerStatsAsync(playerId, ct));

        [HttpGet("stats/catalog")]
        public ActionResult StatCatalog() => Ok(_client.GetStatCatalog());

        [HttpGet("stats/{statId}")]
        public Task<ActionResult> Stat(string statId, int? year = null, string tour = "R", CancellationToken ct = default) =>
            Cached($"stat:{statId}:{year}:{tour}", Daily, async () => (await _client.GetStatAsync(statId, year, tour, ct)).Rows);

        [HttpGet("fedex-cup")]
        public Task<ActionResult> FedExCup(int? year = null, string tour = "R", CancellationToken ct = default) =>
            Cached($"fedex:{year}:{tour}", Daily, () => _client.GetFedExCupAsync(year, tour, ct));

        private async Task<ActionResult> Cached<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
        {
            try
            {
                var result = await _cache.GetOrCreateAsync($"pga:{key}", entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = ttl;
                    return factory();
                });
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (PgaTourException ex)
            {
                return StatusCode(502, new { error = "PGA Tour API request failed", details = ex.Message });
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TimeoutException or Polly.Timeout.TimeoutRejectedException)
            {
                return StatusCode(504, new { error = "PGA Tour API unavailable", details = ex.Message });
            }
        }
    }
}
