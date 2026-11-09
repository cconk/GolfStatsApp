using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace GolfStatsApp.Shared.Models
{
    public static class PredictionLimits
    {
        public const int MinSimulations = 1;
        public const int MaxSimulations = 10_000;
        public const int DefaultSimulations = 10_000;
        public const int MinGolfers = 1;
        public const int MaxGolfers = 156;
        public const int MaxStats = 15;
        public const int MinLastNEvents = 1;
        public const int MaxLastNEvents = 20;
        public const int DefaultLastNEvents = 5;
        public const int MaxHistoricalSeasons = 5;
        public const int DefaultSearchLimit = 25;
        public const int MaxSearchLimit = 100;
    }

    public static class FinishBucketNames
    {
        public const string Win = "1";
        public const string Top5 = "2-5";
        public const string Top10 = "6-10";
        public const string Top20 = "11-20";
        public const string Top40 = "21-40";
        public const string Rest = "41+";
        public const string MissedCut = "MC";

        public static readonly string[] All = { Win, Top5, Top10, Top20, Top40, Rest, MissedCut };
    }

    public class GolferSearchResult
    {
        public string PlayerId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public int? Rank { get; set; }
    }

    public class PredictionStatInfo
    {
        public string StatId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool Supported { get; set; }
        public string? Role { get; set; }
    }

    public class PredictionTournament
    {
        public string TournamentId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime? StartDate { get; set; }
        public string EventType { get; set; } = "pga";
        public string Status { get; set; } = string.Empty;
        public bool HasCut { get; set; } = true;
        public int? CutTopN { get; set; }
        public bool Supported { get; set; } = true;
    }

    public class GolferEventRound
    {
        public int Round { get; set; }
        public int ToPar { get; set; }
    }

    public class GolferEvent
    {
        public string TournamentId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Season { get; set; }
        public DateTime? StartDate { get; set; }
        public string EventType { get; set; } = "pga";
        public string? Position { get; set; }
        /// <summary>finished | MC | WD | DQ</summary>
        public string Status { get; set; } = "finished";
        public int? TotalToPar { get; set; }
        public int RoundsPlayed { get; set; }
        public List<GolferEventRound> Rounds { get; set; } = new();
        /// <summary>db | api</summary>
        public string Source { get; set; } = "db";
    }

    public class EventSelection
    {
        [Required, StringLength(16, MinimumLength = 1)]
        public string TournamentId { get; set; } = string.Empty;

        /// <summary>Optional round subset (1-4). Null or empty means all rounds played.</summary>
        public List<int>? Rounds { get; set; }
    }

    public class GolferSelection
    {
        [StringLength(16)]
        public string? PlayerId { get; set; }

        [StringLength(100)]
        public string? PlayerName { get; set; }

        /// <summary>Optional explicit participated events; overrides generic recent/historical sources for this golfer.</summary>
        public List<EventSelection>? Events { get; set; }
    }

    public class StatSelection
    {
        [Required, StringLength(16, MinimumLength = 1)]
        public string StatId { get; set; } = string.Empty;

        [Range(0, 100)]
        public double? Weight { get; set; }
    }

    public class SourceToggle
    {
        public bool Enabled { get; set; }

        [Range(0, 100)]
        public double? Weight { get; set; }
    }

    public class RecentFormSource : SourceToggle
    {
        [Range(PredictionLimits.MinLastNEvents, PredictionLimits.MaxLastNEvents)]
        public int LastNEvents { get; set; } = PredictionLimits.DefaultLastNEvents;
    }

    public class HistoricalSource : SourceToggle
    {
        [MaxLength(50)]
        public List<string> TournamentIds { get; set; } = new();

        [MaxLength(PredictionLimits.MaxHistoricalSeasons)]
        public List<int> Seasons { get; set; } = new();
    }

    public class PerformanceSelection
    {
        public SourceToggle CurrentTournament { get; set; } = new() { Enabled = true, Weight = 40 };
        public RecentFormSource RecentForm { get; set; } = new() { Enabled = true, Weight = 40 };
        public HistoricalSource Historical { get; set; } = new() { Enabled = false, Weight = 20 };
    }

    public class SimulateRequest
    {
        [Required, StringLength(16, MinimumLength = 1)]
        public string TournamentId { get; set; } = string.Empty;

        [Required, MinLength(PredictionLimits.MinGolfers), MaxLength(PredictionLimits.MaxGolfers)]
        public List<GolferSelection> Golfers { get; set; } = new();

        [MaxLength(PredictionLimits.MaxStats)]
        public List<StatSelection> Stats { get; set; } = new();

        public PerformanceSelection Performance { get; set; } = new();

        /// <summary>Relative weight (0-100) of the season-stat component vs. the performance component.</summary>
        [Range(0, 100)]
        public double StatsWeight { get; set; } = 50;

        [Range(PredictionLimits.MinSimulations, PredictionLimits.MaxSimulations)]
        public int Simulations { get; set; } = PredictionLimits.DefaultSimulations;

        public int? Seed { get; set; }
    }

    public class GolferPrediction
    {
        public string PlayerId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public double MeanScoreToPar { get; set; }
        public double P10 { get; set; }
        public double P50 { get; set; }
        public double P90 { get; set; }
        public double MeanFinish { get; set; }
        public Dictionary<string, double> FinishBuckets { get; set; } = new();
        public double PMakeCut { get; set; }
        public double PWin { get; set; }
        public double PTop5 { get; set; }
        public double PTop10 { get; set; }
        public double PTop20 { get; set; }
        public double SkillEstimate { get; set; }
        public double Sigma { get; set; }
        public List<string> Warnings { get; set; } = new();
    }

    public class StatInputSummary
    {
        public string StatId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double WeightPercent { get; set; }
        public string Source { get; set; } = string.Empty;
        public DateTime? AsOfUtc { get; set; }
    }

    public class PerformanceInputSummary
    {
        public double CurrentWeightPercent { get; set; }
        public double RecentWeightPercent { get; set; }
        public double HistoricalWeightPercent { get; set; }
        public int LastNEvents { get; set; }
        public List<string> HistoricalTournamentIds { get; set; } = new();
        public List<int> HistoricalSeasons { get; set; } = new();
        public double StatsWeightPercent { get; set; }
        public double PerformanceWeightPercent { get; set; }
        public int EventsFromDb { get; set; }
        public int EventsFromApi { get; set; }
    }

    public class GolferEventUsage
    {
        public string TournamentId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Rounds { get; set; }
    }

    public class GolferInputSummary
    {
        public string PlayerId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public bool ExplicitEvents { get; set; }
        public int CurrentRounds { get; set; }
        public List<GolferEventUsage> Events { get; set; } = new();
    }

    public class InputsSummary
    {
        public string TournamentId { get; set; } = string.Empty;
        public string TournamentName { get; set; } = string.Empty;
        public string EventType { get; set; } = "pga";
        public bool HasCut { get; set; } = true;
        public int? CutTopN { get; set; }
        public int FieldSize { get; set; }
        public List<StatInputSummary> Stats { get; set; } = new();
        public PerformanceInputSummary Performance { get; set; } = new();
        public List<GolferInputSummary> Golfers { get; set; } = new();
        public DateTime? DataAsOfUtc { get; set; }
        public List<string> Warnings { get; set; } = new();
    }

    public class SimulateResponse
    {
        public int SimulationsRun { get; set; }
        public int Seed { get; set; }
        public long ElapsedMs { get; set; }
        public DateTime GeneratedUtc { get; set; }
        public int GolfersModeled { get; set; }
        public List<GolferPrediction> Golfers { get; set; } = new();
        public InputsSummary InputsSummary { get; set; } = new();
    }
}
