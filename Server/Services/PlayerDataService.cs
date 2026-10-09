using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GolfStatsApp.Shared.Models;
using GolfStatsApp.Server.PgaTour;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System;

namespace GolfStatsApp.Server.Services
{
    public class PlayerDataService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<PlayerDataService> _logger;
        private const string BirdieAverageStatId = "156";
        private const string BogeyAvoidanceStatId = "02414";
        private const string CacheKey = "pga:simulation-inputs";

        private readonly PgaTourApiClient _pgaTourClient;
        private readonly IMemoryCache _cache;
        private List<GolfPlayerData>? _players;

        public PlayerDataService(IWebHostEnvironment environment, ILogger<PlayerDataService> logger, PgaTourApiClient pgaTourClient, IMemoryCache cache)
        {
            _environment = environment;
            _logger = logger;
            _pgaTourClient = pgaTourClient;
            _cache = cache;
        }

        public async Task<List<GolfPlayerData>> GetPlayersAsync()
        {
            if (_cache.TryGetValue(CacheKey, out List<GolfPlayerData>? cached) && cached != null)
            {
                return cached;
            }

            var live = await LoadPlayersFromPgaTour();
            if (live.Count > 0)
            {
                _cache.Set(CacheKey, live, TimeSpan.FromHours(6));
                return live;
            }

            await LoadPlayersFromCsv();
            return _players ?? new List<GolfPlayerData>();
        }

        private async Task<List<GolfPlayerData>> LoadPlayersFromPgaTour()
        {
            try
            {
                var birdiesTask = _pgaTourClient.GetStatAsync(BirdieAverageStatId);
                var bogeysTask = _pgaTourClient.GetStatAsync(BogeyAvoidanceStatId);
                await Task.WhenAll(birdiesTask, bogeysTask);

                var bogeysByPlayer = bogeysTask.Result.Rows
                    .Where(r => r.PlayerId != null)
                    .GroupBy(r => r.PlayerId!)
                    .ToDictionary(g => g.Key, g => BogeysPerRound(g.First()));

                var players = birdiesTask.Result.Rows
                    .Where(r => r.PlayerId != null && bogeysByPlayer.ContainsKey(r.PlayerId))
                    .Select(r => new GolfPlayerData
                    {
                        PlayerId = r.PlayerId!,
                        PlayerName = r.PlayerName?.Trim() ?? string.Empty,
                        Rank = r.Rank ?? 0,
                        Birdies = StatAverage(r),
                        Bogeys = bogeysByPlayer[r.PlayerId!]
                    })
                    .Where(p => !string.IsNullOrEmpty(p.PlayerName) && p.Birdies > 0 && p.Bogeys > 0)
                    .OrderBy(p => p.Rank)
                    .ToList();

                _logger.LogInformation("Loaded {Count} players from PGA Tour API (birdie rows {B}, bogey rows {G})",
                    players.Count, birdiesTask.Result.Rows.Count, bogeysTask.Result.Rows.Count);
                return players;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PGA Tour API unavailable; falling back to CSV");
                return new List<GolfPlayerData>();
            }
        }

        private static double StatAverage(StatRankingRow row) => StatNumber(row, "avg");

        private static double BogeysPerRound(StatRankingRow row)
        {
            var holes = StatNumber(row, "holes_played");
            return holes > 0 ? Math.Round(StatNumber(row, "bogeys") / holes * 18, 2) : 0;
        }

        private static double StatNumber(StatRankingRow row, string key) =>
            row.Values.TryGetValue(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
                && double.TryParse(v.GetString()?.Replace(",", "").TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

        private async Task LoadPlayersFromCsv()
        {
            try
            {
                var filePath = Path.Combine(_environment.ContentRootPath, "Data", "merged_golf_stats.csv");
                _logger.LogInformation($"Environment: {_environment.EnvironmentName}");
                _logger.LogInformation($"ContentRootPath: {_environment.ContentRootPath}");
                _logger.LogInformation($"WebRootPath: {_environment.WebRootPath}");
                _logger.LogInformation($"Attempting to load CSV from: {filePath}");
                
                // Also check if the Data directory exists
                var dataDir = Path.Combine(_environment.ContentRootPath, "Data");
                _logger.LogInformation($"Data directory exists: {Directory.Exists(dataDir)}");
                if (Directory.Exists(dataDir))
                {
                    var files = Directory.GetFiles(dataDir);
                    _logger.LogInformation($"Files in Data directory: {string.Join(", ", files)}");
                }
                
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning($"CSV file not found at: {filePath}");
                    
                    // Try alternative locations
                    var altPath1 = Path.Combine(_environment.WebRootPath ?? "", "Data", "merged_golf_stats.csv");
                    var altPath2 = Path.Combine(Directory.GetCurrentDirectory(), "Data", "merged_golf_stats.csv");
                    var altPath3 = Path.Combine(Directory.GetCurrentDirectory(), "merged_golf_stats.csv");
                    
                    _logger.LogInformation($"Checking alternative path 1: {altPath1} - Exists: {File.Exists(altPath1)}");
                    _logger.LogInformation($"Checking alternative path 2: {altPath2} - Exists: {File.Exists(altPath2)}");
                    _logger.LogInformation($"Checking alternative path 3: {altPath3} - Exists: {File.Exists(altPath3)}");
                    
                    if (File.Exists(altPath1))
                        filePath = altPath1;
                    else if (File.Exists(altPath2))
                        filePath = altPath2;
                    else if (File.Exists(altPath3))
                        filePath = altPath3;
                    else
                    {
                        _players = new List<GolfPlayerData>();
                        return;
                    }
                    
                    _logger.LogInformation($"Using alternative path: {filePath}");
                }

                var lines = await File.ReadAllLinesAsync(filePath);
                _players = new List<GolfPlayerData>();
                _logger.LogInformation($"Read {lines.Length} lines from CSV file");

                // Skip header row
                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var columns = ParseCsvLine(line);
                    if (columns.Length >= 12)
                    {
                        try
                        {
                            var player = new GolfPlayerData
                            {
                                Rank = int.Parse(columns[0]),
                                PlayerName = columns[3].Trim(),
                                Birdies = double.Parse(columns[4], CultureInfo.InvariantCulture),
                                Bogeys = double.Parse(columns[9], CultureInfo.InvariantCulture)
                            };

                            // Only add players with valid data
                            if (!string.IsNullOrEmpty(player.PlayerName) && player.Birdies > 0 && player.Bogeys > 0)
                            {
                                _players.Add(player);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to parse line {i}: {ex.Message}");
                            continue;
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Line {i} has insufficient columns ({columns.Length}): {line}");
                    }
                }

                // Sort by rank
                _players = _players.OrderBy(p => p.Rank).ToList();
                _logger.LogInformation($"Successfully loaded {_players.Count} players from CSV");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading players from CSV");
                _players = new List<GolfPlayerData>();
                // Don't throw, return empty list instead
            }
        }

        private string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            var inQuotes = false;
            var currentField = "";

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(currentField);
                    currentField = "";
                }
                else
                {
                    currentField += c;
                }
            }

            result.Add(currentField);
            return result.ToArray();
        }
    }
}
