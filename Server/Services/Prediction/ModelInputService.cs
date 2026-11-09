using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GolfStatsApp.Server.Data;
using GolfStatsApp.Server.PgaTour;
using GolfStatsApp.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace GolfStatsApp.Server.Services.Prediction
{
    public class ModelBuildResult
    {
        public List<GolferInputs> Golfers { get; } = new();
        public EngineConfig Engine { get; set; } = new();
        public InputsSummary Summary { get; set; } = new();
    }

    /// <summary>
    /// Assembles <see cref="GolferInputs"/> from season stats and tournament performances.
    /// Stats resolve cache → PGA API (write-through) → DB snapshots → CSV; performances resolve DB first, then API with write-back.
    /// </summary>
    public class ModelInputService
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
        private const int MinRoundsForOwnSigma = 10;

        private readonly AppDbContext _db;
        private readonly IPgaDataSource _pga;
        private readonly IFallbackPlayerSource _fallback;
        private readonly IMemoryCache _cache;
        private readonly GolferSearchService _search;
        private readonly CutRuleResolver _cutRules;
        private readonly PredictiveModelingOptions _options;
        private readonly ILogger<ModelInputService> _logger;

        public ModelInputService(AppDbContext db, IPgaDataSource pga, IFallbackPlayerSource fallback, IMemoryCache cache,
            GolferSearchService search, CutRuleResolver cutRules, PredictiveModelingOptions options, ILogger<ModelInputService> logger)
        {
            _db = db;
            _pga = pga;
            _fallback = fallback;
            _cache = cache;
            _search = search;
            _cutRules = cutRules;
            _options = options;
            _logger = logger;
        }

        // ---------------------------------------------------------------- catalog

        public List<PredictionStatInfo> GetStats() =>
            _pga.GetStatCatalog()
                .Select(c =>
                {
                    var role = StatRoles.Get(c.StatId);
                    return new PredictionStatInfo { StatId = c.StatId, Name = c.StatName, Category = c.Category, Supported = role != null, Role = role?.Role };
                })
                .ToList();

        public async Task<List<PredictionTournament>> GetTournamentsAsync(string scope, int year, CancellationToken ct)
        {
            List<PredictionTournament> all;
            try
            {
                all = await GetScheduleAsync(year, ct);
            }
            catch (Exception ex) when (IsUpstreamFailure(ex))
            {
                _logger.LogWarning(ex, "Schedule unavailable for {Year}; using database tournaments", year);
                var prefix = $"R{year}";
                var rows = await _db.LeagueTournaments.AsNoTracking()
                    .Where(t => t.TournamentId.StartsWith(prefix))
                    .ToListAsync(ct);
                all = rows.GroupBy(t => t.TournamentId)
                    .Select(g => ToTournament(g.Key, g.First().Name, g.First().StartDate, g.First().EventType, string.Empty))
                    .ToList();
                if (all.Count == 0) throw new PredictionDataUnavailableException("Tournament schedule is unavailable.");
            }

            var today = DateTime.UtcNow.Date;
            return scope.ToLowerInvariant() switch
            {
                "current" => all.Where(t => IsInProgress(t) || (t.StartDate is { } d && d <= today && d.AddDays(4) >= today)).ToList(),
                "upcoming" => all.Where(t => !IsCompleted(t) && (t.StartDate is null || t.StartDate >= today.AddDays(-4))).ToList(),
                "historical" => all.Where(IsCompleted).ToList(),
                _ => all,
            };
        }

        private async Task<List<PredictionTournament>> GetScheduleAsync(int year, CancellationToken ct) =>
            (await _cache.GetOrCreateAsync($"prediction:schedule:{year}:R", async e =>
            {
                e.AbsoluteExpirationRelativeToNow = CacheTtl;
                var schedule = await _pga.GetScheduleAsync(year, ct);
                return schedule.Where(s => !string.IsNullOrEmpty(s.TournamentId))
                    .Select(s => ToTournament(s.TournamentId!, s.TournamentName ?? s.TournamentId!, PgaRowParser.ParseDate(s.DisplayDate, year), null, s.Status ?? string.Empty))
                    .ToList();
            }))!;

        private PredictionTournament ToTournament(string id, string name, DateTime? start, string? eventType, string status)
        {
            var (type, rule) = _cutRules.Resolve(id, name, eventType);
            return new PredictionTournament
            {
                TournamentId = id, Name = name, StartDate = start, EventType = type, Status = status,
                HasCut = rule.CutTopN.HasValue, CutTopN = rule.CutTopN, Supported = !rule.Unsupported,
            };
        }

        private static bool IsCompleted(PredictionTournament t) => t.Status.Contains("COMPLET", StringComparison.OrdinalIgnoreCase) || t.Status.Contains("OFFICIAL", StringComparison.OrdinalIgnoreCase);
        private static bool IsInProgress(PredictionTournament t) => t.Status.Contains("PROGRESS", StringComparison.OrdinalIgnoreCase) || t.Status.Contains("LIVE", StringComparison.OrdinalIgnoreCase);

        // ---------------------------------------------------------------- events (FR-10b)

        public async Task<List<GolferEvent>> GetGolferEventsAsync(string playerId, int? fromSeason, int? toSeason, string? eventType, CancellationToken ct)
        {
            var golfer = await _search.ResolveAsync(playerId, null, ct);
            var to = toSeason ?? DateTime.UtcNow.Year;
            var from = fromSeason ?? to - 1;
            if (from > to || to - from >= PredictionLimits.MaxHistoricalSeasons + 1)
                throw new PredictionValidationException("fromSeason", $"Season range must span at most {PredictionLimits.MaxHistoricalSeasons + 1} seasons.");

            var seasons = Enumerable.Range(from, to - from + 1).ToList();
            var loaded = await LoadEventsAsync(new[] { playerId }, seasons, ct);
            var events = loaded.Events[playerId];
            if (golfer is null && events.Count == 0)
                throw new PredictionNotFoundException($"Golfer '{playerId}' was not found.");

            return events
                .Where(e => e.Season >= from && e.Season <= to)
                .Where(e => string.IsNullOrEmpty(eventType) || string.Equals(e.EventType, eventType, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        internal class LoadedEvents
        {
            public Dictionary<string, List<GolferEvent>> Events { get; } = new();
            public int FromDb { get; set; }
            public int FromApi { get; set; }
            public List<string> Warnings { get; } = new();
            public DateTime? OldestDbCapture { get; set; }
        }

        /// <summary>DB first (snapshots + completed synced tournaments), then API for anything stale/missing, with write-back.</summary>
        internal async Task<LoadedEvents> LoadEventsAsync(IReadOnlyCollection<string> playerIds, IReadOnlyCollection<int> seasons, CancellationToken ct)
        {
            var result = new LoadedEvents();
            var ids = playerIds.Distinct().ToList();
            var minSeason = seasons.Min();
            var maxSeason = seasons.Max();

            var snapshots = await _db.PlayerResultSnapshots.AsNoTracking().Include(s => s.RoundScores)
                .Where(s => ids.Contains(s.PlayerId) && s.Season >= minSeason && s.Season <= maxSeason)
                .ToListAsync(ct);
            var synced = await LoadSyncedEventsAsync(ids, ct);

            var byPlayer = ids.ToDictionary(id => id, _ => new Dictionary<string, GolferEvent>());
            foreach (var s in snapshots)
                byPlayer[s.PlayerId][s.TournamentId] = FromSnapshot(s);
            foreach (var (pid, ev) in synced)
                if (ev.Season >= minSeason && ev.Season <= maxSeason)
                    byPlayer[pid][ev.TournamentId] = ev; // synced hole-by-hole data is authoritative

            // Players whose DB data is fresh for every requested season skip the API tier.
            var freshCutoff = DateTime.UtcNow - CacheTtl;
            var freshness = snapshots.GroupBy(s => (s.PlayerId, s.Season)).ToDictionary(g => g.Key, g => g.Max(x => x.CapturedUtc));
            var toFetch = new List<(string PlayerId, int Season)>();
            foreach (var id in ids)
                foreach (var season in seasons)
                    if (!(freshness.TryGetValue((id, season), out var captured) && captured >= freshCutoff))
                        toFetch.Add((id, season));

            var fetched = await FetchResultsAsync(toFetch, ct);
            var failedPlayers = new HashSet<string>();
            foreach (var ((pid, _), rows) in fetched)
            {
                if (rows is null) { failedPlayers.Add(pid); continue; }
                foreach (var row in rows)
                {
                    var ev = PgaRowParser.ParseResult(row, _cutRules);
                    if (ev is null || !seasons.Contains(ev.Season)) continue;
                    if (byPlayer[pid].TryGetValue(ev.TournamentId, out var existing) && existing.Source == "db" && existing.Rounds.Count >= ev.Rounds.Count)
                        continue;
                    byPlayer[pid][ev.TournamentId] = ev;
                }
            }

            await WriteBackAsync(byPlayer, ct);

            foreach (var id in ids)
            {
                var list = byPlayer[id].Values
                    .OrderByDescending(e => e.StartDate ?? new DateTime(e.Season, 1, 1))
                    .ThenByDescending(e => e.TournamentId, StringComparer.Ordinal)
                    .ToList();
                result.Events[id] = list;
                result.FromDb += list.Count(e => e.Source == "db");
                result.FromApi += list.Count(e => e.Source == "api");
            }

            if (failedPlayers.Count > 0)
            {
                result.OldestDbCapture = snapshots.Where(s => failedPlayers.Contains(s.PlayerId)).Select(s => (DateTime?)s.CapturedUtc).Min();
                result.Warnings.Add($"PGA Tour results API unavailable for {failedPlayers.Count} golfer(s); performances served from the database" +
                    (result.OldestDbCapture is { } asOf ? $" (data as of {asOf:yyyy-MM-dd HH:mm} UTC)." : "."));
            }
            return result;
        }

        private async Task<List<((string PlayerId, int Season) Key, List<PlayerResultRow>? Rows)>> FetchResultsAsync(
            List<(string PlayerId, int Season)> keys, CancellationToken ct)
        {
            using var gate = new SemaphoreSlim(8);
            var currentSeason = DateTime.UtcNow.Year;
            var tasks = keys.Select(async key =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var rows = await _cache.GetOrCreateAsync($"prediction:results:{key.PlayerId}:{key.Season}", async e =>
                    {
                        e.AbsoluteExpirationRelativeToNow = CacheTtl;
                        return await _pga.GetPlayerResultsAsync(key.PlayerId, key.Season == currentSeason ? null : key.Season, ct);
                    });
                    return (key, rows);
                }
                catch (Exception ex) when (IsUpstreamFailure(ex))
                {
                    _logger.LogWarning(ex, "Results unavailable for {PlayerId} {Season}", key.PlayerId, key.Season);
                    return (key, (List<PlayerResultRow>?)null);
                }
                finally
                {
                    gate.Release();
                }
            });
            return (await Task.WhenAll(tasks)).ToList();
        }

        private async Task<List<(string PlayerId, GolferEvent Event)>> LoadSyncedEventsAsync(List<string> ids, CancellationToken ct)
        {
            var finals = await _db.SyncedTournaments.AsNoTracking().Where(t => t.IsFinal).Select(t => t.TournamentId).ToListAsync(ct);
            if (finals.Count == 0) return new();

            var rounds = await _db.GolferRounds.AsNoTracking()
                .Where(r => finals.Contains(r.TournamentId) && ids.Contains(r.PlayerId) && r.Holes >= 18)
                .ToListAsync(ct);
            if (rounds.Count == 0) return new();

            var tids = rounds.Select(r => r.TournamentId).Distinct().ToList();
            var golfers = await _db.TournamentGolfers.AsNoTracking()
                .Where(g => tids.Contains(g.TournamentId) && ids.Contains(g.PlayerId)).ToListAsync(ct);
            var meta = (await _db.LeagueTournaments.AsNoTracking().Where(t => tids.Contains(t.TournamentId)).ToListAsync(ct))
                .GroupBy(t => t.TournamentId).ToDictionary(g => g.Key, g => g.First());

            var list = new List<(string, GolferEvent)>();
            foreach (var group in rounds.GroupBy(r => (r.PlayerId, r.TournamentId)))
            {
                var golfer = golfers.FirstOrDefault(g => g.PlayerId == group.Key.PlayerId && g.TournamentId == group.Key.TournamentId);
                meta.TryGetValue(group.Key.TournamentId, out var m);
                var name = m?.Name ?? group.Key.TournamentId;
                var (type, _) = _cutRules.Resolve(group.Key.TournamentId, name, m?.EventType);
                var r = group.OrderBy(x => x.Round).Select(x => new GolferEventRound { Round = x.Round, ToPar = x.ToPar }).ToList();
                list.Add((group.Key.PlayerId, new GolferEvent
                {
                    TournamentId = group.Key.TournamentId,
                    Name = name,
                    Season = PgaRowParser.SeasonFromTournamentId(group.Key.TournamentId) ?? m?.StartDate?.Year ?? DateTime.UtcNow.Year,
                    StartDate = m?.StartDate,
                    EventType = type,
                    Position = golfer?.Position,
                    Status = PgaRowParser.NormalizeStatus(golfer?.Position),
                    TotalToPar = r.Sum(x => x.ToPar),
                    RoundsPlayed = r.Count,
                    Rounds = r,
                    Source = "db",
                }));
            }
            return list;
        }

        private static GolferEvent FromSnapshot(PlayerResultSnapshot s) => new()
        {
            TournamentId = s.TournamentId,
            Name = s.TournamentName,
            Season = s.Season,
            StartDate = s.EndDate,
            EventType = s.EventType,
            Position = s.Position,
            Status = s.Status,
            TotalToPar = s.ToPar,
            RoundsPlayed = s.Rounds,
            Rounds = s.RoundScores.OrderBy(r => r.Round).Select(r => new GolferEventRound { Round = r.Round, ToPar = r.ToPar }).ToList(),
            Source = "db",
        };

        private async Task WriteBackAsync(Dictionary<string, Dictionary<string, GolferEvent>> byPlayer, CancellationToken ct)
        {
            var fromApi = byPlayer.SelectMany(p => p.Value.Values.Where(e => e.Source == "api").Select(e => (PlayerId: p.Key, Event: e))).ToList();
            if (fromApi.Count == 0) return;
            try
            {
                var pids = fromApi.Select(x => x.PlayerId).Distinct().ToList();
                var existing = (await _db.PlayerResultSnapshots.Include(s => s.RoundScores).Where(s => pids.Contains(s.PlayerId)).ToListAsync(ct))
                    .ToDictionary(s => (s.PlayerId, s.TournamentId));
                var now = DateTime.UtcNow;
                foreach (var (pid, ev) in fromApi)
                {
                    if (!existing.TryGetValue((pid, ev.TournamentId), out var snap))
                    {
                        snap = new PlayerResultSnapshot { PlayerId = pid, TournamentId = ev.TournamentId };
                        _db.PlayerResultSnapshots.Add(snap);
                        existing[(pid, ev.TournamentId)] = snap;
                    }
                    snap.TournamentName = ev.Name;
                    snap.Season = ev.Season;
                    snap.EndDate = ev.StartDate;
                    snap.EventType = ev.EventType;
                    snap.Position = ev.Position;
                    snap.Status = ev.Status;
                    snap.ToPar = ev.TotalToPar;
                    snap.Rounds = ev.RoundsPlayed;
                    snap.CapturedUtc = now;
                    snap.RoundScores.Clear();
                    snap.RoundScores.AddRange(ev.Rounds.Select(r => new PlayerResultRound { Round = r.Round, ToPar = r.ToPar }));
                }
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Result snapshot write-back failed");
                _db.ChangeTracker.Clear();
            }
        }

        // ---------------------------------------------------------------- stats

        internal class StatTable
        {
            public string StatId { get; init; } = string.Empty;
            public string Source { get; init; } = string.Empty;
            public DateTime? AsOfUtc { get; init; }
            public Dictionary<string, double> Values { get; init; } = new();
        }

        /// <summary>cache → PGA API (write-through) → DB snapshots → CSV (birdie/bogey only). Null if every tier fails.</summary>
        internal async Task<StatTable?> LoadStatAsync(string statId, List<string> warnings, CancellationToken ct)
        {
            var role = StatRoles.Get(statId)!;
            var cacheKey = $"prediction:stat:{statId}";
            if (_cache.TryGetValue(cacheKey, out StatTable? cached) && cached != null) return cached;

            try
            {
                var rankings = await _pga.GetStatAsync(statId, null, ct);
                var values = new Dictionary<string, double>();
                foreach (var row in rankings.Rows)
                    if (!string.IsNullOrEmpty(row.PlayerId) && PgaRowParser.StatValue(row, role) is { } v)
                        values[row.PlayerId] = v;
                if (values.Count > 0)
                {
                    var table = new StatTable { StatId = statId, Source = "api", AsOfUtc = DateTime.UtcNow, Values = values };
                    _cache.Set(cacheKey, table, CacheTtl);
                    await WriteStatSnapshotsAsync(statId, rankings.Year ?? DateTime.UtcNow.Year, rankings.Rows, role, ct);
                    return table;
                }
            }
            catch (Exception ex) when (IsUpstreamFailure(ex))
            {
                _logger.LogWarning(ex, "Stat {StatId} unavailable from PGA Tour API; trying database", statId);
            }

            var latestSeason = await _db.StatSnapshots.AsNoTracking().Where(s => s.StatId == statId).Select(s => (int?)s.Season).MaxAsync(ct);
            if (latestSeason.HasValue)
            {
                var rows = await _db.StatSnapshots.AsNoTracking().Where(s => s.StatId == statId && s.Season == latestSeason).ToListAsync(ct);
                var asOf = rows.Min(r => r.CapturedUtc);
                warnings.Add($"Stat {statId} served from database (data as of {asOf:yyyy-MM-dd HH:mm} UTC).");
                return new StatTable { StatId = statId, Source = "db", AsOfUtc = asOf, Values = rows.ToDictionary(r => r.PlayerId, r => r.Value) };
            }

            if (statId is StatRoles.BirdieAverage or StatRoles.BogeyAvoidance)
            {
                try
                {
                    var players = await _fallback.GetPlayersAsync();
                    if (players.Count > 0)
                    {
                        warnings.Add($"Stat {statId} served from bundled CSV fallback; values may be outdated.");
                        return new StatTable
                        {
                            StatId = statId, Source = "csv",
                            Values = players.Where(p => !string.IsNullOrEmpty(p.PlayerId))
                                .GroupBy(p => p.PlayerId).ToDictionary(g => g.Key, g => statId == StatRoles.BirdieAverage ? g.First().Birdies : g.First().Bogeys),
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "CSV fallback unavailable for stat {StatId}", statId);
                }
            }

            warnings.Add($"Stat {statId} is unavailable from every data source and was ignored.");
            return null;
        }

        private async Task WriteStatSnapshotsAsync(string statId, int season, List<StatRankingRow> rows, StatRole role, CancellationToken ct)
        {
            try
            {
                var existing = await _db.StatSnapshots.Where(s => s.StatId == statId && s.Season == season).ToDictionaryAsync(s => s.PlayerId, ct);
                var now = DateTime.UtcNow;
                foreach (var row in rows)
                {
                    if (string.IsNullOrEmpty(row.PlayerId) || PgaRowParser.StatValue(row, role) is not { } v) continue;
                    if (!existing.TryGetValue(row.PlayerId, out var snap))
                    {
                        snap = new StatSnapshot { StatId = statId, PlayerId = row.PlayerId, Season = season };
                        _db.StatSnapshots.Add(snap);
                        existing[row.PlayerId] = snap;
                    }
                    snap.PlayerName = row.PlayerName?.Trim() ?? snap.PlayerName;
                    snap.Value = v;
                    snap.Rank = row.Rank;
                    snap.CapturedUtc = now;
                }
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Stat snapshot write-through failed for {StatId}", statId);
                _db.ChangeTracker.Clear();
            }
        }

        // ---------------------------------------------------------------- build

        public async Task<ModelBuildResult> BuildAsync(SimulateRequest request, CancellationToken ct)
        {
            var errors = new Dictionary<string, string[]>();
            var summary = new InputsSummary { TournamentId = request.TournamentId };
            var result = new ModelBuildResult { Summary = summary };
            var warnings = summary.Warnings;

            // --- validation of stats & sources (FR-7, FR-8, FR-12)
            var catalog = _pga.GetStatCatalog().ToDictionary(c => c.StatId, StringComparer.Ordinal);
            for (var i = 0; i < request.Stats.Count; i++)
            {
                var s = request.Stats[i];
                if (!catalog.ContainsKey(s.StatId)) errors[$"stats[{i}].statId"] = new[] { $"Unknown stat id '{s.StatId}'." };
                else if (StatRoles.Get(s.StatId) is null) errors[$"stats[{i}].statId"] = new[] { $"Stat '{s.StatId}' is not supported by the model." };
            }
            if (request.Stats.Select(s => s.StatId).Distinct().Count() != request.Stats.Count)
                errors["stats"] = new[] { "Duplicate stat ids are not allowed." };

            var perf = request.Performance ?? new PerformanceSelection();
            var anyExplicit = request.Golfers.Any(g => g.Events is { Count: > 0 });
            var anyPerf = perf.CurrentTournament.Enabled || perf.RecentForm.Enabled || perf.Historical.Enabled || anyExplicit;
            if (request.Stats.Count == 0 && !anyPerf)
                errors["stats"] = new[] { "Select at least one stat or one performance source." };
            if (perf.Historical.Enabled && perf.Historical.Seasons.Any(y => y < DateTime.UtcNow.Year - PredictionLimits.MaxHistoricalSeasons || y > DateTime.UtcNow.Year))
                errors["performance.historical.seasons"] = new[] { $"Seasons must be within the last {PredictionLimits.MaxHistoricalSeasons} seasons." };

            // --- golfers (FR-2, FR-5)
            var resolved = new List<(int Index, GolferSearchResult Golfer, GolferSelection Selection)>();
            var seen = new HashSet<string>();
            for (var i = 0; i < request.Golfers.Count; i++)
            {
                var sel = request.Golfers[i];
                if (string.IsNullOrWhiteSpace(sel.PlayerId) && string.IsNullOrWhiteSpace(sel.PlayerName))
                {
                    errors[$"golfers[{i}]"] = new[] { "Provide a playerId or playerName." };
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(sel.PlayerId) && !sel.PlayerId.Trim().All(char.IsLetterOrDigit))
                {
                    errors[$"golfers[{i}].playerId"] = new[] { "Player id must be alphanumeric." };
                    continue;
                }
                var g = await _search.ResolveAsync(sel.PlayerId, sel.PlayerName, ct);
                if (g is null)
                {
                    errors[$"golfers[{i}]"] = new[] { $"Unknown golfer '{sel.PlayerId ?? sel.PlayerName}'." };
                    continue;
                }
                if (!seen.Add(g.PlayerId))
                {
                    errors[$"golfers[{i}]"] = new[] { $"Golfer '{g.PlayerName}' is selected more than once." };
                    continue;
                }
                if (sel.Events?.SelectMany(e => e.Rounds ?? new List<int>()).Any(r => r < 1 || r > 4) == true)
                {
                    errors[$"golfers[{i}].events"] = new[] { "Rounds must be between 1 and 4." };
                    continue;
                }
                resolved.Add((i, g, sel));
            }
            if (errors.Count > 0) throw new PredictionValidationException(errors);

            // --- tournament & cut rule (FR-16, §6.1)
            var tournament = await ResolveTournamentAsync(request.TournamentId, warnings, ct);
            if (!tournament.Supported)
                throw new PredictionValidationException("tournamentId", $"{tournament.Name} uses a team format the model does not support.");
            summary.TournamentName = tournament.Name;
            summary.EventType = tournament.EventType;
            summary.HasCut = tournament.HasCut;
            summary.CutTopN = tournament.CutTopN;

            // --- current-tournament played rounds (fixed in the engine)
            var playerIds = resolved.Select(r => r.Golfer.PlayerId).ToList();
            var currentRounds = (await _db.GolferRounds.AsNoTracking()
                    .Where(r => r.TournamentId == request.TournamentId && playerIds.Contains(r.PlayerId) && r.Holes >= 18 && r.Round >= 1 && r.Round <= 4)
                    .ToListAsync(ct))
                .GroupBy(r => r.PlayerId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Round).ToList());
            var currentFieldMean = await RoundFieldMeansAsync(request.TournamentId, ct);

            // --- field size for scaling the cut when only part of the field is modeled
            summary.FieldSize = await ResolveFieldSizeAsync(request.TournamentId, resolved.Count, ct);

            // --- performance events (DB first, then API) for golfers that need them
            var needEvents = perf.RecentForm.Enabled || perf.Historical.Enabled || anyExplicit;
            var seasons = new HashSet<int> { DateTime.UtcNow.Year, DateTime.UtcNow.Year - 1 };
            if (perf.Historical.Enabled)
            {
                foreach (var y in perf.Historical.Seasons) seasons.Add(y);
                foreach (var t in perf.Historical.TournamentIds) if (PgaRowParser.SeasonFromTournamentId(t) is { } y) seasons.Add(y);
            }
            foreach (var r in resolved)
                foreach (var e in r.Selection.Events ?? new List<EventSelection>())
                    if (PgaRowParser.SeasonFromTournamentId(e.TournamentId) is { } y && y >= DateTime.UtcNow.Year - PredictionLimits.MaxHistoricalSeasons) seasons.Add(y);

            var loaded = needEvents ? await LoadEventsAsync(playerIds, seasons, ct) : new LoadedEvents();
            warnings.AddRange(loaded.Warnings);
            var fieldMeans = needEvents ? await FieldMeansForEventsAsync(loaded, ct) : new Dictionary<string, double[]>();

            // Validate explicit participation (FR-10b).
            foreach (var r in resolved)
            {
                if (r.Selection.Events is not { Count: > 0 }) continue;
                var played = loaded.Events.GetValueOrDefault(r.Golfer.PlayerId)?.Select(e => e.TournamentId).ToHashSet() ?? new HashSet<string>();
                var bad = r.Selection.Events.Where(e => !played.Contains(e.TournamentId)).Select(e => e.TournamentId).ToList();
                if (bad.Count > 0) errors[$"golfers[{r.Index}].events"] = new[] { $"{r.Golfer.PlayerName} did not play: {string.Join(", ", bad)}." };
            }
            if (errors.Count > 0) throw new PredictionValidationException(errors);
            summary.Performance.EventsFromDb = loaded.FromDb;
            summary.Performance.EventsFromApi = loaded.FromApi;

            // --- stats (FR-6..FR-9)
            var statWeights = Normalize(request.Stats.Select(s => s.Weight ?? 1).ToList());
            var statTables = new List<(StatSelection Sel, StatRole Role, StatTable Table, double Weight)>();
            for (var i = 0; i < request.Stats.Count; i++)
            {
                var sel = request.Stats[i];
                var table = await LoadStatAsync(sel.StatId, warnings, ct);
                if (table is null) continue;
                statTables.Add((sel, StatRoles.Get(sel.StatId)!, table, statWeights[i]));
            }
            if (request.Stats.Count > 0 && statTables.Count == 0 && !anyPerf)
                throw new PredictionDataUnavailableException("Season stats are unavailable from every data source.");
            var renormalized = Normalize(statTables.Select(t => t.Weight).ToList());
            for (var i = 0; i < statTables.Count; i++)
            {
                statTables[i] = statTables[i] with { Weight = renormalized[i] };
                summary.Stats.Add(new StatInputSummary
                {
                    StatId = statTables[i].Sel.StatId,
                    Name = catalog[statTables[i].Sel.StatId].StatName,
                    WeightPercent = Math.Round(renormalized[i] * 100, 2),
                    Source = statTables[i].Table.Source,
                    AsOfUtc = statTables[i].Table.AsOfUtc,
                });
            }
            var sgScale = await SgTotalScaleAsync(statTables.Select(t => t.Table).ToList(), warnings, ct);

            // --- source weights (FR-11)
            var srcRaw = new[]
            {
                perf.CurrentTournament.Enabled ? perf.CurrentTournament.Weight ?? 40 : 0,
                perf.RecentForm.Enabled ? perf.RecentForm.Weight ?? 40 : 0,
                perf.Historical.Enabled ? perf.Historical.Weight ?? 20 : 0,
            };
            var srcNorm = srcRaw.Sum() > 0 ? Normalize(srcRaw.ToList()) : new List<double> { 0, 0, 0 };
            summary.Performance.CurrentWeightPercent = Math.Round(srcNorm[0] * 100, 2);
            summary.Performance.RecentWeightPercent = Math.Round(srcNorm[1] * 100, 2);
            summary.Performance.HistoricalWeightPercent = Math.Round(srcNorm[2] * 100, 2);
            summary.Performance.LastNEvents = perf.RecentForm.LastNEvents;
            summary.Performance.HistoricalTournamentIds = perf.Historical.TournamentIds.ToList();
            summary.Performance.HistoricalSeasons = perf.Historical.Seasons.ToList();
            var statsShare = statTables.Count == 0 ? 0 : !anyPerf ? 1 : request.StatsWeight / 100.0;
            summary.Performance.StatsWeightPercent = Math.Round(statsShare * 100, 2);
            summary.Performance.PerformanceWeightPercent = Math.Round((1 - statsShare) * 100, 2);

            // --- per golfer
            foreach (var (_, golfer, sel) in resolved)
            {
                ct.ThrowIfCancellationRequested();
                var input = new GolferInputs { PlayerId = golfer.PlayerId, PlayerName = golfer.PlayerName };
                var gs = new GolferInputSummary { PlayerId = golfer.PlayerId, PlayerName = golfer.PlayerName, ExplicitEvents = sel.Events is { Count: > 0 } };

                // Stat component (strokes gained vs. tour per round).
                double? statSkill = null;
                if (statTables.Count > 0)
                {
                    double acc = 0;
                    foreach (var (s, role, table, w) in statTables)
                    {
                        double strokes;
                        if (table.Values.TryGetValue(golfer.PlayerId, out var value))
                            strokes = StatToStrokes(role, value, table, sgScale);
                        else
                        {
                            strokes = 0; // field mean
                            input.Warnings.Add($"Missing {catalog[s.StatId].StatName}; using field mean.");
                        }
                        acc += w * strokes;
                    }
                    statSkill = acc;
                }

                // Performance samples: (strokes better than field, weight)
                var samples = new List<(double Value, double Weight, int Source)>();
                var fixedRounds = new int?[4];
                if (currentRounds.TryGetValue(golfer.PlayerId, out var cur))
                {
                    foreach (var r in cur)
                    {
                        fixedRounds[r.Round - 1] = r.ToPar;
                        if (perf.CurrentTournament.Enabled)
                            samples.Add((FieldMean(currentFieldMean, r.Round) - r.ToPar, 1, 0));
                    }
                    gs.CurrentRounds = cur.Count;
                }
                input.FixedRounds = fixedRounds;

                var events = loaded.Events.GetValueOrDefault(golfer.PlayerId) ?? new List<GolferEvent>();
                events = events.Where(e => e.TournamentId != request.TournamentId).ToList();
                if (sel.Events is { Count: > 0 })
                {
                    var chosen = sel.Events.ToDictionary(e => e.TournamentId, e => e.Rounds);
                    var picked = events.Where(e => chosen.ContainsKey(e.TournamentId)).ToList();
                    AddEventSamples(picked, chosen, samples, gs, fieldMeans, input.Warnings, sourceIndex: 1);
                }
                else
                {
                    if (perf.RecentForm.Enabled)
                        AddEventSamples(events.Take(perf.RecentForm.LastNEvents).ToList(), null, samples, gs, fieldMeans, input.Warnings, 1);
                    if (perf.Historical.Enabled)
                    {
                        var hist = events.Where(e => perf.Historical.TournamentIds.Contains(e.TournamentId) || perf.Historical.Seasons.Contains(e.Season)).ToList();
                        AddEventSamples(hist, null, samples, gs, fieldMeans, input.Warnings, 2);
                    }
                    if ((perf.RecentForm.Enabled || perf.Historical.Enabled) && events.Count == 0)
                        input.Warnings.Add("No participated events available; using stats only.");
                }

                // Explicit events use the combined recent + historical weight.
                var sourceWeights = srcNorm.ToArray();
                if (gs.ExplicitEvents)
                {
                    sourceWeights[1] = (perf.RecentForm.Weight ?? 40) + (perf.Historical.Weight ?? 20);
                    sourceWeights[2] = 0;
                    sourceWeights[0] = perf.CurrentTournament.Enabled ? perf.CurrentTournament.Weight ?? 40 : 0;
                }

                double? perfSkill = null;
                double num = 0, den = 0;
                for (var src = 0; src < 3; src++)
                {
                    var subset = samples.Where(x => x.Source == src).ToList();
                    var wsum = subset.Sum(x => x.Weight);
                    if (subset.Count == 0 || wsum <= 0 || sourceWeights[src] <= 0) continue;
                    num += sourceWeights[src] * subset.Sum(x => x.Value * x.Weight) / wsum;
                    den += sourceWeights[src];
                }
                var n = samples.Count;
                if (den > 0)
                {
                    perfSkill = Shrink(num / den, n, _options.ShrinkageRounds);
                    if (n < _options.ShrinkageRounds)
                        input.Warnings.Add($"Small performance sample ({n} round{(n == 1 ? "" : "s")}); estimate shrunk toward field mean.");
                }

                input.Skill = (statSkill, perfSkill) switch
                {
                    ({ } s, { } p) => statsShare * s + (1 - statsShare) * p,
                    ({ } s, null) => s,
                    (null, { } p) => p,
                    _ => 0,
                };
                if (statSkill is null && perfSkill is null)
                    input.Warnings.Add("No usable stats or performances; modeled at field average.");

                input.Sigma = SigmaFor(samples.Select(x => x.Value).ToList());
                result.Golfers.Add(input);
                summary.Golfers.Add(gs);
            }

            // --- engine config
            var cutN = tournament.CutTopN;
            if (cutN.HasValue && summary.FieldSize > resolved.Count && resolved.Count > 1)
            {
                var scaled = (int)Math.Round(cutN.Value * (double)resolved.Count / summary.FieldSize);
                warnings.Add($"Only {resolved.Count} of ~{summary.FieldSize} golfers modeled; cut line scaled to top {Math.Max(1, scaled)} of the modeled group and finish positions are relative to the modeled golfers.");
                cutN = Math.Max(1, scaled);
            }
            result.Engine = new EngineConfig
            {
                Simulations = request.Simulations,
                CutTopN = cutN,
                FieldMeanToPar = _options.DefaultFieldToPar,
                Rounds = 4,
                CutAfterRound = 2,
            };

            summary.DataAsOfUtc = summary.Stats.Select(s => s.AsOfUtc).Concat(new[] { loaded.OldestDbCapture }).Where(d => d.HasValue).DefaultIfEmpty(DateTime.UtcNow).Min();
            return result;
        }

        private void AddEventSamples(List<GolferEvent> events, Dictionary<string, List<int>?>? chosenRounds,
            List<(double, double, int)> samples, GolferInputSummary gs, Dictionary<string, double[]> fieldMeans, List<string> warnings, int sourceIndex)
        {
            // Events are newest first; recency decay by event index (FR-13).
            for (var idx = 0; idx < events.Count; idx++)
            {
                var e = events[idx];
                var weight = Math.Pow(0.5, idx / Math.Max(0.5, _options.HalfLifeEvents));
                var rounds = e.Rounds;
                if (chosenRounds != null && chosenRounds.TryGetValue(e.TournamentId, out var subset) && subset is { Count: > 0 })
                {
                    var unplayed = subset.Where(r => rounds.All(x => x.Round != r)).ToList();
                    if (unplayed.Count > 0) warnings.Add($"{e.Name}: round(s) {string.Join(",", unplayed)} not played; ignored.");
                    rounds = rounds.Where(r => subset.Contains(r.Round)).ToList();
                }
                fieldMeans.TryGetValue(e.TournamentId, out var means);
                foreach (var r in rounds)
                    samples.Add((FieldMean(means, r.Round) - r.ToPar, weight, sourceIndex));
                if (gs.Events.All(x => x.TournamentId != e.TournamentId))
                    gs.Events.Add(new GolferEventUsage { TournamentId = e.TournamentId, Name = e.Name, Rounds = rounds.Count });
            }
        }

        private double FieldMean(double[]? means, int round) =>
            means != null && round >= 1 && round <= means.Length && !double.IsNaN(means[round - 1]) ? means[round - 1] : _options.DefaultFieldToPar;

        private async Task<double[]?> RoundFieldMeansAsync(string tournamentId, CancellationToken ct)
        {
            var map = await FieldMeansAsync(new[] { tournamentId }, ct);
            return map.GetValueOrDefault(tournamentId);
        }

        private Task<Dictionary<string, double[]>> FieldMeansForEventsAsync(LoadedEvents loaded, CancellationToken ct) =>
            FieldMeansAsync(loaded.Events.Values.SelectMany(e => e).Select(e => e.TournamentId).Distinct().ToList(), ct);

        /// <summary>Per-round field average to par for tournaments synced with at least 30 full rounds.</summary>
        private async Task<Dictionary<string, double[]>> FieldMeansAsync(IReadOnlyCollection<string> tournamentIds, CancellationToken ct)
        {
            var ids = tournamentIds.ToList();
            var stats = await _db.GolferRounds.AsNoTracking()
                .Where(r => ids.Contains(r.TournamentId) && r.Holes >= 18)
                .GroupBy(r => new { r.TournamentId, r.Round })
                .Select(g => new { g.Key.TournamentId, g.Key.Round, Mean = g.Average(x => (double)x.ToPar), Count = g.Count() })
                .ToListAsync(ct);
            var map = new Dictionary<string, double[]>();
            foreach (var s in stats.Where(s => s.Count >= 30 && s.Round >= 1 && s.Round <= 4))
            {
                if (!map.TryGetValue(s.TournamentId, out var arr))
                    map[s.TournamentId] = arr = new[] { double.NaN, double.NaN, double.NaN, double.NaN };
                arr[s.Round - 1] = s.Mean;
            }
            return map;
        }

        private async Task<PredictionTournament> ResolveTournamentAsync(string tournamentId, List<string> warnings, CancellationToken ct)
        {
            var season = PgaRowParser.SeasonFromTournamentId(tournamentId);
            var scheduleLoaded = false;
            if (season.HasValue)
            {
                try
                {
                    var schedule = await GetScheduleAsync(season.Value, ct);
                    scheduleLoaded = true;
                    var match = schedule.FirstOrDefault(t => t.TournamentId == tournamentId);
                    if (match != null) return match;
                }
                catch (Exception ex) when (IsUpstreamFailure(ex))
                {
                    _logger.LogWarning(ex, "Schedule unavailable while resolving {TournamentId}", tournamentId);
                }
            }

            var league = await _db.LeagueTournaments.AsNoTracking().FirstOrDefaultAsync(t => t.TournamentId == tournamentId, ct);
            if (league != null) return ToTournament(league.TournamentId, league.Name, league.StartDate, league.EventType, string.Empty);
            if (await _db.SyncedTournaments.AsNoTracking().AnyAsync(t => t.TournamentId == tournamentId, ct))
                return ToTournament(tournamentId, tournamentId, null, null, string.Empty);

            if (scheduleLoaded || !season.HasValue)
                throw new PredictionNotFoundException($"Tournament '{tournamentId}' was not found.");

            warnings.Add($"Tournament schedule unavailable; '{tournamentId}' modeled with default PGA cut rules.");
            return ToTournament(tournamentId, tournamentId, null, null, string.Empty);
        }

        private async Task<int> ResolveFieldSizeAsync(string tournamentId, int modeled, CancellationToken ct)
        {
            try
            {
                var field = await _cache.GetOrCreateAsync($"prediction:field:{tournamentId}", async e =>
                {
                    e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
                    return await _pga.GetFieldAsync(tournamentId, ct);
                });
                var count = field?.Count(f => !f.Alternate && f.Withdrawn != true) ?? 0;
                if (count > 0) return Math.Max(count, modeled);
            }
            catch (Exception ex) when (IsUpstreamFailure(ex))
            {
                _logger.LogInformation(ex, "Field unavailable for {TournamentId}", tournamentId);
            }
            var synced = await _db.TournamentGolfers.AsNoTracking().CountAsync(g => g.TournamentId == tournamentId, ct);
            return Math.Max(synced > 0 ? synced : _options.DefaultFieldSize, modeled);
        }

        /// <summary>
        /// Calibration scale for non-SG stats: one SD of a non-SG stat maps to one SD of SG: Total (strokes/round)
        /// across the same tour population. Defaults to 1.0 stroke when SG: Total is unavailable.
        /// </summary>
        private async Task<double> SgTotalScaleAsync(List<StatTable> tables, List<string> warnings, CancellationToken ct)
        {
            if (tables.All(t => StatRoles.Get(t.StatId)!.IsStrokesGained)) return 1.0;
            var sg = tables.FirstOrDefault(t => t.StatId == StatRoles.SgTotal) ?? await LoadStatAsync(StatRoles.SgTotal, new List<string>(), ct);
            if (sg is { Values.Count: > 2 })
            {
                var sd = StdDev(sg.Values.Values.ToList());
                if (sd > 0) return sd;
            }
            warnings.Add("SG: Total unavailable for calibration; non-SG stats use a default 1.0 stroke per SD mapping.");
            return 1.0;
        }

        internal static double StatToStrokes(StatRole role, double value, StatTable table, double sgScale)
        {
            if (role.IsStrokesGained) return value;
            var values = table.Values.Values.ToList();
            var mean = values.Average();
            var sd = StdDev(values);
            if (sd <= 0) return 0;
            var z = (value - mean) / sd;
            if (role.Direction == StatDirection.LowerIsBetter) z = -z;
            return z * sgScale;
        }

        private double SigmaFor(List<double> values)
        {
            var sigma = values.Count >= MinRoundsForOwnSigma ? StdDev(values) : _options.FieldSigma;
            return Math.Clamp(sigma, 1.5, 4.5);
        }

        internal static double Shrink(double mean, int n, double k) => n + k <= 0 ? 0 : n * mean / (n + k);

        internal static List<double> Normalize(List<double> weights)
        {
            var sum = weights.Sum();
            return sum <= 0 ? weights.Select(_ => weights.Count == 0 ? 0 : 1.0 / weights.Count).ToList() : weights.Select(w => w / sum).ToList();
        }

        private static double StdDev(List<double> values)
        {
            if (values.Count < 2) return 0;
            var mean = values.Average();
            return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
        }

        internal static bool IsUpstreamFailure(Exception ex) =>
            ex is PgaTourException or System.Net.Http.HttpRequestException or TimeoutException or Polly.Timeout.TimeoutRejectedException
                or System.Text.Json.JsonException or InvalidOperationException
            || (ex is OperationCanceledException oce && !oce.CancellationToken.IsCancellationRequested);
    }
}
