using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GolfStatsApp.Server.Data;
using GolfStatsApp.Server.PgaTour;
using GolfStatsApp.Server.Services;
using GolfStatsApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GolfStatsApp.Server.Controllers
{
	[ApiController, Authorize]
	[Route("api/[controller]")]
	public class LeaguesController : ControllerBase
	{
		private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
		private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMinutes(2);

		private readonly AppDbContext _db;
		private readonly UserManager<AppUser> _users;
		private readonly PgaTourApiClient _pga;
		private readonly TournamentSyncService _sync;
		private readonly IMemoryCache _cache;

		public LeaguesController(AppDbContext db, UserManager<AppUser> users, PgaTourApiClient pga, TournamentSyncService sync, IMemoryCache cache)
		{
			_db = db;
			_users = users;
			_pga = pga;
			_sync = sync;
			_cache = cache;
		}

		private string UserId => _users.GetUserId(User)!;

		[HttpGet]
		public async Task<List<LeagueSummary>> Mine()
		{
			var leagues = await _db.Leagues.AsNoTracking()
				.Include(l => l.Members)
				.Include(l => l.Tournaments)
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
			if (request.PlayersPerTeam is < 1 or > 10) return BadRequest("Starters per team must be between 1 and 10.");

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
			var league = await _db.Leagues.Include(l => l.Members).Include(l => l.Tournaments).FirstOrDefaultAsync(l => l.JoinCode == code);
			if (league is null) return NotFound("No league found for that code.");
			if (league.Members.Any(m => m.UserId == UserId)) return ToSummary(league);
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
			await _db.TeamTournamentEntries.Where(e => e.Member!.LeagueId == id).ExecuteDeleteAsync();
			_db.Leagues.Remove(league);
			await _db.SaveChangesAsync();
			return NoContent();
		}

		[HttpGet("{id:int}")]
		public async Task<ActionResult<LeagueState>> Get(int id)
		{
			var league = await LoadLeague(id);
			return league is null ? NotFound() : await BuildLeagueState(league);
		}

		[HttpPost("{id:int}/tournaments")]
		public async Task<ActionResult<LeagueState>> AddTournament(int id, AddLeagueTournamentRequest request)
		{
			var league = await LoadLeague(id);
			if (league is null) return NotFound();
			if (league.OwnerId != UserId) return Forbid();
			var tournamentId = request.TournamentId?.Trim() ?? string.Empty;
			if (tournamentId.Length is 0 or > 20) return BadRequest("A PGA TOUR tournament id is required.");
			if (request.Segment is < 1 or > 4) return BadRequest("Segment must be between 1 and 4.");
			if (!LeagueRules.EventTypes.Contains(request.EventType)) return BadRequest("Unknown event type.");
			if (league.Tournaments.Any(t => t.TournamentId == tournamentId)) return BadRequest("That tournament is already on the schedule.");

			league.Tournaments.Add(new LeagueTournament
			{
				TournamentId = tournamentId,
				Name = string.IsNullOrWhiteSpace(request.Name) ? tournamentId : request.Name.Trim()[..Math.Min(100, request.Name.Trim().Length)],
				Segment = request.Segment,
				EventType = request.EventType,
				StartDate = request.StartDate
			});
			await _db.SaveChangesAsync();
			return await BuildLeagueState(league);
		}

		[HttpDelete("{id:int}/tournaments/{tournamentId:int}")]
		public async Task<ActionResult<LeagueState>> RemoveTournament(int id, int tournamentId)
		{
			var league = await LoadLeague(id);
			if (league is null) return NotFound();
			if (league.OwnerId != UserId) return Forbid();
			var tournament = league.Tournaments.FirstOrDefault(t => t.Id == tournamentId);
			if (tournament is null) return NotFound();
			_db.LeagueTournaments.Remove(tournament);
			await _db.SaveChangesAsync();
			return await BuildLeagueState(league);
		}

		[HttpPost("{id:int}/tournaments/{tournamentId:int}/refresh")]
		public async Task<IActionResult> Refresh(int id, int tournamentId, CancellationToken ct)
		{
			var league = await LoadLeague(id);
			var tournament = league?.Tournaments.FirstOrDefault(t => t.Id == tournamentId);
			if (tournament is null) return NotFound();
			var status = await _db.SyncedTournaments.AsNoTracking().FirstOrDefaultAsync(s => s.TournamentId == tournament.TournamentId, ct);
			if (status is not null && (status.IsFinal || DateTime.UtcNow - status.LastSyncedUtc < MinRefreshInterval)) return NoContent();
			try
			{
				await _sync.SyncAsync(tournament.TournamentId, ct);
			}
			catch (Exception ex) when (ex is PgaTourException or System.Net.Http.HttpRequestException or TimeoutException or Polly.Timeout.TimeoutRejectedException)
			{
				return StatusCode(502, "Live scoring is unavailable right now.");
			}
			return NoContent();
		}

		[HttpGet("{id:int}/tournaments/{tournamentId:int}/lineup")]
		public async Task<ActionResult<LineupState>> GetLineup(int id, int tournamentId, CancellationToken ct)
		{
			var league = await LoadLeague(id);
			var tournament = league?.Tournaments.FirstOrDefault(t => t.Id == tournamentId);
			if (league is null || tournament is null) return NotFound();
			var me = league.Members.First(m => m.UserId == UserId);
			return await BuildLineupState(league, tournament, me, ct);
		}

		[HttpPut("{id:int}/tournaments/{tournamentId:int}/lineup")]
		public async Task<ActionResult<LineupState>> SaveLineup(int id, int tournamentId, SaveLineupRequest request, CancellationToken ct)
		{
			var league = await LoadLeague(id);
			var tournament = league?.Tournaments.FirstOrDefault(t => t.Id == tournamentId);
			if (league is null || tournament is null) return NotFound();
			var me = league.Members.First(m => m.UserId == UserId);

			var status = await GetStatus(tournament.TournamentId);
			var fromRound = NextEditableRound(status);
			if (fromRound == 0) return BadRequest("This tournament is complete; the lineup can no longer change.");

			var field = await GetField(tournament.TournamentId, ct);
			if (field is null || field.Count == 0) return BadRequest("The field has not been announced yet.");

			var starters = request.StarterIds.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
			var bench = request.BenchIds.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
			if (starters.Count != league.PlayersPerTeam) return BadRequest($"Select exactly {league.PlayersPerTeam} starters.");
			if (bench.Count > LeagueRules.BenchSlots) return BadRequest($"At most {LeagueRules.BenchSlots} bench golfers are allowed.");
			var all = starters.Concat(bench).ToList();
			if (all.Distinct().Count() != all.Count) return BadRequest("A golfer can only appear once in your roster.");
			var fieldById = field.ToDictionary(f => f.PlayerId);
			var unknown = all.FirstOrDefault(p => !fieldById.ContainsKey(p));
			if (unknown is not null) return BadRequest("Every golfer must be in this tournament's field.");

			var entry = await _db.TeamTournamentEntries.Include(e => e.Slots)
				.FirstOrDefaultAsync(e => e.LeagueTournamentId == tournament.Id && e.MemberId == me.Id, ct);
			if (entry is null)
			{
				entry = new TeamTournamentEntry { LeagueTournamentId = tournament.Id, MemberId = me.Id };
				_db.TeamTournamentEntries.Add(entry);
			}

			var data = await LoadTournamentData(tournament, status, ct);
			var usedHere = FantasyScoring.StartsUsed(entry.Slots.Where(s => s.FromRound < fromRound), status?.StartedRound ?? 0, data.CutKnown, data.MissedCut);
			var usedElsewhere = await StartsUsedInSegment(league, tournament, me.Id, ct);
			foreach (var p in all)
			{
				if (usedHere.Contains(p)) continue;
				if (usedElsewhere.GetValueOrDefault(p) >= LeagueRules.StartsPerSegment)
					return BadRequest($"{fieldById[p].Name} has already used {LeagueRules.StartsPerSegment} starts this segment.");
				if (fromRound >= 3 && data.MissedCut(p))
					return BadRequest($"{fieldById[p].Name} missed the cut.");
			}

			var captainLocked = CaptainLocked(entry, status);
			if (captainLocked)
			{
				if (request.CaptainId != entry.CaptainPlayerId) return BadRequest("The captain cannot be changed once the tournament is under way.");
			}
			else if (request.CaptainId is not null)
			{
				if (!starters.Contains(request.CaptainId)) return BadRequest("The captain must be one of your starters.");
				entry.CaptainPlayerId = request.CaptainId;
				entry.CaptainName = fieldById[request.CaptainId].Name;
				entry.CaptainFromRound = fromRound;
			}
			else
			{
				entry.CaptainPlayerId = null;
				entry.CaptainName = null;
			}

			_db.LineupSlots.RemoveRange(entry.Slots.Where(s => s.FromRound >= fromRound));
			entry.Slots.RemoveAll(s => s.FromRound >= fromRound);
			entry.Slots.AddRange(starters.Select(p => new LineupSlot { FromRound = fromRound, PlayerId = p, PlayerName = fieldById[p].Name, IsStarter = true }));
			entry.Slots.AddRange(bench.Select((p, i) => new LineupSlot { FromRound = fromRound, PlayerId = p, PlayerName = fieldById[p].Name, BenchPriority = i + 1 }));

			try
			{
				await _db.SaveChangesAsync(ct);
			}
			catch (DbUpdateException)
			{
				return Conflict("Your lineup was changed elsewhere. Refresh and try again.");
			}
			return await BuildLineupState(league, tournament, me, ct);
		}

		[HttpGet("{id:int}/tournaments/{tournamentId:int}/scores")]
		public async Task<ActionResult<List<TeamTournamentScore>>> TournamentScores(int id, int tournamentId, CancellationToken ct)
		{
			var league = await LoadLeague(id);
			var tournament = league?.Tournaments.FirstOrDefault(t => t.Id == tournamentId);
			if (league is null || tournament is null) return NotFound();
			var status = await GetStatus(tournament.TournamentId);
			var data = await LoadTournamentData(tournament, status, ct);
			var entries = await _db.TeamTournamentEntries.AsNoTracking().Include(e => e.Slots)
				.Where(e => e.LeagueTournamentId == tournament.Id).ToListAsync(ct);

			// Other members' lineups stay hidden until play begins.
			var started = (status?.StartedRound ?? 0) > 0;
			return league.Members.Select(m =>
			{
				var entry = entries.FirstOrDefault(e => e.MemberId == m.Id);
				var golfers = entry is null || (!started && m.UserId != UserId) ? new List<GolferScoreDto>() : FantasyScoring.ScoreEntry(entry, data);
				return new TeamTournamentScore
				{
					MemberId = m.Id,
					DisplayName = m.User?.DisplayName ?? "Unknown",
					Golfers = golfers,
					Total = golfers.Sum(g => g.Total)
				};
			}).OrderByDescending(s => s.Total).ThenBy(s => s.DisplayName).ToList();
		}

		[HttpGet("{id:int}/leaderboard")]
		public async Task<ActionResult<List<LeaderboardEntry>>> Leaderboard(int id, CancellationToken ct)
		{
			var league = await LoadLeague(id);
			return league is null ? NotFound() : await BuildLeaderboard(league, ct);
		}

		[HttpGet("{id:int}/leaderboard/export")]
		public async Task<IActionResult> ExportLeaderboard(int id, [FromQuery] string format = "csv", CancellationToken ct = default)
		{
			var league = await LoadLeague(id);
			if (league is null) return NotFound();

			var board = await BuildLeaderboard(league, ct);
			var headers = new[] { "Rank", "Member", "Points", "Tournaments" };
			var rows = board.Select(e => (IReadOnlyList<object?>)new object?[]
			{
				e.Rank, e.DisplayName, e.Points, string.Join("; ", e.Tournaments.Select(t => $"{t.Name} ({t.Points:0.##})"))
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
				.Include(l => l.Tournaments)
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
			TournamentCount = l.Tournaments.Count,
			IsOwner = l.OwnerId == UserId
		};

		private async Task<LeagueState> BuildLeagueState(League league)
		{
			var ids = league.Tournaments.Select(t => t.TournamentId).ToList();
			var statuses = await _db.SyncedTournaments.AsNoTracking().Where(s => ids.Contains(s.TournamentId)).ToDictionaryAsync(s => s.TournamentId);
			return new LeagueState
			{
				League = ToSummary(league),
				Members = league.Members.OrderBy(m => m.User?.DisplayName).Select(m => new LeagueMemberDto
				{
					Id = m.Id,
					DisplayName = m.User?.DisplayName ?? "Unknown",
					IsOwner = m.UserId == league.OwnerId,
					IsMe = m.UserId == UserId
				}).ToList(),
				Tournaments = OrderedTournaments(league).Select(t => ToDto(t, statuses.GetValueOrDefault(t.TournamentId))).ToList()
			};
		}

		private static IEnumerable<LeagueTournament> OrderedTournaments(League league) =>
			league.Tournaments.OrderBy(t => t.StartDate ?? DateTime.MaxValue).ThenBy(t => t.Id);

		private static LeagueTournamentDto ToDto(LeagueTournament t, SyncedTournament? s) => new()
		{
			Id = t.Id,
			TournamentId = t.TournamentId,
			Name = t.Name,
			Segment = t.Segment,
			EventType = t.EventType,
			StartDate = t.StartDate,
			StartedRound = s?.StartedRound ?? 0,
			Status = s is null || s.StartedRound == 0 ? TournamentStatuses.Upcoming
				: s.IsFinal ? TournamentStatuses.Final : TournamentStatuses.InProgress
		};

		private Task<SyncedTournament?> GetStatus(string tournamentId) =>
			_db.SyncedTournaments.AsNoTracking().FirstOrDefaultAsync(s => s.TournamentId == tournamentId);

		// A saved lineup applies to the next round that has not begun.
		private static int NextEditableRound(SyncedTournament? s) =>
			s is null ? 1 : s.IsFinal || s.StartedRound >= FantasyScoring.Rounds ? 0 : s.StartedRound + 1;

		private static bool CaptainLocked(TeamTournamentEntry entry, SyncedTournament? s) =>
			entry.CaptainPlayerId is not null && entry.CaptainFromRound <= (s?.StartedRound ?? 0);

		private async Task<List<FieldGolferDto>?> GetField(string tournamentId, CancellationToken ct)
		{
			try
			{
				return await _cache.GetOrCreateAsync($"league-field:{tournamentId}", async entry =>
				{
					entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
					var field = await _pga.GetFieldAsync(tournamentId, ct: ct);
					return field.Where(f => !string.IsNullOrEmpty(f.PlayerId) && !f.Alternate && f.Withdrawn != true)
						.Select(f => new FieldGolferDto { PlayerId = f.PlayerId!, Name = f.DisplayName ?? f.PlayerId! })
						.OrderBy(f => f.Name).ToList();
				});
			}
			catch (Exception ex) when (ex is PgaTourException or System.Net.Http.HttpRequestException or TimeoutException or Polly.Timeout.TimeoutRejectedException)
			{
				return null;
			}
		}

		private async Task<FantasyScoring.TournamentData> LoadTournamentData(LeagueTournament tournament, SyncedTournament? status, CancellationToken ct)
		{
			var rounds = await _db.GolferRounds.AsNoTracking().Where(r => r.TournamentId == tournament.TournamentId).ToListAsync(ct);
			var golfers = await _db.TournamentGolfers.AsNoTracking().Where(g => g.TournamentId == tournament.TournamentId).ToDictionaryAsync(g => g.PlayerId, ct);
			var complete = TournamentSyncService.ParseRounds(status?.CompleteRounds);
			var bonuses = new Dictionary<(string, int), double>();
			foreach (var group in rounds.Where(r => complete.Contains(r.Round)).GroupBy(r => r.Round))
				foreach (var (playerId, bonus) in FantasyScoring.RoundBonuses(group))
					bonuses[(playerId, group.Key)] = bonus;

			return new FantasyScoring.TournamentData(
				status?.StartedRound ?? 0,
				status?.IsFinal ?? false,
				status?.CutKnown ?? false,
				tournament.Segment,
				tournament.EventType,
				rounds.ToDictionary(r => (r.PlayerId, r.Round)),
				bonuses,
				golfers);
		}

		/// <summary>Starts used per golfer by a member in the other tournaments of the same segment.</summary>
		private async Task<Dictionary<string, int>> StartsUsedInSegment(League league, LeagueTournament current, int memberId, CancellationToken ct)
		{
			var others = league.Tournaments.Where(t => t.Segment == current.Segment && t.Id != current.Id).ToList();
			var otherIds = others.Select(t => t.Id).ToList();
			var entries = await _db.TeamTournamentEntries.AsNoTracking().Include(e => e.Slots)
				.Where(e => e.MemberId == memberId && otherIds.Contains(e.LeagueTournamentId)).ToListAsync(ct);
			var counts = new Dictionary<string, int>();
			foreach (var entry in entries)
			{
				var tournament = others.First(t => t.Id == entry.LeagueTournamentId);
				var status = await GetStatus(tournament.TournamentId);
				var data = await LoadTournamentData(tournament, status, ct);
				foreach (var p in FantasyScoring.StartsUsed(entry.Slots, data.StartedRound, data.CutKnown, data.MissedCut))
					counts[p] = counts.GetValueOrDefault(p) + 1;
			}
			return counts;
		}

		private async Task<LineupState> BuildLineupState(League league, LeagueTournament tournament, LeagueMember me, CancellationToken ct)
		{
			var status = await GetStatus(tournament.TournamentId);
			var data = await LoadTournamentData(tournament, status, ct);
			var entry = await _db.TeamTournamentEntries.AsNoTracking().Include(e => e.Slots)
				.FirstOrDefaultAsync(e => e.LeagueTournamentId == tournament.Id && e.MemberId == me.Id, ct);
			var nextRound = NextEditableRound(status);
			var current = entry is null ? new List<LineupSlot>()
				: FantasyScoring.EffectiveSlots(entry.Slots, nextRound == 0 ? FantasyScoring.Rounds : nextRound);

			var state = new LineupState
			{
				Tournament = ToDto(tournament, status),
				StarterSlots = league.PlayersPerTeam,
				NextEditableRound = nextRound,
				CaptainLocked = entry is not null && CaptainLocked(entry, status),
				CaptainPlayerId = entry?.CaptainPlayerId,
				Starters = current.Where(s => s.IsStarter).Select(s => new LineupPlayerDto { PlayerId = s.PlayerId, Name = s.PlayerName }).ToList(),
				Bench = current.Where(s => !s.IsStarter).OrderBy(s => s.BenchPriority)
					.Select(s => new LineupPlayerDto { PlayerId = s.PlayerId, Name = s.PlayerName, BenchPriority = s.BenchPriority }).ToList()
			};

			if (entry is not null && data.StartedRound > 0)
			{
				var golfers = FantasyScoring.ScoreEntry(entry, data);
				state.Score = new TeamTournamentScore
				{
					MemberId = me.Id,
					DisplayName = me.User?.DisplayName ?? "Unknown",
					Golfers = golfers,
					Total = golfers.Sum(g => g.Total)
				};
			}

			var field = nextRound == 0 ? null : await GetField(tournament.TournamentId, ct);
			state.FieldAvailable = field is { Count: > 0 };
			if (field is { Count: > 0 })
			{
				var used = await StartsUsedInSegment(league, tournament, me.Id, ct);
				var (previous, season) = await GolferHistory(league, tournament, ct);
				state.Field = field.Select(f => new FieldGolferDto
				{
					PlayerId = f.PlayerId,
					Name = f.Name,
					Position = data.Golfers.GetValueOrDefault(f.PlayerId)?.Position,
					IsOut = FantasyScoring.IsOut(data.Golfers.GetValueOrDefault(f.PlayerId)?.Position),
					StartsUsed = used.GetValueOrDefault(f.PlayerId),
					StartsRemaining = Math.Max(0, LeagueRules.StartsPerSegment - used.GetValueOrDefault(f.PlayerId)),
					PreviousEventPoints = previous?.GetValueOrDefault(f.PlayerId),
					SeasonPoints = season.GetValueOrDefault(f.PlayerId)
				}).ToList();
			}
			return state;
		}

		/// <summary>Fantasy points each golfer earned (as an every-round starter) in the previous league event and season to date.</summary>
		private async Task<(Dictionary<string, double>? Previous, Dictionary<string, double> Season)> GolferHistory(
			League league, LeagueTournament current, CancellationToken ct)
		{
			var season = new Dictionary<string, double>();
			Dictionary<string, double>? previous = null;
			foreach (var t in OrderedTournaments(league).TakeWhile(t => t.Id != current.Id))
			{
				var status = await GetStatus(t.TournamentId);
				if (status is null || status.StartedRound == 0) continue;
				var data = await LoadTournamentData(t, status, ct);
				previous = data.Golfers.Keys.ToDictionary(id => id, id =>
				{
					var everyRound = new TeamTournamentEntry { Slots = { new LineupSlot { FromRound = 1, PlayerId = id, IsStarter = true } } };
					return FantasyScoring.ScoreEntry(everyRound, data).Sum(s => s.Total);
				});
				foreach (var (id, points) in previous) season[id] = season.GetValueOrDefault(id) + points;
			}
			return (previous, season);
		}

		private async Task<List<LeaderboardEntry>> BuildLeaderboard(League league, CancellationToken ct)
		{
			var tournaments = OrderedTournaments(league).ToList();
			var ids = tournaments.Select(t => t.Id).ToList();
			var entries = await _db.TeamTournamentEntries.AsNoTracking().Include(e => e.Slots)
				.Where(e => ids.Contains(e.LeagueTournamentId)).ToListAsync(ct);

			var results = league.Members.ToDictionary(m => m.Id, m => new LeaderboardEntry
			{
				MemberId = m.Id,
				DisplayName = m.User?.DisplayName ?? "Unknown"
			});
			foreach (var t in tournaments)
			{
				var status = await GetStatus(t.TournamentId);
				if (status is null || status.StartedRound == 0) continue;
				var data = await LoadTournamentData(t, status, ct);
				foreach (var entry in entries.Where(e => e.LeagueTournamentId == t.Id && results.ContainsKey(e.MemberId)))
				{
					var points = FantasyScoring.ScoreEntry(entry, data).Sum(g => g.Total);
					results[entry.MemberId].Tournaments.Add(new LeaderboardTournament { LeagueTournamentId = t.Id, Name = t.Name, Segment = t.Segment, Points = points });
					results[entry.MemberId].Points += points;
				}
			}

			var ordered = results.Values.OrderByDescending(e => e.Points).ThenBy(e => e.DisplayName).ToList();
			for (var i = 0; i < ordered.Count; i++)
				ordered[i].Rank = i > 0 && ordered[i].Points == ordered[i - 1].Points ? ordered[i - 1].Rank : i + 1;
			return ordered;
		}
	}
}
