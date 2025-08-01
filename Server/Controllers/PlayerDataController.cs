using GolfStatsApp.Server.Services;
using GolfStatsApp.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

namespace GolfStatsApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PlayerDataController : ControllerBase
    {
        private readonly Services.PlayerDataService _playerDataService;

        public PlayerDataController(Services.PlayerDataService playerDataService)
        {
            _playerDataService = playerDataService;
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
    }
}
