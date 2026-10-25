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

    public static class DraftStatuses
    {
        public const string NotStarted = "NotStarted";
        public const string InProgress = "InProgress";
        public const string Complete = "Complete";
    }

    public class CreateLeagueRequest
    {
        public string Name { get; set; } = string.Empty;
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
        public string DraftStatus { get; set; } = DraftStatuses.NotStarted;
        public bool IsOwner { get; set; }
    }

    public class LeagueMemberDto
    {
        public int Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public int? DraftPosition { get; set; }
        public bool IsOwner { get; set; }
        public bool IsMe { get; set; }
    }

    public class DraftPickDto
    {
        public int PickNumber { get; set; }
        public int MemberId { get; set; }
        public string MemberName { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
    }

    public class DraftState
    {
        public LeagueSummary League { get; set; } = new();
        public List<LeagueMemberDto> Members { get; set; } = new();
        public List<DraftPickDto> Picks { get; set; } = new();
        public int TotalPicks { get; set; }
        public int? CurrentMemberId { get; set; }
        public string? CurrentMemberName { get; set; }
        public bool IsMyTurn { get; set; }
    }

    public class MakePickRequest
    {
        public string PlayerName { get; set; } = string.Empty;
    }

    public class LeaderboardPlayer
    {
        public string PlayerName { get; set; } = string.Empty;
        public double Points { get; set; }
    }

    public class LeaderboardEntry
    {
        public int Rank { get; set; }
        public int MemberId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public double Points { get; set; }
        public List<LeaderboardPlayer> Players { get; set; } = new();
    }

    public class PlayerPointsEntry
    {
        public int Id { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public string Tournament { get; set; } = string.Empty;
        public double Points { get; set; }
    }

    public static class DraftOrder
    {
        // Snake draft: odd rounds run in reverse order.
        public static int MemberIndexForPick(int pickIndex, int memberCount)
        {
            if (memberCount <= 0) return -1;
            var round = pickIndex / memberCount;
            var index = pickIndex % memberCount;
            return round % 2 == 0 ? index : memberCount - 1 - index;
        }
    }
}
