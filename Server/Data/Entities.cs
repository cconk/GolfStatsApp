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
        // Number of starting lineup slots per team.
        public int PlayersPerTeam { get; set; } = 4;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public List<LeagueMember> Members { get; set; } = new();
        public List<LeagueTournament> Tournaments { get; set; } = new();
    }

    public class LeagueMember
    {
        public int Id { get; set; }
        public int LeagueId { get; set; }
        public League? League { get; set; }
        public string UserId { get; set; } = string.Empty;
        public AppUser? User { get; set; }
    }

    /// <summary>A PGA TOUR event on a league's season schedule.</summary>
    public class LeagueTournament
    {
        public int Id { get; set; }
        public int LeagueId { get; set; }
        public League? League { get; set; }
        public string TournamentId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Segment { get; set; } = 1;
        public string EventType { get; set; } = "pga";
        public DateTime? StartDate { get; set; }
        public List<TeamTournamentEntry> Entries { get; set; } = new();
    }

    /// <summary>One member's entry (captain and lineup history) for a league tournament.</summary>
    public class TeamTournamentEntry
    {
        public int Id { get; set; }
        public int LeagueTournamentId { get; set; }
        public LeagueTournament? LeagueTournament { get; set; }
        public int MemberId { get; set; }
        public LeagueMember? Member { get; set; }
        public string? CaptainPlayerId { get; set; }
        public string? CaptainName { get; set; }
        public int CaptainFromRound { get; set; } = 1;
        public List<LineupSlot> Slots { get; set; } = new();
    }

    /// <summary>A lineup slot that applies from <see cref="FromRound"/> until superseded by a later version.</summary>
    public class LineupSlot
    {
        public int Id { get; set; }
        public int EntryId { get; set; }
        public TeamTournamentEntry? Entry { get; set; }
        public int FromRound { get; set; } = 1;
        public string PlayerId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public bool IsStarter { get; set; }
        public int BenchPriority { get; set; }
    }

    /// <summary>Synced PGA TOUR tournament status shared by every league.</summary>
    public class SyncedTournament
    {
        public string TournamentId { get; set; } = string.Empty;
        public bool IsFinal { get; set; }
        public bool CutKnown { get; set; }
        public string CompleteRounds { get; set; } = string.Empty;
        public int StartedRound { get; set; }
        public DateTime LastSyncedUtc { get; set; }
    }

    public class TournamentGolfer
    {
        public int Id { get; set; }
        public string TournamentId { get; set; } = string.Empty;
        public string PlayerId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public string? Position { get; set; }
        public string? State { get; set; }
    }

    public class GolferRound
    {
        public int Id { get; set; }
        public string TournamentId { get; set; } = string.Empty;
        public string PlayerId { get; set; } = string.Empty;
        public int Round { get; set; }
        public int Holes { get; set; }
        public int ToPar { get; set; }
        public double HolePoints { get; set; }
        public bool BogeyFree { get; set; }
    }

    public class PlayerPoints
    {
        public int Id { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public string Tournament { get; set; } = string.Empty;
        public double Points { get; set; }
    }

    /// <summary>Persisted season stat value captured from the PGA Tour API (fallback tier for predictive modeling).</summary>
    public class StatSnapshot
    {
        public int Id { get; set; }
        public string StatId { get; set; } = string.Empty;
        public string PlayerId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public int Season { get; set; }
        public double Value { get; set; }
        public int? Rank { get; set; }
        public DateTime CapturedUtc { get; set; }
    }

    /// <summary>Persisted tournament result for a player (recent/historical performance source).</summary>
    public class PlayerResultSnapshot
    {
        public int Id { get; set; }
        public string PlayerId { get; set; } = string.Empty;
        public string TournamentId { get; set; } = string.Empty;
        public string TournamentName { get; set; } = string.Empty;
        public int Season { get; set; }
        public DateTime? EndDate { get; set; }
        public string EventType { get; set; } = "pga";
        public string? Position { get; set; }
        public string Status { get; set; } = "finished";
        public int? ToPar { get; set; }
        public int Rounds { get; set; }
        public DateTime CapturedUtc { get; set; }
        public List<PlayerResultRound> RoundScores { get; set; } = new();
    }

    public class PlayerResultRound
    {
        public int Id { get; set; }
        public int SnapshotId { get; set; }
        public PlayerResultSnapshot? Snapshot { get; set; }
        public int Round { get; set; }
        public int ToPar { get; set; }
    }
}
