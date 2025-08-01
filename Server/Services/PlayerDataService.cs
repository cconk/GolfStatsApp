using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GolfStatsApp.Shared.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System;

namespace GolfStatsApp.Server.Services
{
    public class PlayerDataService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<PlayerDataService> _logger;
        private List<GolfPlayerData>? _players;

        public PlayerDataService(IWebHostEnvironment environment, ILogger<PlayerDataService> logger)
        {
            _environment = environment;
            _logger = logger;
        }

        public async Task<List<GolfPlayerData>> GetPlayersAsync()
        {
            if (_players == null)
            {
                await LoadPlayersFromCsv();
            }
            return _players ?? new List<GolfPlayerData>();
        }

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
