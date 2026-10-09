using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GolfStatsApp.Server.PgaTour
{
    public record TournamentLeaderboardRow
    {
        public string? PlayerId { get; init; }
        public string? DisplayName { get; init; }
        public string? Country { get; init; }
        public string? Position { get; init; }
        public string? Total { get; init; }
        public string? Thru { get; init; }
        public string? Score { get; init; }
        public int? CurrentRound { get; init; }
        public string? PlayerState { get; init; }
        public int? TotalStrokes { get; init; }
        public string? Rounds { get; init; }
    }

    public record FieldPlayer
    {
        public string? PlayerId { get; init; }
        public string? DisplayName { get; init; }
        public string? Country { get; init; }
        public bool? Amateur { get; init; }
        public int? Owgr { get; init; }
        public string? Status { get; init; }
        public bool Alternate { get; init; }
        public bool? Withdrawn { get; init; }
    }

    public record OddsRow
    {
        public string? PlayerId { get; init; }
        public string? DisplayName { get; init; }
        public string? Odds { get; init; }
        public double? OddsSort { get; init; }
        public string? OddsDirection { get; init; }
    }

    public record CourseHoleStat
    {
        public string? CourseName { get; init; }
        public int? RoundNumber { get; init; }
        public string? RoundHeader { get; init; }
        public int? HoleNumber { get; init; }
        public int? Par { get; init; }
        public string? Yardage { get; init; }
        public string? ScoringAverage { get; init; }
        public string? ScoringAverageDiff { get; init; }
        public int? Eagles { get; init; }
        public int? Birdies { get; init; }
        public int? Pars { get; init; }
        public int? Bogeys { get; init; }
        public int? DoubleBogey { get; init; }
        public int? Rank { get; init; }
    }

    public record ScorecardHole
    {
        public int? RoundNumber { get; init; }
        public int? HoleNumber { get; init; }
        public int? Par { get; init; }
        public string? Score { get; init; }
        public string? Status { get; init; }
        public int? Yardage { get; init; }
        public string? RoundScore { get; init; }
        public string? CourseName { get; init; }
    }

    public record ScheduleEvent
    {
        public string? TournamentId { get; init; }
        public string? TournamentName { get; init; }
        public string? DisplayDate { get; init; }
        public string? Status { get; init; }
        public string? Purse { get; init; }
        public string? FedExCupPoints { get; init; }
        public string? Champion { get; init; }
        public string? CourseName { get; init; }
        public string? City { get; init; }
        public string? State { get; init; }
        public string? Country { get; init; }
    }

    public record PlayerDirectoryEntry
    {
        public string? PlayerId { get; init; }
        public string? DisplayName { get; init; }
        public string? Country { get; init; }
        public bool? IsActive { get; init; }
        public int? Age { get; init; }
    }

    public record PlayerProfile
    {
        public string? PlayerId { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public string? Country { get; init; }
        public string? Born { get; init; }
        public string? Age { get; init; }
        public string? Birthplace { get; init; }
        public string? College { get; init; }
        public string? TurnedPro { get; init; }
        public List<ProfileHighlight> Highlights { get; init; } = new();
    }

    public record ProfileHighlight(string? Title, string? Value, string? Subtitle);

    public record PlayerStat
    {
        public string? StatId { get; init; }
        public string? Title { get; init; }
        public string? Rank { get; init; }
        public string? Value { get; init; }
        public string? Category { get; init; }
        public string? FieldAverage { get; init; }
    }

    public record FedExCupRow
    {
        public string? PlayerId { get; init; }
        public string? DisplayName { get; init; }
        public string? Country { get; init; }
        public string? ThisWeekRank { get; init; }
        public string? PreviousWeekRank { get; init; }
        public string? ProjectedPoints { get; init; }
        public string? OfficialPoints { get; init; }
    }

    public record StatCatalogEntry(
        [property: JsonPropertyName("stat_id")] string StatId,
        [property: JsonPropertyName("stat_name")] string StatName,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("subcategory")] string Subcategory);

    /// <summary>One player's row in a stat leaderboard. Stat columns vary by stat and are flattened via <see cref="Values"/>.</summary>
    public record StatRankingRow
    {
        public string? StatId { get; init; }
        public int? Rank { get; init; }
        public string? PlayerId { get; init; }
        public string? PlayerName { get; init; }
        public string? Country { get; init; }

        public Dictionary<string, JsonElement> Values { get; init; } = new();
    }

    public record StatRankings(string? StatTitle, string? StatDescription, string? TourAverage, int? Year, IReadOnlyList<string> Headers, List<StatRankingRow> Rows);

    /// <summary>One tournament result for a player. Columns vary by season and are flattened via <see cref="Values"/>.</summary>
    public record PlayerResultRow
    {
        public string? Season { get; init; }
        public string? TournamentId { get; init; }

        public Dictionary<string, JsonElement> Values { get; init; } = new();
    }
}
