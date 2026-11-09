using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GolfStatsApp.Server.PgaTour;
using GolfStatsApp.Shared.Models;

namespace GolfStatsApp.Server.Services.Prediction
{
    /// <summary>
    /// Thin seam over <see cref="PgaTourApiClient"/> used by predictive modeling so the PGA Tour API
    /// can be replaced in tests (and so failures can be simulated).
    /// </summary>
    public interface IPgaDataSource
    {
        IReadOnlyList<StatCatalogEntry> GetStatCatalog();
        Task<string> GetCurrentTournamentIdAsync(CancellationToken ct);
        Task<List<PlayerDirectoryEntry>> GetPlayersAsync(CancellationToken ct);
        Task<StatRankings> GetStatAsync(string statId, int? year, CancellationToken ct);
        Task<List<PlayerResultRow>> GetPlayerResultsAsync(string playerId, int? season, CancellationToken ct);
        Task<List<ScheduleEvent>> GetScheduleAsync(int year, CancellationToken ct);
        Task<List<FieldPlayer>> GetFieldAsync(string tournamentId, CancellationToken ct);
        Task<List<TournamentLeaderboardRow>> GetLeaderboardAsync(string tournamentId, CancellationToken ct);
    }

    public class PgaTourDataSource : IPgaDataSource
    {
        private readonly PgaTourApiClient _client;

        public PgaTourDataSource(PgaTourApiClient client) => _client = client;

        public IReadOnlyList<StatCatalogEntry> GetStatCatalog() => _client.GetStatCatalog();
        public Task<string> GetCurrentTournamentIdAsync(CancellationToken ct) => _client.GetCurrentTournamentIdAsync("R", ct);
        public Task<List<PlayerDirectoryEntry>> GetPlayersAsync(CancellationToken ct) => _client.GetPlayersAsync("R", ct);
        public Task<StatRankings> GetStatAsync(string statId, int? year, CancellationToken ct) => _client.GetStatAsync(statId, year, "R", ct);
        public Task<List<PlayerResultRow>> GetPlayerResultsAsync(string playerId, int? season, CancellationToken ct) => _client.GetPlayerResultsAsync(playerId, season, ct);
        public Task<List<ScheduleEvent>> GetScheduleAsync(int year, CancellationToken ct) => _client.GetScheduleAsync(year, "R", ct);
        public Task<List<FieldPlayer>> GetFieldAsync(string tournamentId, CancellationToken ct) => _client.GetFieldAsync(tournamentId, ct: ct);
        public Task<List<TournamentLeaderboardRow>> GetLeaderboardAsync(string tournamentId, CancellationToken ct) => _client.GetLeaderboardAsync(tournamentId, ct);
    }

    /// <summary>Last-resort player source (PlayerDataService falls back to the bundled CSV).</summary>
    public interface IFallbackPlayerSource
    {
        Task<List<GolfPlayerData>> GetPlayersAsync();
    }

    public class PlayerDataFallbackSource : IFallbackPlayerSource
    {
        private readonly PlayerDataService _players;

        public PlayerDataFallbackSource(PlayerDataService players) => _players = players;

        public Task<List<GolfPlayerData>> GetPlayersAsync() => _players.GetPlayersAsync();
    }
}
