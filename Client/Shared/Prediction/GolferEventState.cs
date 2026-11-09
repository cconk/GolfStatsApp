using GolfStatsApp.Shared.Models;

namespace GolfStatsApp.Client.Shared.Prediction;

/// <summary>Client-side per-golfer event selection (FR-10b).</summary>
public class GolferEventState
{
    public string PlayerId { get; init; } = string.Empty;
    public string PlayerName { get; init; } = string.Empty;
    public List<GolferEvent>? Events { get; set; }
    public bool Loading { get; set; }
    public string? Error { get; set; }

    /// <summary>True once the user has edited this golfer's selection; otherwise generic sources are used.</summary>
    public bool Customized { get; set; }

    /// <summary>tournamentId → selected rounds (empty = all played rounds).</summary>
    public Dictionary<string, HashSet<int>> Selected { get; } = new();

    public void SelectLastN(int n)
    {
        Selected.Clear();
        foreach (var e in (Events ?? new()).Take(n)) Selected[e.TournamentId] = new HashSet<int>();
    }

    public List<EventSelection>? ToRequest() => !Customized ? null
        : Selected.Select(kv => new EventSelection
        {
            TournamentId = kv.Key,
            Rounds = kv.Value.Count == 0 ? null : kv.Value.OrderBy(r => r).ToList(),
        }).ToList();
}
