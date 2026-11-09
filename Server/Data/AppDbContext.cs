using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GolfStatsApp.Shared;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GolfStatsApp.Server.Data
{
    public class AppDbContext : IdentityDbContext<AppUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<TeamStats> Teams => Set<TeamStats>();
        public DbSet<League> Leagues => Set<League>();
        public DbSet<LeagueMember> LeagueMembers => Set<LeagueMember>();
        public DbSet<PlayerPoints> PlayerPoints => Set<PlayerPoints>();
        public DbSet<LeagueTournament> LeagueTournaments => Set<LeagueTournament>();
        public DbSet<TeamTournamentEntry> TeamTournamentEntries => Set<TeamTournamentEntry>();
        public DbSet<LineupSlot> LineupSlots => Set<LineupSlot>();
        public DbSet<SyncedTournament> SyncedTournaments => Set<SyncedTournament>();
        public DbSet<TournamentGolfer> TournamentGolfers => Set<TournamentGolfer>();
        public DbSet<GolferRound> GolferRounds => Set<GolferRound>();
        public DbSet<StatSnapshot> StatSnapshots => Set<StatSnapshot>();
        public DbSet<PlayerResultSnapshot> PlayerResultSnapshots => Set<PlayerResultSnapshot>();
        public DbSet<PlayerResultRound> PlayerResultRounds => Set<PlayerResultRound>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<TeamStats>().HasIndex(t => t.TeamName).IsUnique();
            builder.Entity<League>().HasIndex(l => l.JoinCode).IsUnique();
            builder.Entity<LeagueMember>().HasIndex(m => new { m.LeagueId, m.UserId }).IsUnique();
            builder.Entity<LeagueTournament>().HasIndex(t => new { t.LeagueId, t.TournamentId }).IsUnique();
            builder.Entity<TeamTournamentEntry>().HasIndex(e => new { e.LeagueTournamentId, e.MemberId }).IsUnique();
            // SQL Server disallows a second cascade path (League -> Members -> Entries and League -> Tournaments -> Entries).
            builder.Entity<TeamTournamentEntry>().HasOne(e => e.Member).WithMany().OnDelete(DeleteBehavior.ClientCascade);
            builder.Entity<LineupSlot>().HasIndex(s => new { s.EntryId, s.FromRound });
            builder.Entity<SyncedTournament>().HasKey(t => t.TournamentId);
            builder.Entity<TournamentGolfer>().HasIndex(g => new { g.TournamentId, g.PlayerId }).IsUnique();
            builder.Entity<GolferRound>().HasIndex(r => new { r.TournamentId, r.PlayerId, r.Round }).IsUnique();
            builder.Entity<StatSnapshot>().HasIndex(s => new { s.StatId, s.PlayerId, s.Season }).IsUnique();
            builder.Entity<PlayerResultSnapshot>().HasIndex(s => new { s.PlayerId, s.TournamentId }).IsUnique();
            builder.Entity<PlayerResultSnapshot>().HasMany(s => s.RoundScores).WithOne(r => r.Snapshot!)
                .HasForeignKey(r => r.SnapshotId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PlayerResultRound>().HasIndex(r => new { r.SnapshotId, r.Round }).IsUnique();
        }
    }

    public static class DbInitializer
    {
        public static async Task InitializeAsync(AppDbContext db, string contentRoot)
        {
            if (db.Database.IsSqlite())
            {
                if (!await db.Database.EnsureCreatedAsync())
                    await UpgradeSchemaAsync(db);
            }
            else if (!await db.Database.CanConnectAsync())
            {
                throw new InvalidOperationException(
                    "Cannot connect to the SQL Server database. Run the scripts in the /database folder (see database/README.md) and check ConnectionStrings:Default.");
            }

            if (await db.Teams.AnyAsync()) return;

            var seedPath = Path.Combine(contentRoot, "Data", "teamstats.seed.json");
            if (!File.Exists(seedPath)) return;

            var json = await File.ReadAllTextAsync(seedPath);
            var teams = JsonSerializer.Deserialize<System.Collections.Generic.List<TeamStats>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (teams is null || teams.Count == 0) return;

            foreach (var t in teams) t.Id = 0;
            db.Teams.AddRange(teams);
            await db.SaveChangesAsync();
        }

        // EnsureCreated does nothing for an existing database, so add any newly introduced tables/indexes
        // and drop the column left behind by the removed draft feature.
        private static async Task UpgradeSchemaAsync(AppDbContext db)
        {
            var script = db.Database.GenerateCreateScript()
                .Replace("CREATE TABLE \"", "CREATE TABLE IF NOT EXISTS \"")
                .Replace("CREATE UNIQUE INDEX \"", "CREATE UNIQUE INDEX IF NOT EXISTS \"")
                .Replace("CREATE INDEX \"", "CREATE INDEX IF NOT EXISTS \"");
            foreach (var statement in script.Split(";", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                await db.Database.ExecuteSqlRawAsync(statement);

            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Leagues') WHERE name = 'DraftStatus'";
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0)
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Leagues\" DROP COLUMN \"DraftStatus\"");
        }
    }
}
