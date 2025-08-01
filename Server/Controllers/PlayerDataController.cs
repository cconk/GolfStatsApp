using GolfStatsApp.Server.Services;
using GolfStatsApp.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq;

namespace GolfStatsApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PlayerDataController : ControllerBase
    {
        private readonly Services.PlayerDataService _playerDataService;
        private readonly IWebHostEnvironment _environment;

        public PlayerDataController(Services.PlayerDataService playerDataService, IWebHostEnvironment environment)
        {
            _playerDataService = playerDataService;
            _environment = environment;
        }

        [HttpGet("players")]
        public async Task<ActionResult<List<GolfPlayerData>>> GetPlayers()
        {
            try
            {
                var players = await _playerDataService.GetPlayersAsync();
                return Ok(players);
            }
            catch (Exception ex)
            {
                // Log the detailed error for debugging
                return StatusCode(500, new { 
                    error = "Error loading player data", 
                    details = ex.Message,
                    stackTrace = ex.StackTrace 
                });
            }
        }

        [HttpGet("debug")]
        public ActionResult GetDebugInfo()
        {
            try
            {
                var filePath = Path.Combine(_environment.ContentRootPath, "Data", "merged_golf_stats.csv");
                var dataDir = Path.Combine(_environment.ContentRootPath, "Data");
                
                var debugInfo = new
                {
                    Environment = _environment.EnvironmentName,
                    ContentRootPath = _environment.ContentRootPath,
                    WebRootPath = _environment.WebRootPath,
                    ExpectedFilePath = filePath,
                    FileExists = System.IO.File.Exists(filePath),
                    DataDirectoryExists = Directory.Exists(dataDir),
                    FilesInDataDirectory = Directory.Exists(dataDir) ? Directory.GetFiles(dataDir) : new string[0],
                    CurrentDirectory = Directory.GetCurrentDirectory(),
                    AllFilesInRoot = Directory.GetFiles(_environment.ContentRootPath, "*", SearchOption.AllDirectories)
                        .Where(f => f.Contains("csv") || f.Contains("golf"))
                        .ToArray()
                };
                
                return Ok(debugInfo);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    error = "Error getting debug info", 
                    details = ex.Message 
                });
            }
        }
    }
}
