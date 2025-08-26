namespace GolfStatsApp.Shared
{
    public class TeamStats
    {
        public int Id { get; set; }

        public string TeamName { get; set; } = string.Empty;
        public double OverallAverage { get; set; }
        public double AverageWithRoster { get; set; }
        public int BestTournamentScore { get; set; }
        public string BestTournament { get; set; } = string.Empty;
        public int WorstTournamentScore { get; set; }
        public string WorstTournament { get; set; } = string.Empty;
        public int LongestRosterStreak { get; set; }
        public double Wins { get; set; }
        public double Majors { get; set; }
        public double SignatureEvents { get; set; }
        public double Regulars { get; set; }
    }
}
