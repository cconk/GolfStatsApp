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
        public int Wins { get; set; }
        public int Majors { get; set; }
        public int SignatureEvents { get; set; }
        public int Regulars { get; set; }
    }
}
