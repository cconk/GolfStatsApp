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
        public DbSet<DraftPick> DraftPicks => Set<DraftPick>();
        public DbSet<PlayerPoints> PlayerPoints => Set<PlayerPoints>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<TeamStats>().HasIndex(t => t.TeamName).IsUnique();
            builder.Entity<League>().HasIndex(l => l.JoinCode).IsUnique();
            builder.Entity<LeagueMember>().HasIndex(m => new { m.LeagueId, m.UserId }).IsUnique();
            builder.Entity<DraftPick>().HasIndex(p => new { p.LeagueId, p.PickNumber }).IsUnique();
            builder.Entity<DraftPick>().HasIndex(p => new { p.LeagueId, p.PlayerName }).IsUnique();
            builder.Entity<DraftPick>().HasOne(p => p.Member).WithMany().OnDelete(DeleteBehavior.Cascade);
        }
    }

    public static class DbInitializer
    {
        public static async Task InitializeAsync(AppDbContext db, string contentRoot)
        {
            await db.Database.EnsureCreatedAsync();

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
    }
}
