using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using GolfStatsApp.Shared.Models;
using GolfStatsApp.Server.Services;

namespace GolfStatsApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FantasyGolfController : ControllerBase
    {
        private readonly ILogger<FantasyGolfController> _logger;
        private readonly GolfSimulationService _simulationService;

        public FantasyGolfController(ILogger<FantasyGolfController> logger, GolfSimulationService simulationService)
        {
            _logger = logger;
            _simulationService = simulationService;
        }

        [HttpPost("simulate")]
        public async Task<ActionResult<SimulationResult>> RunSimulation([FromBody] SimulationRequest request)
        {
            try
            {
                _logger.LogInformation("Starting fantasy golf simulation for event type: {EventType}", request.EventType);

                // Validate request
                if (!request.Players.Any())
                {
                    return BadRequest("At least one player is required");
                }

                if (string.IsNullOrEmpty(request.Captain))
                {
                    return BadRequest("Captain must be specified");
                }

                if (!request.Players.Any(p => p.Name == request.Captain))
                {
                    return BadRequest("Captain must be one of the players in the starting lineup");
                }

                // Run simulation using C# service
                var result = await _simulationService.RunSimulation(request, 10000);
                
                _logger.LogInformation("Simulation completed successfully");
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running fantasy golf simulation");
                return StatusCode(500, "Internal server error while running simulation");
            }
        }
    }
}
