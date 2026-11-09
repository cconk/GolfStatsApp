using System;
using System.Collections.Generic;
using System.Linq;

namespace GolfStatsApp.Server.Services.Prediction
{
    public class CutRule
    {
        /// <summary>Top N (and ties) make the cut after R2. Null means no cut.</summary>
        public int? CutTopN { get; set; }
        public string? EventType { get; set; }
        /// <summary>Team / non-stroke-play formats the engine cannot model.</summary>
        public bool Unsupported { get; set; }
    }

    public class TournamentCutOverride : CutRule
    {
        /// <summary>Exact tournament id (e.g. R2026014) or a case-insensitive name fragment.</summary>
        public string Match { get; set; } = string.Empty;
    }

    public class PredictiveModelingOptions
    {
        public const string SectionName = "PredictiveModeling";

        public int MaxConcurrentRuns { get; set; } = 4;
        public int MaxConcurrentRunsPerClient { get; set; } = 2;
        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>Recency half-life (in events) for recent/historical performance.</summary>
        public double HalfLifeEvents { get; set; } = 8;

        /// <summary>Shrinkage constant k (rounds) toward the field mean.</summary>
        public double ShrinkageRounds { get; set; } = 8;

        /// <summary>Field round-to-round SD (strokes) used when a golfer has fewer than 10 rounds.</summary>
        public double FieldSigma { get; set; } = 2.8;

        /// <summary>Assumed field-average strokes to par per round when the actual field average is unknown.</summary>
        public double DefaultFieldToPar { get; set; } = 0.7;

        /// <summary>Typical full-field size used to scale the cut line when only part of the field is modeled.</summary>
        public int DefaultFieldSize { get; set; } = 156;

        /// <summary>Default cut rule per event class (pga, major, signature, additional, playoff).</summary>
        public Dictionary<string, CutRule> CutRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Per-tournament overrides; evaluated before the event-class defaults.</summary>
        public List<TournamentCutOverride> CutOverrides { get; set; } = new();
    }

    /// <summary>
    /// Resolves the cut rule for a tournament from configuration (§6.1). Defaults are the v1 table and must be
    /// re-verified against the official PGA TOUR format each season.
    /// </summary>
    public class CutRuleResolver
    {
        public static readonly IReadOnlyDictionary<string, CutRule> DefaultRules = new Dictionary<string, CutRule>(StringComparer.OrdinalIgnoreCase)
        {
            ["pga"] = new() { CutTopN = 65 },
            ["major"] = new() { CutTopN = 70 },
            ["signature"] = new() { CutTopN = null },
            ["playoff"] = new() { CutTopN = null },
            ["additional"] = new() { CutTopN = 65 },
        };

        public static readonly IReadOnlyList<TournamentCutOverride> DefaultOverrides = new List<TournamentCutOverride>
        {
            new() { Match = "Masters", EventType = "major", CutTopN = 50 },
            new() { Match = "PGA Championship", EventType = "major", CutTopN = 70 },
            new() { Match = "U.S. Open", EventType = "major", CutTopN = 60 },
            new() { Match = "Open Championship", EventType = "major", CutTopN = 70 },
            new() { Match = "THE PLAYERS", EventType = "pga", CutTopN = 65 },
            new() { Match = "Zurich", EventType = "pga", Unsupported = true },
            new() { Match = "FedEx St. Jude", EventType = "playoff", CutTopN = null },
            new() { Match = "BMW Championship", EventType = "playoff", CutTopN = null },
            new() { Match = "TOUR Championship", EventType = "playoff", CutTopN = null },
            new() { Match = "AT&T Pebble Beach", EventType = "signature", CutTopN = null },
            new() { Match = "Arnold Palmer", EventType = "signature", CutTopN = 50 },
            new() { Match = "Memorial", EventType = "signature", CutTopN = 50 },
            new() { Match = "Sentry", EventType = "signature", CutTopN = null },
            new() { Match = "Genesis", EventType = "signature", CutTopN = null },
            new() { Match = "RBC Heritage", EventType = "signature", CutTopN = null },
            new() { Match = "Truist", EventType = "signature", CutTopN = null },
            new() { Match = "Travelers", EventType = "signature", CutTopN = null },
        };

        private readonly PredictiveModelingOptions _options;

        public CutRuleResolver(PredictiveModelingOptions options) => _options = options;

        public (string EventType, CutRule Rule) Resolve(string tournamentId, string? name, string? eventType = null)
        {
            var overrides = _options.CutOverrides.Concat(DefaultOverrides);
            var match = overrides.FirstOrDefault(o => string.Equals(o.Match, tournamentId, StringComparison.OrdinalIgnoreCase))
                ?? (string.IsNullOrWhiteSpace(name) ? null
                    : overrides.FirstOrDefault(o => o.Match.Length > 0 && name.Contains(o.Match, StringComparison.OrdinalIgnoreCase)));

            var type = match?.EventType ?? eventType ?? "pga";
            if (match != null) return (type, match);

            var rule = _options.CutRules.TryGetValue(type, out var configured) ? configured
                : DefaultRules.TryGetValue(type, out var dflt) ? dflt : DefaultRules["pga"];
            return (type, rule);
        }
    }
}
