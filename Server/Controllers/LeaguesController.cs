using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GolfStatsApp.Server.Data;
using GolfStatsApp.Server.Services;
using GolfStatsApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GolfStatsApp.Server.Controllers
{
    [ApiController, Authorize]
    [Route("api/[controller]")]
    public class LeaguesController : ControllerBase
    {
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private readonly AppDbContext _db;
        private readonly UserManager<AppUser> _users;
        private readonly PlayerDataService _players;

        public LeaguesController(AppDbContext db, UserManager<AppUser> users, PlayerDataService players)
        {
            _db = db;
            _users = users;
            _players = players;
        }

        private string UserId => _users.GetUserId(User)!;

        [HttpGet]
        public async Task<List<LeagueSummary>> Mine()
        {
            var leagues = await _db.Leagues.AsNoTracking()
                .Include(l => l.Members)
                .Where(l => l.Members.Any(m => m.UserId == UserId))
                .OrderBy(l => l.Name)
                .ToListAsync();
            return leagues.Select(ToSummary).ToList();
        }

        [HttpPost]
        public async Task<ActionResult<LeagueSummary>> Create(CreateLeagueRequest request)
        {
            var name = request.Name?.Trim() ?? string.Empty;
            if (name.Length is 0 or > 60) return BadRequest("League name must be 1-60 characters.");
            if (request.PlayersPerTeam is < 1 or > 10) return BadRequest("Players per team must be between 1 and 10.");

            var league = new League
            {
                Name = name,
                PlayersPerTeam = request.PlayersPerTeam,
                OwnerId = UserId,
                JoinCode = await NewJoinCode()
            };
            league.Members.Add(new LeagueMember { UserId = UserId });
            _db.Leagues.Add(league);
            await _db.SaveChangesAsync();
            return ToSummary(league);
        }

        [HttpPost("join")]
        public async Task<ActionResult<LeagueSummary>> Join(JoinLeagueRequest request)
        {
            var code = request.JoinCode?.Trim().ToUpperInvariant() ?? string.Empty;
            var league = await _db.Leagues.Include(l => l.Members).FirstOrDefaultAsync(l => l.JoinCode == code);
            if (league is null) return NotFound("No league found for that code.");
            if (league.Members.Any(m => m.UserId == UserId)) return ToSummary(league);
            if (league.DraftStatus != DraftStatuses.NotStarted) return BadRequest("The draft has already started.");
            if (league.Members.Count >= 20) return BadRequest("League is full.");

            league.Members.Add(new LeagueMember { UserId = UserId });
            await _db.SaveChangesAsync();
            return ToSummary(league);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var league = await _db.Leagues.FindAsync(id);
            if (league is null || league.OwnerId != UserId) return NotFound();
            _db.Leagues.Remove(league);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpGet("{id:int}/draft")]
        public async Task<ActionResult<DraftState>> GetDraft(int id)
        {
            var league = await LoadLeague(id);
            return league is null ? NotFound() : await BuildDraftState(league);
        }

        [HttpPost("{id:int}/draft/start")]
        public async Task<ActionResult<DraftState>> StartDraft(int id)
        {
            var league = await LoadLeague(id);
            if (league is null) return NotFound();
            if (league.OwnerId != UserId) return Forbid();
            if (league.DraftStatus != DraftStatuses.NotStarted) return BadRequest("The draft has already started.");
            if (league.Members.Count < 2) return BadRequest("At least 2 members are needed to draft.");

            var order = league.Members.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToList();
            for (var i = 0; i < order.Count; i++) order[i].DraftPosition = i + 1;
            league.DraftStatus = DraftStatuses.InProgress;
            await _db.SaveChangesAsync();
            return await BuildDraftState(league);
        }

        [HttpPost("{id:int}/draft/pick")]
        public async Task<ActionResult<DraftState>> MakePick(int id, MakePickRequest request)
        {
            var league = await LoadLeague(id);
            if (league is null) return NotFound();
            if (league.DraftStatus != DraftStatuses.InProgress) return BadRequest("The draft is not in progress.");

            var ordered = league.Members.OrderBy(m => m.DraftPosition).ToList();
            var pickNumber = league.Picks.Count;
            var onClock = ordered[DraftOrder.MemberIndexForPick(pickNumber, ordered.Count)];
            if (onClock.UserId != UserId) return BadRequest("It is not your turn.");

            var pool = await _players.GetPlayersAsync();
            if (pool.Count == 0) return StatusCode(503, "Player pool is unavailable right now.");
            var player = pool.FirstOrDefault(p => string.Equals(p.PlayerName, request.PlayerName?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (player is null) return BadRequest("Unknown player.");
            if (league.Picks.Any(p => string.Equals(p.PlayerName, player.PlayerName, StringComparison.OrdinalIgnoreCase)))
                return BadRequest("That player has already been drafted.");

            var pick = new DraftPick { LeagueId = id, MemberId = onClock.Id, PickNumber = pickNumber, PlayerName = player.PlayerName };
            league.Picks.Add(pick);
            if (league.Picks.Count >= ordered.Count * league.PlayersPerTeam)
                league.DraftStatus = DraftStatuses.Complete;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Conflict("Another pick was just made. Refresh and try again.");
            }
            return await BuildDraftState(league);
        }

        [HttpGet("{id:int}/leaderboard")]
        public async Task<ActionResult<List<LeaderboardEntry>>> Leaderboard(int id)
        {
            var league = await LoadLeague(id);
            return league is null ? NotFound() : await BuildLeaderboard(league);
        }

        [HttpGet("{id:int}/leaderboard/export")]
        public async Task<IActionResult> ExportLeaderboard(int id, [FromQuery] string format = "csv")
        {
            var league = await LoadLeague(id);
            if (league is null) return NotFound();

            var board = await BuildLeaderboard(league);
            var headers = new[] { "Rank", "Member", "Points", "Players" };
            var rows = board.Select(e => (IReadOnlyList<object?>)new object?[]
            {
                e.Rank, e.DisplayName, e.Points, string.Join("; ", e.Players.Select(p => $"{p.PlayerName} ({p.Points:0.##})"))
            }).ToList();

            return format.ToLowerInvariant() switch
            {
                "xlsx" => File(ExportService.ToXlsx("Leaderboard", headers, rows), ExportService.XlsxContentType, "leaderboard.xlsx"),
                "csv" => File(ExportService.ToCsv(headers, rows), ExportService.CsvContentType, "leaderboard.csv"),
                _ => BadRequest("format must be csv or xlsx")
            };
        }

        private async Task<League?> LoadLeague(int id)
        {
            var league = await _db.Leagues
                .Include(l => l.Members).ThenInclude(m => m.User)
                .Include(l => l.Picks)
                .FirstOrDefaultAsync(l => l.Id == id);
            return league != null && league.Members.Any(m => m.UserId == UserId) ? league : null;
        }

        private async Task<string> NewJoinCode()
        {
            while (true)
            {
                var code = new string(Enumerable.Range(0, 6)
                    .Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]).ToArray());
                if (!await _db.Leagues.AnyAsync(l => l.JoinCode == code)) return code;
            }
        }

        private LeagueSummary ToSummary(League l) => new()
        {
            Id = l.Id,
            Name = l.Name,
            JoinCode = l.JoinCode,
            MemberCount = l.Members.Count,
            PlayersPerTeam = l.PlayersPerTeam,
            DraftStatus = l.DraftStatus,
            IsOwner = l.OwnerId == UserId
        };

        private Task<DraftState> BuildDraftState(League league)
        {
            var members = league.Members.OrderBy(m => m.DraftPosition ?? int.MaxValue).ThenBy(m => m.Id).ToList();
            var names = members.ToDictionary(m => m.Id, m => m.User?.DisplayName ?? "Unknown");
            var total = members.Count * league.PlayersPerTeam;

            var state = new DraftState
            {
                League = ToSummary(league),
                TotalPicks = total,
                Members = members.Select(m => new LeagueMemberDto
                {
                    Id = m.Id,
                    DisplayName = names[m.Id],
                    DraftPosition = m.DraftPosition,
                    IsOwner = m.UserId == league.OwnerId,
                    IsMe = m.UserId == UserId
                }).ToList(),
                Picks = league.Picks.OrderBy(p => p.PickNumber).Select(p => new DraftPickDto
                {
                    PickNumber = p.PickNumber + 1,
                    MemberId = p.MemberId,
                    MemberName = names.GetValueOrDefault(p.MemberId, "Unknown"),
                    PlayerName = p.PlayerName
                }).ToList()
            };

            if (league.DraftStatus == DraftStatuses.InProgress)
            {
                var onClock = members[DraftOrder.MemberIndexForPick(league.Picks.Count, members.Count)];
                state.CurrentMemberId = onClock.Id;
                state.CurrentMemberName = names[onClock.Id];
                state.IsMyTurn = onClock.UserId == UserId;
            }
            return Task.FromResult(state);
        }

        private async Task<List<LeaderboardEntry>> BuildLeaderboard(League league)
        {
            var points = (await _db.PlayerPoints.AsNoTracking().ToListAsync())
                .GroupBy(p => p.PlayerName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Points), StringComparer.OrdinalIgnoreCase);

            var entries = league.Members.Select(m =>
            {
                var picks = league.Picks.Where(p => p.MemberId == m.Id).OrderBy(p => p.PickNumber)
                    .Select(p => new LeaderboardPlayer { PlayerName = p.PlayerName, Points = points.GetValueOrDefault(p.PlayerName) })
                    .ToList();
                return new LeaderboardEntry
                {
                    MemberId = m.Id,
                    DisplayName = m.User?.DisplayName ?? "Unknown",
                    Players = picks,
                    Points = picks.Sum(p => p.Points)
                };
            }).OrderByDescending(e => e.Points).ThenBy(e => e.DisplayName).ToList();

            for (var i = 0; i < entries.Count; i++)
                entries[i].Rank = i > 0 && entries[i].Points == entries[i - 1].Points ? entries[i - 1].Rank : i + 1;
            return entries;
        }
    }
}
