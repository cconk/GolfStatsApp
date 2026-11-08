using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GolfStatsApp.Server.Data;
using GolfStatsApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GolfStatsApp.Server.Controllers
{
    [ApiController, Authorize(Roles = "Admin")]
    [Route("api/player-points")]
    public class PlayerPointsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public PlayerPointsController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<List<PlayerPointsEntry>> GetAll() =>
            (await _db.PlayerPoints.AsNoTracking().OrderByDescending(p => p.Id).ToListAsync())
                .Select(p => new PlayerPointsEntry { Id = p.Id, PlayerName = p.PlayerName, Tournament = p.Tournament, Points = p.Points })
                .ToList();

        [HttpPost]
        public async Task<ActionResult<PlayerPointsEntry>> Create(PlayerPointsEntry entry)
        {
            var error = Validate(entry);
            if (error != null) return BadRequest(error);

            var row = new PlayerPoints { PlayerName = entry.PlayerName.Trim(), Tournament = entry.Tournament.Trim(), Points = entry.Points };
            _db.PlayerPoints.Add(row);
            await _db.SaveChangesAsync();
            entry.Id = row.Id;
            return entry;
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, PlayerPointsEntry entry)
        {
            var error = Validate(entry);
            if (error != null) return BadRequest(error);

            var row = await _db.PlayerPoints.FindAsync(id);
            if (row is null) return NotFound();
            row.PlayerName = entry.PlayerName.Trim();
            row.Tournament = entry.Tournament.Trim();
            row.Points = entry.Points;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var row = await _db.PlayerPoints.FindAsync(id);
            if (row is null) return NotFound();
            _db.PlayerPoints.Remove(row);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private static string? Validate(PlayerPointsEntry e) =>
            string.IsNullOrWhiteSpace(e.PlayerName) ? "Player name is required." :
            string.IsNullOrWhiteSpace(e.Tournament) ? "Tournament is required." : null;
    }
}
