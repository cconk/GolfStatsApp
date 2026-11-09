using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using GolfStatsApp.Server.PgaTour;
using GolfStatsApp.Shared.Models;

namespace GolfStatsApp.Server.Services.Prediction
{
    /// <summary>Tolerant parsers for the loosely-typed PGA Tour result/stat rows.</summary>
    public static class PgaRowParser
    {
        private static readonly Regex RoundKey = new(@"(^|_)r(ound)?_?([1-4])$", RegexOptions.Compiled);

        public static int? SeasonFromTournamentId(string? tournamentId) =>
            tournamentId is { Length: >= 5 } && int.TryParse(tournamentId.AsSpan(1, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) && y > 1900 ? y : null;

        public static string? Text(IReadOnlyDictionary<string, JsonElement> values, string key) =>
            values.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        public static double? Number(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim().Replace(",", "").TrimEnd('%').TrimStart('T');
            if (s.Equals("E", StringComparison.OrdinalIgnoreCase)) return 0;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        }

        public static double? StatValue(StatRankingRow row, StatRole role)
        {
            if (role.StatId == StatRoles.BogeyAvoidance)
            {
                var bogeys = Number(Text(row.Values, "bogeys"));
                var holes = Number(Text(row.Values, "holes_played"));
                if (bogeys.HasValue && holes is > 0) return bogeys.Value / holes.Value * 18;
            }
            foreach (var key in role.ValueKeys)
            {
                var n = Number(Text(row.Values, key));
                if (n.HasValue) return n;
            }
            foreach (var kv in row.Values)
            {
                var n = Number(kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() : null);
                if (n.HasValue) return n;
            }
            return null;
        }

        public static string NormalizeStatus(string? position)
        {
            var p = (position ?? string.Empty).Trim().ToUpperInvariant().Replace("/", "");
            return p switch
            {
                "CUT" or "MC" or "MDF" => "MC",
                "WD" => "WD",
                "DQ" => "DQ",
                _ => "finished",
            };
        }

        public static GolferEvent? ParseResult(PlayerResultRow row, CutRuleResolver cutRules)
        {
            if (string.IsNullOrWhiteSpace(row.TournamentId)) return null;
            var v = row.Values;

            string? Find(Func<string, bool> predicate) => v.Keys.Where(predicate).Select(k => Text(v, k)).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

            var name = Find(k => k.Contains("tournament") || k == "event" || k.StartsWith("event_")) ?? row.TournamentId;
            var position = Find(k => k is "pos" or "position" or "finish" || k.StartsWith("pos_") || k.StartsWith("finish_"));
            var toParRaw = Find(k => k.Contains("to_par") || k == "par" || k == "score");
            var date = Find(k => k.Contains("date"));

            var roundValues = new SortedDictionary<int, double>();
            foreach (var key in v.Keys)
            {
                var m = RoundKey.Match(key);
                if (!m.Success) continue;
                var n = Number(Text(v, key));
                if (n.HasValue) roundValues[int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)] = n.Value;
            }

            int? totalToPar = Number(toParRaw) is { } tp ? (int)Math.Round(tp) : null;
            var rounds = new List<GolferEventRound>();
            if (roundValues.Count > 0)
            {
                // Round columns are usually strokes; convert to to-par using the event par implied by the total.
                var looksLikeStrokes = roundValues.Values.All(x => x > 50);
                double parPerRound = 72;
                if (looksLikeStrokes && totalToPar.HasValue)
                    parPerRound = (roundValues.Values.Sum() - totalToPar.Value) / roundValues.Count;
                foreach (var (round, value) in roundValues)
                    rounds.Add(new GolferEventRound { Round = round, ToPar = (int)Math.Round(looksLikeStrokes ? value - parPerRound : value) });
                totalToPar ??= rounds.Sum(r => r.ToPar);
            }

            var season = int.TryParse(row.Season, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s
                : SeasonFromTournamentId(row.TournamentId) ?? DateTime.UtcNow.Year;
            var (eventType, _) = cutRules.Resolve(row.TournamentId, name);

            return new GolferEvent
            {
                TournamentId = row.TournamentId,
                Name = name,
                Season = season,
                StartDate = ParseDate(date, season),
                EventType = eventType,
                Position = position,
                Status = NormalizeStatus(position),
                TotalToPar = totalToPar,
                RoundsPlayed = rounds.Count,
                Rounds = rounds,
                Source = "api",
            };
        }

        public static DateTime? ParseDate(string? raw, int? season)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
            // Display dates like "Jan 2 - 5, 2025" or "Apr 10 - 13". Handled before the general parse,
            // which misreads "Nov 12 - 15" as Nov 12, 2015.
            var m = Regex.Match(raw, @"([A-Za-z]{3,9})\.?\s+(\d{1,2})");
            var year = Regex.Match(raw, @"\b(19|20)\d{2}\b") is { Success: true } ym ? int.Parse(ym.Value, CultureInfo.InvariantCulture) : season;
            if (m.Success && year.HasValue &&
                DateTime.TryParse($"{m.Groups[1].Value} {m.Groups[2].Value} {year}", CultureInfo.InvariantCulture, styles, out var d))
                return d;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, styles, out d)) return d;
            return null;
        }
    }
}
