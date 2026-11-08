using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GolfStatsApp.Server.Data;
using GolfStatsApp.Server.Services;
using GolfStatsApp.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GolfStatsApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TeamsController : ControllerBase
    {
        private static readonly string[] Headers =
        {
            "Team", "Overall Avg", "Avg (Roster)", "Best Score", "Best Tournament", "Worst Score",
            "Worst Tournament", "Roster Streak", "Wins", "Majors", "Signatures", "Regulars"
        };

        private readonly AppDbContext _db;

        public TeamsController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<ActionResult<List<TeamStats>>> GetAll() =>
            await _db.Teams.AsNoTracking().OrderBy(t => t.TeamName).ToListAsync();

        [HttpGet("{id:int}")]
        public async Task<ActionResult<TeamStats>> Get(int id)
        {
            var team = await _db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            return team is null ? NotFound() : team;
        }

        [HttpPost, Authorize(Roles = "Admin")]
        public async Task<ActionResult<TeamStats>> Create(TeamStats team)
        {
            var error = Validate(team);
            if (error != null) return BadRequest(error);
            if (await _db.Teams.AnyAsync(t => t.TeamName == team.TeamName))
                return Conflict("A team with that name already exists.");

            team.Id = 0;
            _db.Teams.Add(team);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(Get), new { id = team.Id }, team);
        }

        [HttpPut("{id:int}"), Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(int id, TeamStats update)
        {
            var error = Validate(update);
            if (error != null) return BadRequest(error);

            var team = await _db.Teams.FindAsync(id);
            if (team is null) return NotFound();
            if (await _db.Teams.AnyAsync(t => t.TeamName == update.TeamName && t.Id != id))
                return Conflict("A team with that name already exists.");

            update.Id = id;
            _db.Entry(team).CurrentValues.SetValues(update);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}"), Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var team = await _db.Teams.FindAsync(id);
            if (team is null) return NotFound();
            _db.Teams.Remove(team);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpGet("export")]
        public async Task<IActionResult> Export([FromQuery] string format = "csv")
        {
            var teams = await _db.Teams.AsNoTracking().OrderBy(t => t.TeamName).ToListAsync();
            var rows = teams.Select(t => (IReadOnlyList<object?>)new object?[]
            {
                t.TeamName, t.OverallAverage, t.AverageWithRoster, t.BestTournamentScore, t.BestTournament,
                t.WorstTournamentScore, t.WorstTournament, t.LongestRosterStreak, t.Wins, t.Majors,
                t.SignatureEvents, t.Regulars
            }).ToList();

            return format.ToLowerInvariant() switch
            {
                "xlsx" => File(ExportService.ToXlsx("Team Stats", Headers, rows), ExportService.XlsxContentType, "team-stats.xlsx"),
                "csv" => File(ExportService.ToCsv(Headers, rows), ExportService.CsvContentType, "team-stats.csv"),
                _ => BadRequest("format must be csv or xlsx")
            };
        }

        private static string? Validate(TeamStats t) =>
            string.IsNullOrWhiteSpace(t.TeamName) ? "Team name is required." :
            t.TeamName.Length > 100 ? "Team name is too long." : null;
    }
}
