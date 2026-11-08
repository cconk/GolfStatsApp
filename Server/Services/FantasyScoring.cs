using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GolfStatsApp.Server.Data;
using GolfStatsApp.Shared.Models;

namespace GolfStatsApp.Server.Services
{
    /// <summary>Pure league scoring rules: hole points, round bonuses, auto-swap, captain, FedExCup bonus and start counting.</summary>
    public static class FantasyScoring
    {
        public const int Rounds = 4;

        public static double HolePoints(int strokes, int par) => (strokes - par) switch
        {
            <= -3 => 8,
            -2 => 5,
            -1 => 2,
            0 => 1,
            1 => -1,
            _ => -3
        };

        /// <summary>Low round (10), second low round (5), both including ties; uses score to par. Plus bogey-free (3).</summary>
        public static Dictionary<string, double> RoundBonuses(IEnumerable<GolferRound> roundResults)
        {
            var finished = roundResults.Where(r => r.Holes >= 18).ToList();
            var distinct = finished.Select(r => r.ToPar).Distinct().OrderBy(s => s).ToList();
            var bonuses = new Dictionary<string, double>();
            foreach (var r in finished)
            {
                double bonus = 0;
                if (distinct.Count > 0 && r.ToPar == distinct[0]) bonus += 10;
                else if (distinct.Count > 1 && r.ToPar == distinct[1]) bonus += 5;
                if (r.BogeyFree) bonus += 3;
                bonuses[r.PlayerId] = bonus;
            }
            return bonuses;
        }

        public static bool MissedCut(string? position) =>
            position is not null && (position.Equals("CUT", StringComparison.OrdinalIgnoreCase) ||
                                     position.Equals("MC", StringComparison.OrdinalIgnoreCase));

        public static bool IsOut(string? position) =>
            MissedCut(position) || position is "WD" or "DQ" or "MDF" or "wd" or "dq";

        /// <summary>The lineup version that applies to <paramref name="round"/>.</summary>
        public static List<LineupSlot> EffectiveSlots(IEnumerable<LineupSlot> slots, int round)
        {
            var list = slots as IReadOnlyCollection<LineupSlot> ?? slots.ToList();
            var from = list.Where(s => s.FromRound <= round).Select(s => s.FromRound).DefaultIfEmpty(0).Max();
            return from == 0 ? new() : list.Where(s => s.FromRound == from).ToList();
        }

        /// <summary>Starting golfers for a round after auto-swapping cut starters with bench golfers who made the cut.</summary>
        public static List<string> StartersForRound(IEnumerable<LineupSlot> slots, int round, bool cutKnown, Func<string, bool> missedCut)
        {
            var effective = EffectiveSlots(slots, round);
            var starters = effective.Where(s => s.IsStarter).Select(s => s.PlayerId).ToList();
            if (round < 3 || !cutKnown) return starters;

            var bench = new Queue<string>(effective.Where(s => !s.IsStarter && !missedCut(s.PlayerId))
                .OrderBy(s => s.BenchPriority).Select(s => s.PlayerId));
            for (var i = 0; i < starters.Count && bench.Count > 0; i++)
                if (missedCut(starters[i])) starters[i] = bench.Dequeue();
            return starters;
        }

        /// <summary>Golfers that used a start in this tournament. Before play begins, the planned starters count as committed.</summary>
        public static HashSet<string> StartsUsed(IEnumerable<LineupSlot> slots, int startedRound, bool cutKnown, Func<string, bool> missedCut)
        {
            var list = slots.ToList();
            if (startedRound <= 0)
                return EffectiveSlots(list, Rounds).Where(s => s.IsStarter).Select(s => s.PlayerId).ToHashSet();
            var used = new HashSet<string>();
            for (var r = 1; r <= Math.Min(startedRound, Rounds); r++)
                used.UnionWith(StartersForRound(list, r, cutKnown, missedCut));
            return used;
        }

        /// <summary>1/10th FedExCup points for a finishing position (ties share the average of the tied positions).</summary>
        public static double FedExBonus(string? position, string eventType, IReadOnlyDictionary<string, int> tieCounts)
        {
            if (string.IsNullOrWhiteSpace(position)) return 0;
            if (!int.TryParse(position.TrimStart('T', 't'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pos)) return 0;
            var tied = Math.Max(1, tieCounts.GetValueOrDefault(position, 1));
            var key = LeagueRules.EventTypes.Contains(eventType) ? eventType : "pga";
            var total = Enumerable.Range(pos, tied)
                .Sum(p => GolfSimulationService.FedExTable.TryGetValue(p, out var row) ? row[key] : 0);
            return total / tied * LeagueRules.FedExBonusFactor;
        }

        public record TournamentData(
            int StartedRound,
            bool IsFinal,
            bool CutKnown,
            int Segment,
            string EventType,
            IReadOnlyDictionary<(string PlayerId, int Round), GolferRound> Rounds,
            IReadOnlyDictionary<(string PlayerId, int Round), double> Bonuses,
            IReadOnlyDictionary<string, TournamentGolfer> Golfers)
        {
            public bool MissedCut(string playerId) => Golfers.TryGetValue(playerId, out var g) && FantasyScoring.MissedCut(g.Position);
        }

        public static List<GolferScoreDto> ScoreEntry(TeamTournamentEntry entry, TournamentData data)
        {
            var tieCounts = data.Golfers.Values.Where(g => g.Position is not null)
                .GroupBy(g => g.Position!).ToDictionary(g => g.Key, g => g.Count());
            var names = entry.Slots.GroupBy(s => s.PlayerId).ToDictionary(g => g.Key, g => g.Last().PlayerName);
            var scores = new Dictionary<string, GolferScoreDto>();
            GolferScoreDto For(string id) => scores.TryGetValue(id, out var s) ? s : scores[id] = new GolferScoreDto
            {
                PlayerId = id,
                Name = names.GetValueOrDefault(id) ?? data.Golfers.GetValueOrDefault(id)?.PlayerName ?? id,
                IsCaptain = id == entry.CaptainPlayerId
            };

            for (var r = 1; r <= Math.Min(data.StartedRound, Rounds); r++)
            {
                foreach (var id in StartersForRound(entry.Slots, r, data.CutKnown, data.MissedCut))
                {
                    var score = For(id);
                    var multiplier = score.IsCaptain && r >= entry.CaptainFromRound ? 2 : 1;
                    score.RoundsStarted.Add(r);
                    if (data.Rounds.TryGetValue((id, r), out var round)) score.HolePoints += round.HolePoints * multiplier;
                    score.BonusPoints += data.Bonuses.GetValueOrDefault((id, r)) * multiplier;
                    if (r == Rounds && data.IsFinal && data.Segment <= LeagueRules.FedExBonusMaxSegment)
                        score.FedExPoints = Math.Round(FedExBonus(data.Golfers.GetValueOrDefault(id)?.Position, data.EventType, tieCounts) * multiplier, 2);
                }
            }

            foreach (var s in scores.Values)
                s.Total = s.HolePoints + s.BonusPoints + s.FedExPoints;
            return scores.Values.OrderByDescending(s => s.Total).ThenBy(s => s.Name).ToList();
        }
    }
}
