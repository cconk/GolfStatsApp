using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GolfStatsApp.Server.Data;
using GolfStatsApp.Server.PgaTour;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GolfStatsApp.Server.Services
{
    /// <summary>Pulls PGA TOUR leaderboards and hole-by-hole scorecards into the database for league scoring.</summary>
    public class TournamentSyncService
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);

        private readonly AppDbContext _db;
        private readonly PgaTourApiClient _pga;

        public TournamentSyncService(AppDbContext db, PgaTourApiClient pga)
        {
            _db = db;
            _pga = pga;
        }

        public async Task SyncAsync(string tournamentId, CancellationToken ct = default)
        {
            await Gate.WaitAsync(ct);
            try
            {
                await SyncCoreAsync(tournamentId, ct);
            }
            finally
            {
                Gate.Release();
            }
        }

        private async Task SyncCoreAsync(string tournamentId, CancellationToken ct)
        {
            var leaderboard = await _pga.GetLeaderboardAsync(tournamentId, ct);
            var status = await _db.SyncedTournaments.FindAsync(new object[] { tournamentId }, ct);
            if (status is null)
            {
                status = new SyncedTournament { TournamentId = tournamentId };
                _db.SyncedTournaments.Add(status);
            }

            var golfers = await _db.TournamentGolfers.Where(g => g.TournamentId == tournamentId).ToDictionaryAsync(g => g.PlayerId, ct);
            var rounds = await _db.GolferRounds.Where(r => r.TournamentId == tournamentId).ToListAsync(ct);
            var roundsByKey = rounds.ToDictionary(r => (r.PlayerId, r.Round));

            var anyStarted = leaderboard.Any(HasStarted);
            foreach (var row in leaderboard.Where(r => !string.IsNullOrEmpty(r.PlayerId)))
            {
                var id = row.PlayerId!;
                if (!golfers.TryGetValue(id, out var golfer))
                {
                    golfer = new TournamentGolfer { TournamentId = tournamentId, PlayerId = id };
                    golfers[id] = golfer;
                    _db.TournamentGolfers.Add(golfer);
                }
                var wasOut = FantasyScoring.IsOut(golfer.Position);
                golfer.PlayerName = row.DisplayName ?? golfer.PlayerName;
                golfer.Position = row.Position;
                golfer.State = row.PlayerState;

                // Golfers who were already out keep their stored rounds.
                if (!anyStarted || (wasOut && FantasyScoring.IsOut(row.Position))) continue;

                var card = await _pga.GetScorecardAsync(tournamentId, id, ct);
                foreach (var group in card.Where(h => h.RoundNumber is > 0 and <= FantasyScoring.Rounds).GroupBy(h => h.RoundNumber!.Value))
                {
                    var played = group.Where(h => h.Par is > 0 && int.TryParse(h.Score, out var s) && s > 0)
                        .Select(h => (Par: h.Par!.Value, Strokes: int.Parse(h.Score!))).ToList();
                    if (played.Count == 0) continue;

                    if (!roundsByKey.TryGetValue((id, group.Key), out var round))
                    {
                        round = new GolferRound { TournamentId = tournamentId, PlayerId = id, Round = group.Key };
                        roundsByKey[(id, group.Key)] = round;
                        _db.GolferRounds.Add(round);
                    }
                    round.Holes = played.Count;
                    round.ToPar = played.Sum(h => h.Strokes - h.Par);
                    round.HolePoints = played.Sum(h => FantasyScoring.HolePoints(h.Strokes, h.Par));
                    round.BogeyFree = played.Count >= 18 && played.All(h => h.Strokes <= h.Par);
                }
            }

            var all = roundsByKey.Values.ToList();
            status.StartedRound = all.Count == 0 ? 0 : all.Max(r => r.Round);
            status.CutKnown = golfers.Values.Any(g => FantasyScoring.MissedCut(g.Position));
            var complete = new List<int>();
            for (var r = 1; r <= FantasyScoring.Rounds; r++)
            {
                var inRound = all.Where(x => x.Round == r).ToList();
                if (inRound.Count == 0) break;
                var stillPlaying = inRound.Any(x => x.Holes < 18 && !FantasyScoring.IsOut(golfers.GetValueOrDefault(x.PlayerId)?.Position));
                if (stillPlaying || (r == 2 && !status.CutKnown && status.StartedRound < 3)) break;
                complete.Add(r);
            }
            status.CompleteRounds = string.Join(",", complete);
            status.IsFinal = complete.Contains(FantasyScoring.Rounds);
            status.LastSyncedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        private static bool HasStarted(TournamentLeaderboardRow row) =>
            row.TotalStrokes is > 0 || row.CurrentRound is > 1 ||
            (row.Thru is { Length: > 0 } thru && (thru.StartsWith('F') || (int.TryParse(thru.TrimEnd('*'), out var t) && t > 0)));

        public static HashSet<int> ParseRounds(string? complete) =>
            (complete ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet();
    }

    /// <summary>Periodically syncs every scheduled league tournament that has started and is not final.</summary>
    public class TournamentSyncBackgroundService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<TournamentSyncBackgroundService> _logger;

        public TournamentSyncBackgroundService(IServiceScopeFactory scopes, ILogger<TournamentSyncBackgroundService> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);
            do
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var sync = scope.ServiceProvider.GetRequiredService<TournamentSyncService>();
                    var cutoff = DateTime.UtcNow.AddDays(1);
                    var final = await db.SyncedTournaments.Where(t => t.IsFinal).Select(t => t.TournamentId).ToListAsync(stoppingToken);
                    var ids = await db.LeagueTournaments
                        .Where(t => (t.StartDate == null || t.StartDate <= cutoff) && !final.Contains(t.TournamentId))
                        .Select(t => t.TournamentId).Distinct().ToListAsync(stoppingToken);
                    foreach (var id in ids)
                    {
                        try { await sync.SyncAsync(id, stoppingToken); }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogWarning(ex, "Sync failed for tournament {TournamentId}", id);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Tournament sync pass failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
