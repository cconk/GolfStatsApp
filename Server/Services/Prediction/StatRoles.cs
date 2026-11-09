using System;
using System.Collections.Generic;
using System.Linq;

namespace GolfStatsApp.Server.Services.Prediction
{
    public enum StatDirection { HigherIsBetter, LowerIsBetter }

    /// <summary>How a catalog stat feeds the skill estimate (§6). Only stats listed here are supported in v1.</summary>
    public record StatRole(string StatId, string Role, bool IsStrokesGained, StatDirection Direction, string[] ValueKeys);

    public static class StatRoles
    {
        public const string SgTotal = "02675";
        public const string BirdieAverage = "156";
        public const string BogeyAvoidance = "02414";

        private static readonly StatRole[] Roles =
        {
            new(SgTotal, "sg-total", true, StatDirection.HigherIsBetter, new[] { "avg", "average" }),
            new("02674", "sg-tee-to-green", true, StatDirection.HigherIsBetter, new[] { "avg", "average" }),
            new("02567", "sg-off-the-tee", true, StatDirection.HigherIsBetter, new[] { "avg", "average" }),
            new("02568", "sg-approach", true, StatDirection.HigherIsBetter, new[] { "avg", "average" }),
            new("02569", "sg-around-the-green", true, StatDirection.HigherIsBetter, new[] { "avg", "average" }),
            new("02564", "sg-putting", true, StatDirection.HigherIsBetter, new[] { "avg", "average" }),
            new(BirdieAverage, "birdies", false, StatDirection.HigherIsBetter, new[] { "avg", "average" }),
            new(BogeyAvoidance, "bogey-avoidance", false, StatDirection.LowerIsBetter, new[] { "bogeys_per_round", "avg", "average", "_" }),
            new("120", "scoring-average", false, StatDirection.LowerIsBetter, new[] { "avg", "average" }),
        };

        private static readonly Dictionary<string, StatRole> ById = Roles.ToDictionary(r => r.StatId, StringComparer.Ordinal);

        public static IReadOnlyCollection<StatRole> All => Roles;

        public static StatRole? Get(string statId) => ById.TryGetValue(statId, out var r) ? r : null;
    }
}
