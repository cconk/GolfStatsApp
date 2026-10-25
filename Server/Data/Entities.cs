using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;

namespace GolfStatsApp.Server.Data
{
    public class AppUser : IdentityUser
    {
        public string DisplayName { get; set; } = string.Empty;
    }

    public class League
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string JoinCode { get; set; } = string.Empty;
        public string OwnerId { get; set; } = string.Empty;
        public int PlayersPerTeam { get; set; } = 4;
        public string DraftStatus { get; set; } = "NotStarted";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public List<LeagueMember> Members { get; set; } = new();
        public List<DraftPick> Picks { get; set; } = new();
    }

    public class LeagueMember
    {
        public int Id { get; set; }
        public int LeagueId { get; set; }
        public League? League { get; set; }
        public string UserId { get; set; } = string.Empty;
        public AppUser? User { get; set; }
        public int? DraftPosition { get; set; }
    }

    public class DraftPick
    {
        public int Id { get; set; }
        public int LeagueId { get; set; }
        public League? League { get; set; }
        public int MemberId { get; set; }
        public LeagueMember? Member { get; set; }
        public int PickNumber { get; set; }
        public string PlayerName { get; set; } = string.Empty;
    }

    public class PlayerPoints
    {
        public int Id { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public string Tournament { get; set; } = string.Empty;
        public double Points { get; set; }
    }
}
