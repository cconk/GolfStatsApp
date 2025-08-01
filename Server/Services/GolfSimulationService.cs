using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GolfStatsApp.Shared.Models;

namespace GolfStatsApp.Server.Services
{
    public class GolfSimulationService
    {
        private readonly Random _random = new();
        
        private readonly Dictionary<int, Dictionary<string, double>> _fedexTable = new()
        {
            { 1, new() { ["pga"] = 500, ["major"] = 750, ["signature"] = 700, ["additional"] = 300 } },
            { 2, new() { ["pga"] = 300, ["major"] = 500, ["signature"] = 400, ["additional"] = 165 } },
            { 3, new() { ["pga"] = 190, ["major"] = 350, ["signature"] = 350, ["additional"] = 105 } },
            { 4, new() { ["pga"] = 135, ["major"] = 325, ["signature"] = 325, ["additional"] = 80 } },
            { 5, new() { ["pga"] = 110, ["major"] = 300, ["signature"] = 300, ["additional"] = 65 } },
            { 6, new() { ["pga"] = 100, ["major"] = 270, ["signature"] = 275, ["additional"] = 60 } },
            { 7, new() { ["pga"] = 90, ["major"] = 250, ["signature"] = 225, ["additional"] = 55 } },
            { 8, new() { ["pga"] = 85, ["major"] = 225, ["signature"] = 200, ["additional"] = 50 } },
            { 9, new() { ["pga"] = 80, ["major"] = 200, ["signature"] = 175, ["additional"] = 45 } },
            { 10, new() { ["pga"] = 75, ["major"] = 175, ["signature"] = 150, ["additional"] = 40 } },
            { 11, new() { ["pga"] = 70, ["major"] = 155, ["signature"] = 130, ["additional"] = 37.5 } },
            { 12, new() { ["pga"] = 65, ["major"] = 135, ["signature"] = 120, ["additional"] = 35.0 } },
            { 13, new() { ["pga"] = 60, ["major"] = 115, ["signature"] = 110, ["additional"] = 32.5 } },
            { 14, new() { ["pga"] = 57, ["major"] = 105, ["signature"] = 100, ["additional"] = 31.0 } },
            { 15, new() { ["pga"] = 55, ["major"] = 95, ["signature"] = 90, ["additional"] = 30.5 } },
            { 16, new() { ["pga"] = 53, ["major"] = 85, ["signature"] = 80, ["additional"] = 30.0 } },
            { 17, new() { ["pga"] = 51, ["major"] = 75, ["signature"] = 70, ["additional"] = 29.5 } },
            { 18, new() { ["pga"] = 49, ["major"] = 70, ["signature"] = 65, ["additional"] = 29.0 } },
            { 19, new() { ["pga"] = 47, ["major"] = 65, ["signature"] = 60, ["additional"] = 28.5 } },
            { 20, new() { ["pga"] = 45, ["major"] = 60, ["signature"] = 55, ["additional"] = 28.0 } },
            { 21, new() { ["pga"] = 43, ["major"] = 55, ["signature"] = 50, ["additional"] = 26.76 } },
            { 22, new() { ["pga"] = 41, ["major"] = 53, ["signature"] = 48, ["additional"] = 25.51 } },
            { 23, new() { ["pga"] = 39, ["major"] = 51, ["signature"] = 46, ["additional"] = 24.27 } },
            { 24, new() { ["pga"] = 37, ["major"] = 49, ["signature"] = 44, ["additional"] = 23.02 } },
            { 25, new() { ["pga"] = 35.5, ["major"] = 47, ["signature"] = 42, ["additional"] = 22.09 } },
            { 26, new() { ["pga"] = 34, ["major"] = 45, ["signature"] = 40, ["additional"] = 21.16 } },
            { 27, new() { ["pga"] = 32.5, ["major"] = 43, ["signature"] = 38, ["additional"] = 20.22 } },
            { 28, new() { ["pga"] = 31, ["major"] = 41, ["signature"] = 36, ["additional"] = 19.29 } },
            { 29, new() { ["pga"] = 29.5, ["major"] = 39, ["signature"] = 34, ["additional"] = 18.36 } },
            { 30, new() { ["pga"] = 28, ["major"] = 37, ["signature"] = 32.5, ["additional"] = 17.42 } },
            { 31, new() { ["pga"] = 26.5, ["major"] = 35, ["signature"] = 31, ["additional"] = 16.49 } },
            { 32, new() { ["pga"] = 25, ["major"] = 33, ["signature"] = 29.5, ["additional"] = 15.56 } },
            { 33, new() { ["pga"] = 23.5, ["major"] = 31, ["signature"] = 28, ["additional"] = 14.62 } },
            { 34, new() { ["pga"] = 22, ["major"] = 29, ["signature"] = 26.5, ["additional"] = 13.69 } },
            { 35, new() { ["pga"] = 21, ["major"] = 27, ["signature"] = 25, ["additional"] = 13.07 } },
            { 36, new() { ["pga"] = 20, ["major"] = 26, ["signature"] = 24, ["additional"] = 12.44 } },
            { 37, new() { ["pga"] = 19, ["major"] = 25, ["signature"] = 23, ["additional"] = 11.82 } },
            { 38, new() { ["pga"] = 18, ["major"] = 24, ["signature"] = 22, ["additional"] = 11.20 } },
            { 39, new() { ["pga"] = 17, ["major"] = 23, ["signature"] = 21, ["additional"] = 10.58 } },
            { 40, new() { ["pga"] = 16, ["major"] = 22, ["signature"] = 20.25, ["additional"] = 9.96 } },
            { 41, new() { ["pga"] = 15, ["major"] = 21, ["signature"] = 19.5, ["additional"] = 9.33 } },
            { 42, new() { ["pga"] = 14, ["major"] = 20.25, ["signature"] = 18.75, ["additional"] = 8.71 } },
            { 43, new() { ["pga"] = 13, ["major"] = 19.5, ["signature"] = 18, ["additional"] = 8.09 } },
            { 44, new() { ["pga"] = 12, ["major"] = 18.75, ["signature"] = 17.25, ["additional"] = 7.47 } },
            { 45, new() { ["pga"] = 11, ["major"] = 18, ["signature"] = 16.5, ["additional"] = 6.84 } },
            { 46, new() { ["pga"] = 10.5, ["major"] = 17.25, ["signature"] = 15.75, ["additional"] = 6.53 } },
            { 47, new() { ["pga"] = 10, ["major"] = 16.5, ["signature"] = 15, ["additional"] = 6.22 } },
            { 48, new() { ["pga"] = 9.5, ["major"] = 15.75, ["signature"] = 14.25, ["additional"] = 5.91 } },
            { 49, new() { ["pga"] = 9, ["major"] = 15, ["signature"] = 13.5, ["additional"] = 5.60 } },
            { 50, new() { ["pga"] = 8.5, ["major"] = 14.25, ["signature"] = 13, ["additional"] = 5.29 } },
            { 51, new() { ["pga"] = 8, ["major"] = 13.5, ["signature"] = 12.5, ["additional"] = 4.98 } },
            { 52, new() { ["pga"] = 7.5, ["major"] = 13, ["signature"] = 12, ["additional"] = 4.67 } },
            { 53, new() { ["pga"] = 7, ["major"] = 12.5, ["signature"] = 11.5, ["additional"] = 4.36 } },
            { 54, new() { ["pga"] = 6.5, ["major"] = 12, ["signature"] = 11, ["additional"] = 4.04 } },
            { 55, new() { ["pga"] = 6, ["major"] = 11.5, ["signature"] = 10.5, ["additional"] = 3.73 } },
            { 56, new() { ["pga"] = 5.8, ["major"] = 11, ["signature"] = 10, ["additional"] = 3.61 } },
            { 57, new() { ["pga"] = 5.6, ["major"] = 10.5, ["signature"] = 9.5, ["additional"] = 3.48 } },
            { 58, new() { ["pga"] = 5.4, ["major"] = 10, ["signature"] = 9, ["additional"] = 3.36 } },
            { 59, new() { ["pga"] = 5.2, ["major"] = 9.5, ["signature"] = 8.5, ["additional"] = 3.24 } },
            { 60, new() { ["pga"] = 5.0, ["major"] = 9, ["signature"] = 8.25, ["additional"] = 3.11 } },
            { 61, new() { ["pga"] = 4.8, ["major"] = 8.5, ["signature"] = 8, ["additional"] = 2.99 } },
            { 62, new() { ["pga"] = 4.6, ["major"] = 8.25, ["signature"] = 7.75, ["additional"] = 2.86 } },
            { 63, new() { ["pga"] = 4.4, ["major"] = 8, ["signature"] = 7.5, ["additional"] = 2.74 } },
            { 64, new() { ["pga"] = 4.2, ["major"] = 7.75, ["signature"] = 7.25, ["additional"] = 2.61 } },
            { 65, new() { ["pga"] = 4.0, ["major"] = 7.5, ["signature"] = 7, ["additional"] = 2.49 } },
            { 66, new() { ["pga"] = 3.8, ["major"] = 7.25, ["signature"] = 6.75, ["additional"] = 2.36 } },
            { 67, new() { ["pga"] = 3.6, ["major"] = 7, ["signature"] = 6.5, ["additional"] = 2.24 } },
            { 68, new() { ["pga"] = 3.4, ["major"] = 6.75, ["signature"] = 6.25, ["additional"] = 2.12 } },
            { 69, new() { ["pga"] = 3.2, ["major"] = 6.5, ["signature"] = 6, ["additional"] = 1.99 } },
            { 70, new() { ["pga"] = 3.0, ["major"] = 6.25, ["signature"] = 5.75, ["additional"] = 1.87 } },
            { 71, new() { ["pga"] = 2.9, ["major"] = 6, ["signature"] = 5.5, ["additional"] = 1.80 } },
            { 72, new() { ["pga"] = 2.8, ["major"] = 5.75, ["signature"] = 5.25, ["additional"] = 1.74 } },
            { 73, new() { ["pga"] = 2.7, ["major"] = 5.5, ["signature"] = 5, ["additional"] = 1.68 } },
            { 74, new() { ["pga"] = 2.6, ["major"] = 5.25, ["signature"] = 4.75, ["additional"] = 1.62 } },
            { 75, new() { ["pga"] = 2.5, ["major"] = 5, ["signature"] = 4.5, ["additional"] = 1.56 } },
            { 76, new() { ["pga"] = 2.4, ["major"] = 4.75, ["signature"] = 4.25, ["additional"] = 1.49 } },
            { 77, new() { ["pga"] = 2.3, ["major"] = 4.5, ["signature"] = 4, ["additional"] = 1.43 } },
            { 78, new() { ["pga"] = 2.2, ["major"] = 4.25, ["signature"] = 3.75, ["additional"] = 1.37 } },
            { 79, new() { ["pga"] = 2.1, ["major"] = 4, ["signature"] = 3.5, ["additional"] = 1.31 } },
            { 80, new() { ["pga"] = 2.0, ["major"] = 3.75, ["signature"] = 3.25, ["additional"] = 1.24 } },
            { 81, new() { ["pga"] = 1.9, ["major"] = 3.5, ["signature"] = 3, ["additional"] = 1.18 } },
            { 82, new() { ["pga"] = 1.8, ["major"] = 3.25, ["signature"] = 2.75, ["additional"] = 1.12 } },
            { 83, new() { ["pga"] = 1.7, ["major"] = 3, ["signature"] = 2.5, ["additional"] = 1.06 } },
            { 84, new() { ["pga"] = 1.6, ["major"] = 2.75, ["signature"] = 2.25, ["additional"] = 1.00 } },
            { 85, new() { ["pga"] = 1.5, ["major"] = 2.5, ["signature"] = 2, ["additional"] = 0.93 } }
        };

        public async Task<SimulationResult> RunSimulation(SimulationRequest request, int simulations = 10000)
        {
            return await Task.Run(() =>
            {
                var teamScores = new List<double>();
                var playerScores = new Dictionary<string, List<double>>();
                var sampleRun = new Dictionary<string, PlayerResult>();
                var sampleRunFound = false;

                // Initialize player score tracking
                foreach (var player in request.Players)
                {
                    playerScores[player.Name] = new List<double>();
                }
                foreach (var benchPlayer in request.BenchPlayers)
                {
                    playerScores[benchPlayer.Name] = new List<double>();
                }

                for (int sim = 0; sim < simulations; sim++)
                {
                    var (teamScore, playerResults) = SimulateTournament(request);
                    teamScores.Add(teamScore);

                    // Track individual player scores for percentile calculations
                    foreach (var result in playerResults)
                    {
                        if (playerScores.ContainsKey(result.Name))
                        {
                            playerScores[result.Name].Add(result.TotalPoints);
                        }
                    }

                    // Keep a run that shows bench substitution for detailed breakdown
                    // Prefer a run where bench players were actually used
                    if (!sampleRunFound || (!sampleRunFound && HasBenchPlayerResults(playerResults, request)))
                    {
                        sampleRun.Clear();
                        foreach (var result in playerResults)
                        {
                            sampleRun[result.Name] = result;
                        }
                        
                        // If this run has bench players, we found a good sample
                        if (HasBenchPlayerResults(playerResults, request))
                        {
                            sampleRunFound = true;
                        }
                    }
                }

                teamScores.Sort();

                // Calculate individual player percentiles
                var playerPercentiles = new List<PlayerPercentiles>();
                foreach (var kvp in playerScores)
                {
                    if (kvp.Value.Count > 0)
                    {
                        var sortedScores = kvp.Value.OrderBy(x => x).ToList();
                        var isCaptain = kvp.Key == request.Captain;
                        
                        playerPercentiles.Add(new PlayerPercentiles
                        {
                            Name = kvp.Key,
                            Percentile10th = sortedScores[(int)(sortedScores.Count * 0.1)],
                            Percentile50th = sortedScores[sortedScores.Count / 2],
                            Percentile90th = sortedScores[(int)(sortedScores.Count * 0.9)],
                            IsCaptain = isCaptain
                        });
                    }
                }
                
                return new SimulationResult
                {
                    Percentile10th = teamScores[(int)(simulations * 0.1)],
                    Percentile50th = teamScores[simulations / 2],
                    Percentile90th = teamScores[(int)(simulations * 0.9)],
                    TeamTotal = sampleRun.Values.Sum(p => p.TotalPoints),
                    PlayerResults = sampleRun.Values.ToList(),
                    PlayerPercentiles = playerPercentiles
                };
            });
        }

        private bool HasBenchPlayerResults(List<PlayerResult> playerResults, SimulationRequest request)
        {
            // Check if any bench players appear in the results
            var benchPlayerNames = request.BenchPlayers.Select(b => b.Name).ToList();
            return playerResults.Any(pr => benchPlayerNames.Contains(pr.Name));
        }

        private (double teamScore, List<PlayerResult> playerResults) SimulateTournament(SimulationRequest request)
        {
            var playerResults = new List<PlayerResult>();
            var madeCut = new Dictionary<string, bool>();
            var holePoints = new Dictionary<string, double>();
            var fedexPoints = new Dictionary<string, double>();

            // Rounds 1 & 2: All starters AND bench players play
            foreach (var player in request.Players)
            {
                double totalHolePoints = 0;

                // Simulate 2 rounds
                for (int round = 1; round <= 2; round++)
                {
                    var (birdies, bogeys) = SimulateRound(player.Birdies, player.Bogeys);
                    var pars = Math.Max(0, 18 - birdies - bogeys);
                    var roundPoints = 2 * birdies + pars - bogeys;
                    
                    // Bogey-free bonus
                    if (bogeys == 0)
                        roundPoints += 3;
                        
                    totalHolePoints += roundPoints;
                }

                holePoints[player.Name] = totalHolePoints;
                madeCut[player.Name] = _random.NextDouble() < player.CutProbability;
            }

            // Bench players also play rounds 1 & 2
            foreach (var benchPlayer in request.BenchPlayers)
            {
                double totalHolePoints = 0;

                // Simulate 2 rounds
                for (int round = 1; round <= 2; round++)
                {
                    var (birdies, bogeys) = SimulateRound(benchPlayer.Birdies, benchPlayer.Bogeys);
                    var pars = Math.Max(0, 18 - birdies - bogeys);
                    var roundPoints = 2 * birdies + pars - bogeys;
                    
                    // Bogey-free bonus
                    if (bogeys == 0)
                        roundPoints += 3;
                        
                    totalHolePoints += roundPoints;
                }

                holePoints[benchPlayer.Name] = totalHolePoints;
                madeCut[benchPlayer.Name] = _random.NextDouble() < benchPlayer.CutProbability;
            }

            // Determine R3/R4 lineup: start with surviving starters
            var survivors = request.Players.Where(p => madeCut[p.Name]).ToList();
            var slots = request.Players.Count - survivors.Count;
            var finalLineup = survivors.ToList();

            // Auto-swap bench players if needed - select the best performing bench players who made the cut
            if (slots > 0)
            {
                var availableBench = request.BenchPlayers
                    .Where(b => madeCut[b.Name]) // Only bench players who made the cut
                    .OrderByDescending(b => holePoints[b.Name]) // Order by best performance in R1/R2
                    .Take(slots); // Take the top performers

                foreach (var bench in availableBench)
                {
                    finalLineup.Add(new GolfPlayer 
                    { 
                        Name = bench.Name, 
                        Birdies = bench.Birdies, 
                        Bogeys = bench.Bogeys,
                        CutProbability = bench.CutProbability 
                    });
                    // Bench players already have their R1/R2 points, don't reset to 0
                }
            }

            // Rounds 3 & 4: Final lineup plays
            foreach (var player in finalLineup)
            {
                double additionalPoints = 0;

                for (int round = 3; round <= 4; round++)
                {
                    var (birdies, bogeys) = SimulateRound(player.Birdies, player.Bogeys);
                    var pars = Math.Max(0, 18 - birdies - bogeys);
                    var roundPoints = 2 * birdies + pars - bogeys;
                    
                    if (bogeys == 0)
                        roundPoints += 3;
                        
                    additionalPoints += roundPoints;
                }

                if (!holePoints.ContainsKey(player.Name))
                    holePoints[player.Name] = 0;
                    
                holePoints[player.Name] += additionalPoints;
            }

            // Add round bonuses (proxy for low round bonuses) - only for players who made the final lineup
            foreach (var player in finalLineup)
            {
                holePoints[player.Name] += 15; // 10 + 5 for low round bonuses
            }

            // FedEx Cup bonuses - only for players in final lineup
            foreach (var player in finalLineup)
            {
                var position = _random.Next(1, 86);
                var fedexAward = _fedexTable.ContainsKey(position) ? 
                    _fedexTable[position][request.EventType] * 0.1 : 0;
                fedexPoints[player.Name] = fedexAward;
            }

            // Build player results and apply captain doubling
            double teamTotal = 0;

            // Include only players who made the final lineup (this includes substituted bench players)
            foreach (var player in finalLineup)
            {
                var baseHolePoints = holePoints.GetValueOrDefault(player.Name, 0);
                var baseFedexPoints = fedexPoints.GetValueOrDefault(player.Name, 0);
                var isCaptain = player.Name == request.Captain;
                
                var multiplier = isCaptain ? 2.0 : 1.0;
                var finalHolePoints = baseHolePoints * multiplier;
                var finalFedexPoints = baseFedexPoints * multiplier;
                var totalPoints = finalHolePoints + finalFedexPoints;

                playerResults.Add(new PlayerResult
                {
                    Name = player.Name,
                    HolePoints = finalHolePoints,
                    FedExPoints = finalFedexPoints,
                    TotalPoints = totalPoints,
                    IsCaptain = isCaptain
                });

                teamTotal += totalPoints;
            }

            return (teamTotal, playerResults);
        }

        private (int birdies, int bogeys) SimulateRound(double averageBirdies, double averageBogeys)
        {
            // Use Poisson distribution approximation
            var birdies = SamplePoisson(averageBirdies);
            var bogeys = SamplePoisson(averageBogeys);
            
            return (birdies, bogeys);
        }

        private int SamplePoisson(double lambda)
        {
            // Simple Poisson approximation using normal distribution for larger lambda
            if (lambda > 10)
            {
                var normal = SampleNormal(lambda, Math.Sqrt(lambda));
                return Math.Max(0, (int)Math.Round(normal));
            }

            // For smaller lambda, use the inverse transform method
            double L = Math.Exp(-lambda);
            double p = 1.0;
            int k = 0;

            do
            {
                k++;
                p *= _random.NextDouble();
            } while (p > L);

            return k - 1;
        }

        private double SampleNormal(double mean, double stdDev)
        {
            // Box-Muller transform
            double u1 = 1.0 - _random.NextDouble();
            double u2 = 1.0 - _random.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mean + stdDev * randStdNormal;
        }
    }
}
