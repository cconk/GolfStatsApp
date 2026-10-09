using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GolfStatsApp.Server.PgaTour;
using Microsoft.AspNetCore.Mvc;

namespace GolfStatsApp.Server.Controllers
{
    /// <summary>Live smoke/contract check of every PGA Tour client call. Useful for detecting upstream API changes.</summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PgaTourHealthController : ControllerBase
    {
        private readonly PgaTourApiClient _client;

        public PgaTourHealthController(PgaTourApiClient client)
        {
            _client = client;
        }

        [HttpGet]
        public async Task<ActionResult> Check(string playerId = "46046", CancellationToken ct = default)
        {
            var results = new List<object>();
            var tid = string.Empty;
            var year = DateTime.Now.Year;

            async Task Run(string name, Func<Task<int>> call)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var count = await call();
                    results.Add(new { name, ok = count > 0, count, ms = sw.ElapsedMilliseconds });
                }
                catch (Exception ex)
                {
                    results.Add(new { name, ok = false, error = ex.Message, ms = sw.ElapsedMilliseconds });
                }
            }

            await Run("currentTournament", async () => { tid = await _client.GetCurrentTournamentIdAsync(ct: ct); return 1; });
            await Run("leaderboard", async () => (await _client.GetLeaderboardAsync(tid, ct)).Count);
            await Run("field", async () => (await _client.GetFieldAsync(tid, ct: ct)).Count);
            await Run("odds", async () => (await _client.GetOddsAsync(tid, ct)).Count);
            await Run("courseStats", async () => (await _client.GetCourseStatsAsync(tid, ct)).Count);
            await Run("scorecard", async () => (await _client.GetScorecardAsync(tid, playerId, ct)).Count);
            await Run("schedule", async () => (await _client.GetScheduleAsync(year, ct: ct)).Count);
            await Run("players", async () => (await _client.GetPlayersAsync(ct: ct)).Count);
            await Run("playerProfile", async () => (await _client.GetPlayerProfileAsync(playerId, ct)).PlayerId != null ? 1 : 0);
            await Run("playerStats", async () => (await _client.GetPlayerStatsAsync(playerId, ct)).Count);
            await Run("playerResults", async () => (await _client.GetPlayerResultsAsync(playerId, year, ct)).Count);
            await Run("stat", async () => (await _client.GetStatAsync("02675", year, ct: ct)).Rows.Count);
            await Run("fedexCup", async () => (await _client.GetFedExCupAsync(year, ct: ct)).Count);
            await Run("statCatalog", () => Task.FromResult(_client.GetStatCatalog().Count));

            return Ok(new { tournamentId = tid, results });
        }
    }
}
