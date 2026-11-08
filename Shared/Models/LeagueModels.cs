using System;
using System.Collections.Generic;

namespace GolfStatsApp.Shared.Models
{
    public class AuthRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
    }

    public class UserInfo
    {
        public bool IsAuthenticated { get; set; }
        public string? Email { get; set; }
        public string? DisplayName { get; set; }
        public bool IsAdmin { get; set; }
    }

    public class CreateLeagueRequest
    {
        public string Name { get; set; } = string.Empty;
        /// <summary>Number of starting lineup slots per team.</summary>
        public int PlayersPerTeam { get; set; } = 4;
    }

    public class JoinLeagueRequest
    {
        public string JoinCode { get; set; } = string.Empty;
    }

    public class LeagueSummary
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string JoinCode { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public int PlayersPerTeam { get; set; }
        public int TournamentCount { get; set; }
        public bool IsOwner { get; set; }
    }

    public class LeagueMemberDto
    {
        public int Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public bool IsOwner { get; set; }
        public bool IsMe { get; set; }
    }

    public static class TournamentStatuses
    {
        public const string Upcoming = "Upcoming";
        public const string InProgress = "InProgress";
        public const string Final = "Final";
    }

    public static class LeagueRules
    {
        public const int BenchSlots = 2;
        public const int StartsPerSegment = 3;
        public const int FedExBonusMaxSegment = 3;
        public const double FedExBonusFactor = 0.1;
        public static readonly string[] EventTypes = { "pga", "major", "signature", "additional" };
    }

    public class LeagueTournamentDto
    {
        public int Id { get; set; }
        public string TournamentId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Segment { get; set; }
        public string EventType { get; set; } = "pga";
        public DateTime? StartDate { get; set; }
        public string Status { get; set; } = TournamentStatuses.Upcoming;
        public int StartedRound { get; set; }
    }

    public class LeagueState
    {
        public LeagueSummary League { get; set; } = new();
        public List<LeagueMemberDto> Members { get; set; } = new();
        public List<LeagueTournamentDto> Tournaments { get; set; } = new();
    }

    public class AddLeagueTournamentRequest
    {
        public string TournamentId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Segment { get; set; } = 1;
        public string EventType { get; set; } = "pga";
        public DateTime? StartDate { get; set; }
    }

    /// <summary>Subset of the PGA TOUR schedule payload used to pick league tournaments.</summary>
    public class PgaScheduleEvent
    {
        public string? TournamentId { get; set; }
        public string? TournamentName { get; set; }
        public string? DisplayDate { get; set; }
        public string? Status { get; set; }
    }

    public class FieldGolferDto
    {
        public string PlayerId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Position { get; set; }
        public bool IsOut { get; set; }
        public int StartsUsed { get; set; }
        public int StartsRemaining { get; set; }
        public double? PreviousEventPoints { get; set; }
        public double SeasonPoints { get; set; }
    }

    public class LineupPlayerDto
    {
        public string PlayerId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int BenchPriority { get; set; }
    }

    public class LineupState
    {
        public LeagueTournamentDto Tournament { get; set; } = new();
        public int StarterSlots { get; set; }
        public int BenchSlots { get; set; } = LeagueRules.BenchSlots;
        public bool FieldAvailable { get; set; }
        /// <summary>First round a saved lineup would apply to; 0 when the tournament can no longer be changed.</summary>
        public int NextEditableRound { get; set; }
        public bool CaptainLocked { get; set; }
        public string? CaptainPlayerId { get; set; }
        public List<LineupPlayerDto> Starters { get; set; } = new();
        public List<LineupPlayerDto> Bench { get; set; } = new();
        public List<FieldGolferDto> Field { get; set; } = new();
        public TeamTournamentScore? Score { get; set; }
    }

    public class SaveLineupRequest
    {
        public List<string> StarterIds { get; set; } = new();
        /// <summary>Bench golfers in auto-swap priority order (index 0 = priority 1).</summary>
        public List<string> BenchIds { get; set; } = new();
        public string? CaptainId { get; set; }
    }

    public class GolferScoreDto
    {
        public string PlayerId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsCaptain { get; set; }
        public List<int> RoundsStarted { get; set; } = new();
        public double HolePoints { get; set; }
        public double BonusPoints { get; set; }
        public double FedExPoints { get; set; }
        public double Total { get; set; }
    }

    public class TeamTournamentScore
    {
        public int MemberId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public double Total { get; set; }
        public List<GolferScoreDto> Golfers { get; set; } = new();
    }

    public class LeaderboardTournament
    {
        public int LeagueTournamentId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Segment { get; set; }
        public double Points { get; set; }
    }

    public class LeaderboardEntry
    {
        public int Rank { get; set; }
        public int MemberId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public double Points { get; set; }
        public List<LeaderboardTournament> Tournaments { get; set; } = new();
    }

    public class PlayerPointsEntry
    {
        public int Id { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public string Tournament { get; set; } = string.Empty;
        public double Points { get; set; }
    }
}
