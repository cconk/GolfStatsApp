using System.Collections.Generic;

namespace GolfStatsApp.Shared.Models
{
    public class GolfPlayerData
    {
        public string PlayerName { get; set; } = string.Empty;
        public double Birdies { get; set; }
        public double Bogeys { get; set; }
        public int Rank { get; set; }
    }
}
