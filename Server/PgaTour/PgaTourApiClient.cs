// Portions ported from pgatourPY (https://github.com/WalrusQuant/pgatourPY), MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GolfStatsApp.Server.PgaTour
{
    /// <summary>
    /// Native C# client for the PGA Tour data APIs (replaces the pgatourPY Python sidecar).
    /// </summary>
    public class PgaTourApiClient
    {
        private static readonly HashSet<string> ValidTours = new() { "R", "S", "H", "Y" };
        private static readonly Lazy<IReadOnlyList<StatCatalogEntry>> Catalog = new(LoadCatalog);

        private readonly PgaTourTransport _transport;

        public PgaTourApiClient(PgaTourTransport transport)
        {
            _transport = transport;
        }

        public IReadOnlyList<StatCatalogEntry> GetStatCatalog() => Catalog.Value;

        public async Task<string> GetCurrentTournamentIdAsync(string tour = "R", CancellationToken ct = default)
        {
            ValidateTour(tour);
            var cfg = await _transport.ConfigAsync("web-config", ct);
            var first = cfg?["defaultTournaments"]?[tour]?.AsArray().FirstOrDefault()
                ?? throw new PgaTourException($"No default tournament for tour '{tour}'");
            return Str(first["id"]) ?? Str(first["leaderboardId"])
                ?? throw new PgaTourException($"Default tournament for tour '{tour}' has no id");
        }

        public async Task<List<TournamentLeaderboardRow>> GetLeaderboardAsync(string tournamentId, CancellationToken ct = default)
        {
            var data = await _transport.GraphQlAsync("LeaderboardCompressedV3", new { leaderboardCompressedV3Id = tournamentId }, ct);
            var parsed = Decompress(data?["leaderboardCompressedV3"]?["payload"]);
            return Items(parsed?["players"]).Select(p =>
            {
                var player = p["player"];
                var scoring = p["scoringData"];
                return new TournamentLeaderboardRow
                {
                    PlayerId = Str(player?["id"]),
                    DisplayName = Str(player?["displayName"]),
                    Country = Str(player?["country"]),
                    Position = Str(scoring?["position"]),
                    Total = Str(scoring?["total"]),
                    Thru = Str(scoring?["thru"]),
                    Score = Str(scoring?["score"]),
                    CurrentRound = Int(scoring?["currentRound"]),
                    PlayerState = Str(scoring?["playerState"]),
                    TotalStrokes = Int(scoring?["totalStrokes"]),
                    Rounds = scoring?["rounds"] is JsonArray r ? string.Join(" / ", r.Select(Str)) : null,
                };
            }).ToList();
        }

        public async Task<List<FieldPlayer>> GetFieldAsync(string tournamentId, bool includeWithdrawn = true, CancellationToken ct = default)
        {
            var data = await _transport.GraphQlAsync("Field", new { fieldId = tournamentId, includeWithdrawn }, ct);
            var field = data?["field"];
            FieldPlayer Map(JsonNode p, bool alternate) => new()
            {
                PlayerId = Str(p["id"]),
                DisplayName = Str(p["displayName"]),
                Country = Str(p["country"]),
                Amateur = Bool(p["amateur"]),
                Owgr = Int(p["owgr"]),
                Status = Str(p["status"]),
                Alternate = alternate || (Bool(p["alternate"]) ?? false),
                Withdrawn = Bool(p["withdrawn"]),
            };
            return Items(field?["players"]).Select(p => Map(p, false))
                .Concat(Items(field?["alternates"]).Select(p => Map(p, true)))
                .ToList();
        }

        public async Task<List<OddsRow>> GetOddsAsync(string tournamentId, CancellationToken ct = default)
        {
            var data = await _transport.GraphQlAsync("oddsToWinCompressed", new { tournamentId }, ct);
            var payload = data?["oddsToWinCompressed"]?["payload"];
            if (payload is null) return new();
            var parsed = Decompress(payload);
            var players = parsed as JsonArray
                ?? new[] { "players", "odds", "rows" }.Select(k => parsed?[k] as JsonArray).FirstOrDefault(a => a != null);
            return Items(players).Select(p => new OddsRow
            {
                PlayerId = Str(p["playerId"]) ?? Str(p["id"]),
                DisplayName = Str(p["displayName"]),
                Odds = Str(p["odds"]),
                OddsSort = Dbl(p["oddsSort"]),
                OddsDirection = Str(p["oddsDirection"]),
            }).ToList();
        }

        public async Task<List<CourseHoleStat>> GetCourseStatsAsync(string tournamentId, CancellationToken ct = default)
        {
            var data = await _transport.GraphQlAsync("CourseStats", new { tournamentId }, ct);
            var rows = new List<CourseHoleStat>();
            foreach (var course in Items(data?["courseStats"]?["courses"]))
            foreach (var round in Items(course["roundHoleStats"]))
            foreach (var hole in Items(round["holeStats"]))
            {
                rows.Add(new CourseHoleStat
                {
                    CourseName = Str(course["courseName"]),
                    RoundNumber = Int(round["roundNum"]),
                    RoundHeader = Str(round["roundHeader"]),
                    HoleNumber = Int(hole["courseHoleNum"]),
                    Par = Int(hole["parValue"]) ?? Int(hole["par"]),
                    Yardage = Str(hole["yards"]) ?? Str(hole["yardage"]),
                    ScoringAverage = Str(hole["scoringAverage"]),
                    ScoringAverageDiff = Str(hole["scoringAverageDiff"]),
                    Eagles = Int(hole["eagles"]),
                    Birdies = Int(hole["birdies"]),
                    Pars = Int(hole["pars"]),
                    Bogeys = Int(hole["bogeys"]),
                    DoubleBogey = Int(hole["doubleBogey"]),
                    Rank = Int(hole["rank"]),
                });
            }
            return rows;
        }

        public async Task<List<ScorecardHole>> GetScorecardAsync(string tournamentId, string playerId, CancellationToken ct = default)
        {
            var data = await _transport.GraphQlAsync("ScorecardCompressedV3", new { tournamentId, playerId }, ct);
            var parsed = Decompress(data?["scorecardCompressedV3"]?["payload"]);
            var rows = new List<ScorecardHole>();
            foreach (var round in Items(parsed?["roundScores"]))
            foreach (var nine in new[] { "firstNine", "secondNine" })
            foreach (var hole in Items(round[nine]?["holes"]))
            {
                rows.Add(new ScorecardHole
                {
                    RoundNumber = Int(round["roundNumber"]),
                    HoleNumber = Int(hole["holeNumber"]),
                    Par = Int(hole["par"]),
                    Score = Str(hole["score"]),
                    Status = Str(hole["status"]),
                    Yardage = Int(hole["yardage"]),
                    RoundScore = Str(hole["roundScore"]),
                    CourseName = Str(round["courseName"]),
                });
            }
            return rows;
        }

        public async Task<List<ScheduleEvent>> GetScheduleAsync(int year, string tour = "R", CancellationToken ct = default)
        {
            ValidateTour(tour);
            var resp = await _transport.RestAsync($"schedule/{tour}/{year}", ct);
            return Items(resp?["tournaments"]).Select(t => new ScheduleEvent
            {
                TournamentId = Str(t["tournamentId"]),
                TournamentName = Str(t["name"]),
                DisplayDate = Str(t["displayDate"]),
                Status = Str(t["status"]),
                Purse = Str(t["purse"]),
                FedExCupPoints = Str(t["standings"]?["value"]),
                Champion = Str(Items(t["champions"]).FirstOrDefault()?["displayName"]),
                CourseName = Str(t["courseData"]?["name"]),
                City = Str(t["courseData"]?["city"]),
                State = Str(t["courseData"]?["stateCode"]),
                Country = Str(t["courseData"]?["country"]),
            }).ToList();
        }

        public async Task<List<PlayerDirectoryEntry>> GetPlayersAsync(string tour = "R", CancellationToken ct = default)
        {
            ValidateTour(tour);
            var resp = await _transport.RestAsync($"player/list/{tour}", ct);
            return Items(resp?["players"]).Select(p => new PlayerDirectoryEntry
            {
                PlayerId = Str(p["id"]),
                DisplayName = Str(p["displayName"]),
                Country = Str(p["country"]),
                IsActive = Bool(p["isActive"]),
                Age = Int(p["playerBio"]?["age"]),
            }).ToList();
        }

        public async Task<PlayerProfile> GetPlayerProfileAsync(string playerId, CancellationToken ct = default)
        {
            var resp = await _transport.RestAsync($"player/profiles/{Uri.EscapeDataString(playerId)}", ct);
            var s = resp?["summaryData"]?["summaryData"];
            return new PlayerProfile
            {
                PlayerId = Str(resp?["playerId"]),
                FirstName = Str(s?["firstName"]),
                LastName = Str(s?["lastName"]),
                Country = Str(s?["country"]),
                Born = Str(s?["born"]),
                Age = Str(s?["age"]),
                Birthplace = Str(s?["birthplace"]),
                College = Str(s?["college"]),
                TurnedPro = Str(s?["turnedPro"]),
                Highlights = Items(s?["careerHighlights"])
                    .Select(h => new ProfileHighlight(Str(h["title"]), Str(h["data"]), Str(h["subTitle"]))).ToList(),
            };
        }

        public async Task<List<PlayerStat>> GetPlayerStatsAsync(string playerId, CancellationToken ct = default)
        {
            var resp = await _transport.RestAsync($"player/profiles/{Uri.EscapeDataString(playerId)}/stats", ct);
            return Items(resp?["stats"]).Select(s => new PlayerStat
            {
                StatId = Str(s["statId"]),
                Title = Str(s["title"]),
                Rank = Str(s["rank"]),
                Value = Str(s["value"]),
                Category = s["category"] is JsonArray c ? string.Join(", ", c.Select(Str)) : null,
                FieldAverage = Str(s["fieldAverage"]),
            }).ToList();
        }

        /// <summary>Tournament-by-tournament results for one season (or the default/current season when null).</summary>
        public async Task<List<PlayerResultRow>> GetPlayerResultsAsync(string playerId, int? season = null, CancellationToken ct = default)
        {
            var path = $"player/profiles/{Uri.EscapeDataString(playerId)}/results" + (season.HasValue ? $"?season={season}" : string.Empty);
            var resp = await _transport.RestAsync(path, ct);
            var rows = new List<PlayerResultRow>();
            foreach (var block in Items(resp?["resultsData"]))
            {
                var labels = new List<string>();
                foreach (var h in Items(block["headers"]))
                {
                    if (h["label"] is not null) labels.Add(Str(h["label"]) ?? "");
                    else foreach (var sub in (h["labels"] as JsonArray ?? new JsonArray()).Select(Str)) labels.Add($"{Str(h["groupLabel"])} {sub}".Trim());
                }
                var cols = MakeUniqueSnake(labels);
                var blockSeason = Str(block["season"]) ?? Str(block["year"]) ?? Str(block["displaySeason"]) ?? season?.ToString();

                foreach (var d in Items(block["data"]))
                {
                    var values = new Dictionary<string, JsonElement>();
                    var fields = d["fields"] as JsonArray ?? new JsonArray();
                    for (var i = 0; i < fields.Count; i++)
                        values[i < cols.Count ? cols[i] : $"field_{i}"] = JsonSerializer.SerializeToElement(Str(fields[i]));
                    rows.Add(new PlayerResultRow { Season = blockSeason, TournamentId = Str(d["tournamentId"]), Values = values });
                }
            }
            return rows;
        }

        public async Task<StatRankings> GetStatAsync(string statId, int? year = null, string tour = "R", CancellationToken ct = default)
        {
            ValidateTour(tour);
            var variables = new Dictionary<string, object> { ["tourCode"] = tour, ["statId"] = statId };
            if (year.HasValue) variables["year"] = year.Value;

            var data = await _transport.GraphQlAsync("StatDetails", variables, ct);
            var details = data?["statDetails"];
            var headers = MakeUniqueSnake((details?["statHeaders"] as JsonArray ?? new JsonArray()).Select(h => Str(h) ?? "").ToList());

            var rows = Items(details?["rows"])
                .Where(r => Str(r["__typename"]) == "StatDetailsPlayer")
                .Select(r =>
                {
                    var stats = r["stats"] as JsonArray ?? new JsonArray();
                    var values = new Dictionary<string, JsonElement>();
                    for (var i = 0; i < headers.Count; i++)
                        values[headers[i]] = JsonSerializer.SerializeToElement(i < stats.Count ? Str(stats[i]?["statValue"]) : null);
                    return new StatRankingRow
                    {
                        StatId = statId,
                        Rank = Int(r["rank"]),
                        PlayerId = Str(r["playerId"]),
                        PlayerName = Str(r["playerName"]),
                        Country = Str(r["country"]),
                        Values = values,
                    };
                }).ToList();

            return new StatRankings(Str(details?["statTitle"]), Str(details?["statDescription"]), Str(details?["tourAvg"]),
                Int(details?["year"]) ?? year, headers, rows);
        }

        public async Task<List<FedExCupRow>> GetFedExCupAsync(int? year = null, string tour = "R", CancellationToken ct = default)
        {
            ValidateTour(tour);
            var data = await _transport.GraphQlAsync("TourCupSplit",
                new { tourCode = tour, id = "02671", year = year ?? DateTime.Now.Year }, ct);
            var cup = data?["tourCupSplit"];
            var players = cup?["projectedPlayers"] as JsonArray is { Count: > 0 } p ? p : cup?["officialPlayers"];
            return Items(players)
                .Where(x => Str(x["__typename"]) == "TourCupCombinedPlayer")
                .Select(x => new FedExCupRow
                {
                    PlayerId = Str(x["id"]),
                    DisplayName = Str(x["displayName"]),
                    Country = Str(x["country"]),
                    ThisWeekRank = Str(x["thisWeekRank"]),
                    PreviousWeekRank = Str(x["previousWeekRank"]),
                    ProjectedPoints = Str(x["pointData"]?["projected"]),
                    OfficialPoints = Str(x["pointData"]?["official"]),
                }).ToList();
        }

        private static JsonNode? Decompress(JsonNode? payload) =>
            payload is null ? null : PgaTourTransport.DecompressPayload(Str(payload));

        private static IEnumerable<JsonNode> Items(JsonNode? node) =>
            node is JsonArray arr ? arr.Where(n => n is JsonObject).Select(n => n!) : Enumerable.Empty<JsonNode>();

        private static string? Str(JsonNode? node) => node switch
        {
            null => null,
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            _ => node.ToJsonString(),
        };

        private static int? Int(JsonNode? node)
        {
            var s = Str(node)?.TrimStart('T');
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
        }

        private static double? Dbl(JsonNode? node) =>
            double.TryParse(Str(node), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

        private static bool? Bool(JsonNode? node) =>
            node is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

        private static void ValidateTour(string tour)
        {
            if (!ValidTours.Contains(tour))
                throw new ArgumentException($"Unknown tour code '{tour}'. Use R, S, H or Y.", nameof(tour));
        }

        /// <summary>Converts header labels to unique snake_case keys (mirrors pgatourPY's make_unique_snake).</summary>
        internal static List<string> MakeUniqueSnake(IReadOnlyList<string> labels)
        {
            var seen = new Dictionary<string, int>();
            var result = new List<string>();
            foreach (var label in labels)
            {
                var snake = Regex.Replace(label.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
                if (snake.Length == 0) snake = "col";
                if (seen.TryGetValue(snake, out var n))
                {
                    seen[snake] = n + 1;
                    snake = $"{snake}_{n + 1}";
                }
                else seen[snake] = 1;
                result.Add(snake);
            }
            return result;
        }

        private static IReadOnlyList<StatCatalogEntry> LoadCatalog()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var name = assembly.GetManifestResourceNames().First(r => r.EndsWith("stat_ids.json", StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(name)!;
            return JsonSerializer.Deserialize<List<StatCatalogEntry>>(stream) ?? new();
        }
    }
}
