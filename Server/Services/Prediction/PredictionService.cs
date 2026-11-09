using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GolfStatsApp.Shared.Models;
using Microsoft.Extensions.Logging;

namespace GolfStatsApp.Server.Services.Prediction
{
    /// <summary>Runs a prediction: assemble inputs, run the engine, map results.</summary>
    public class PredictionService
    {
        private readonly ModelInputService _inputs;
        private readonly MonteCarloEngine _engine;
        private readonly ILogger<PredictionService> _logger;

        public PredictionService(ModelInputService inputs, MonteCarloEngine engine, ILogger<PredictionService> logger)
        {
            _inputs = inputs;
            _engine = engine;
            _logger = logger;
        }

        public async Task<SimulateResponse> SimulateAsync(SimulateRequest request, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var build = await _inputs.BuildAsync(request, ct);
            var seed = request.Seed ?? Random.Shared.Next();
            build.Engine.Seed = seed;

            _logger.LogInformation("Prediction run start: {Golfers} golfers, {Sims} sims, tournament {TournamentId}",
                build.Golfers.Count, build.Engine.Simulations, request.TournamentId);

            var results = await Task.Run(() => _engine.Run(build.Golfers, build.Engine, ct), ct);
            sw.Stop();

            _logger.LogInformation("Prediction run end: {Golfers} golfers, {Sims} sims, {Elapsed} ms",
                build.Golfers.Count, build.Engine.Simulations, sw.ElapsedMilliseconds);

            var response = new SimulateResponse
            {
                SimulationsRun = build.Engine.Simulations,
                Seed = seed,
                ElapsedMs = sw.ElapsedMilliseconds,
                GeneratedUtc = DateTime.UtcNow,
                GolfersModeled = build.Golfers.Count,
                InputsSummary = build.Summary,
            };
            for (var i = 0; i < build.Golfers.Count; i++)
            {
                var g = build.Golfers[i];
                var r = results[i];
                var buckets = r.FinishBuckets.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 4));
                if (!build.Summary.HasCut) buckets.Remove(FinishBucketNames.MissedCut);
                response.Golfers.Add(new GolferPrediction
                {
                    PlayerId = g.PlayerId,
                    PlayerName = g.PlayerName,
                    MeanScoreToPar = Math.Round(r.MeanScoreToPar, 2),
                    P10 = Math.Round(r.P10, 1),
                    P50 = Math.Round(r.P50, 1),
                    P90 = Math.Round(r.P90, 1),
                    MeanFinish = Math.Round(r.MeanFinish, 2),
                    FinishBuckets = buckets,
                    PMakeCut = build.Summary.HasCut ? Math.Round(r.PMakeCut, 4) : 1.0,
                    PWin = Math.Round(r.PWin, 4),
                    PTop5 = Math.Round(r.PTop5, 4),
                    PTop10 = Math.Round(r.PTop10, 4),
                    PTop20 = Math.Round(r.PTop20, 4),
                    SkillEstimate = Math.Round(g.Skill, 3),
                    Sigma = Math.Round(g.Sigma, 3),
                    Warnings = g.Warnings,
                });
            }
            return response;
        }

        public static (string[] Headers, List<IReadOnlyList<object?>> Rows) ToExportRows(SimulateResponse response)
        {
            var headers = new[]
            {
                "Player ID", "Player", "Mean To Par (estimate)", "P10", "P50", "P90", "Mean Finish",
                "P(Make Cut)", "P(Win)", "P(Top 5)", "P(Top 10)", "P(Top 20)",
                "Finish 1", "Finish 2-5", "Finish 6-10", "Finish 11-20", "Finish 21-40", "Finish 41+", "MC", "Warnings",
            };
            var rows = new List<IReadOnlyList<object?>>();
            foreach (var g in response.Golfers)
            {
                double B(string k) => g.FinishBuckets.TryGetValue(k, out var v) ? v : 0;
                rows.Add(new object?[]
                {
                    g.PlayerId, g.PlayerName, g.MeanScoreToPar, g.P10, g.P50, g.P90, g.MeanFinish,
                    g.PMakeCut, g.PWin, g.PTop5, g.PTop10, g.PTop20,
                    B(FinishBucketNames.Win), B(FinishBucketNames.Top5), B(FinishBucketNames.Top10), B(FinishBucketNames.Top20),
                    B(FinishBucketNames.Top40), B(FinishBucketNames.Rest), B(FinishBucketNames.MissedCut),
                    string.Join("; ", g.Warnings),
                });
            }

            var s = response.InputsSummary;
            void Meta(string label, object? value)
            {
                var row = new object?[headers.Length];
                row[0] = label;
                row[1] = value;
                rows.Add(row);
            }
            rows.Add(new object?[headers.Length]);
            Meta("Inputs summary", "All values are model estimates.");
            Meta("Tournament", $"{s.TournamentName} ({s.TournamentId})");
            Meta("Simulations", response.SimulationsRun);
            Meta("Seed", response.Seed);
            Meta("Generated (UTC)", response.GeneratedUtc.ToString("u"));
            Meta("Data as of (UTC)", s.DataAsOfUtc?.ToString("u"));
            Meta("Cut", s.HasCut ? $"Top {s.CutTopN} and ties" : "No cut");
            foreach (var st in s.Stats) Meta($"Stat {st.StatId}", $"{st.Name} {st.WeightPercent}% ({st.Source})");
            Meta("Stats / performance weight", $"{s.Performance.StatsWeightPercent}% / {s.Performance.PerformanceWeightPercent}%");
            Meta("Current / recent / historical", $"{s.Performance.CurrentWeightPercent}% / {s.Performance.RecentWeightPercent}% / {s.Performance.HistoricalWeightPercent}%");
            Meta("Events from DB / API", $"{s.Performance.EventsFromDb} / {s.Performance.EventsFromApi}");
            foreach (var w in s.Warnings) Meta("Warning", w);
            return (headers, rows);
        }
    }
}
