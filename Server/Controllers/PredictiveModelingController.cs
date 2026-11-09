using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GolfStatsApp.Server.Services;
using GolfStatsApp.Server.Services.Prediction;
using GolfStatsApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace GolfStatsApp.Server.Controllers
{
    [ApiController]
    [Route("api/predictive-modeling")]
    public class PredictiveModelingController : ControllerBase
    {
        private readonly GolferSearchService _search;
        private readonly ModelInputService _inputs;
        private readonly PredictionService _prediction;
        private readonly SimulationConcurrencyLimiter _limiter;
        private readonly PredictiveModelingOptions _options;
        private readonly ILogger<PredictiveModelingController> _logger;

        public PredictiveModelingController(GolferSearchService search, ModelInputService inputs, PredictionService prediction,
            SimulationConcurrencyLimiter limiter, PredictiveModelingOptions options, ILogger<PredictiveModelingController> logger)
        {
            _search = search;
            _inputs = inputs;
            _prediction = prediction;
            _limiter = limiter;
            _options = options;
            _logger = logger;
        }

        [HttpGet("golfers")]
        public Task<ActionResult> Golfers(string? query, int limit = PredictionLimits.DefaultSearchLimit, CancellationToken ct = default) =>
            Handle(async () =>
            {
                if (limit < 1 || limit > PredictionLimits.MaxSearchLimit)
                    throw new PredictionValidationException("limit", $"limit must be between 1 and {PredictionLimits.MaxSearchLimit}.");
                var q = query?.Trim() ?? string.Empty;
                if (q.Length < 2 && !(q.Length > 0 && q.All(char.IsDigit)))
                    throw new PredictionValidationException("query", "query must be at least 2 characters (or an exact player id).");
                return Ok(await _search.SearchAsync(q, limit, ct));
            });

        [HttpGet("stats")]
        public ActionResult<List<PredictionStatInfo>> Stats() => _inputs.GetStats();

        [HttpGet("tournaments")]
        public Task<ActionResult> Tournaments(string scope = "upcoming", int? year = null, CancellationToken ct = default) =>
            Handle(async () =>
            {
                if (scope is not ("current" or "upcoming" or "historical" or "all"))
                    throw new PredictionValidationException("scope", "scope must be current, upcoming, historical or all.");
                var y = year ?? DateTime.UtcNow.Year;
                if (y < 2000 || y > DateTime.UtcNow.Year + 1)
                    throw new PredictionValidationException("year", "year is out of range.");
                return Ok(await _inputs.GetTournamentsAsync(scope, y, ct));
            });

        [HttpGet("golfers/{playerId}/events")]
        public Task<ActionResult> Events(string playerId, int? fromSeason = null, int? toSeason = null, string? eventType = null, CancellationToken ct = default) =>
            Handle(async () =>
            {
                if (string.IsNullOrWhiteSpace(playerId) || playerId.Length > 16 || !playerId.All(char.IsLetterOrDigit))
                    throw new PredictionValidationException("playerId", "Invalid player id.");
                return Ok(await _inputs.GetGolferEventsAsync(playerId, fromSeason, toSeason, eventType, ct));
            });

        [HttpPost("simulate")]
        public Task<ActionResult> Simulate([FromBody] SimulateRequest request, CancellationToken ct) =>
            Handle(async () =>
            {
                using var lease = _limiter.TryAcquire(ClientKey());
                if (lease is null)
                    return Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many simulations are running. Please try again shortly.");

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                try
                {
                    return Ok(await _prediction.SimulateAsync(request, linked.Token));
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    return Problem(statusCode: StatusCodes.Status504GatewayTimeout, title: "The simulation timed out.");
                }
            });

        [HttpPost("export")]
        public ActionResult Export([FromBody] SimulateResponse response, string format = "csv")
        {
            if (response.Golfers.Count == 0 || response.Golfers.Count > PredictionLimits.MaxGolfers)
                return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["golfers"] = new[] { "Nothing to export." } }));

            var (headers, rows) = PredictionService.ToExportRows(response);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            return format.ToLowerInvariant() switch
            {
                "csv" => File(ExportService.ToCsv(headers, rows), ExportService.CsvContentType, $"predictions-{stamp}.csv"),
                "xlsx" => File(ExportService.ToXlsx("Predictions", headers, rows), ExportService.XlsxContentType, $"predictions-{stamp}.xlsx"),
                _ => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["format"] = new[] { "format must be csv or xlsx." } })),
            };
        }

        private string ClientKey() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } uid ? $"user:{uid}"
                : $"ip:{HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

        private async Task<ActionResult> Handle(Func<Task<ActionResult>> action)
        {
            try
            {
                return await action();
            }
            catch (PredictionValidationException ex)
            {
                return ValidationProblem(new ValidationProblemDetails(ex.Errors));
            }
            catch (PredictionNotFoundException ex)
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: ex.Message);
            }
            catch (PredictionDataUnavailableException ex)
            {
                return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: ex.Message);
            }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
            {
                return StatusCode(499);
            }
            catch (Exception ex) when (ModelInputService.IsUpstreamFailure(ex))
            {
                _logger.LogWarning(ex, "Predictive modeling data unavailable");
                return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Golf data is temporarily unavailable.");
            }
        }
    }
}
