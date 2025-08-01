using System.Collections.Generic;

namespace GolfStatsApp.Shared.Models
{
    public class GolfPlayer
    {
        public string Name { get; set; } = string.Empty;
        public double Birdies { get; set; }
        public double Bogeys { get; set; }
        public double CutProbability { get; set; }
        
        // UI helper property for slider (0-100)
        public int CutProbabilityPercent 
        { 
            get => (int)(CutProbability * 100);
            set => CutProbability = value / 100.0;
        }
    }

    public class BenchPlayer : GolfPlayer
    {
        // No longer using Priority - bench players are automatically selected based on R1/R2 performance
    }

    public class SimulationRequest
    {
        public string EventType { get; set; } = "major";
        public string Captain { get; set; } = string.Empty;
        public List<GolfPlayer> Players { get; set; } = new();
        public List<BenchPlayer> BenchPlayers { get; set; } = new();
    }

    public class PlayerResult
    {
        public string Name { get; set; } = string.Empty;
        public double HolePoints { get; set; }
        public double FedExPoints { get; set; }
        public double TotalPoints { get; set; }
        public bool IsCaptain { get; set; }
    }

    public class PlayerPercentiles
    {
        public string Name { get; set; } = string.Empty;
        public double Percentile10th { get; set; }
        public double Percentile50th { get; set; }
        public double Percentile90th { get; set; }
        public bool IsCaptain { get; set; }
    }

    public class SimulationResult
    {
        public double Percentile10th { get; set; }
        public double Percentile50th { get; set; }
        public double Percentile90th { get; set; }
        public List<PlayerResult> PlayerResults { get; set; } = new();
        public List<PlayerPercentiles> PlayerPercentiles { get; set; } = new();
        public double TeamTotal { get; set; }
    }
}
