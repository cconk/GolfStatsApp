using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GolfStatsApp.Shared.Models;

namespace GolfStatsApp.Client.Services;

/// <summary>Typed client for api/predictive-modeling.</summary>
public class PredictionApi
{
    private readonly HttpClient _http;

    public PredictionApi(HttpClient http) => _http = http;

    public async Task<List<GolferSearchResult>> SearchGolfersAsync(string query, int limit = 25, CancellationToken ct = default) =>
        await GetAsync<List<GolferSearchResult>>($"api/predictive-modeling/golfers?query={Uri.EscapeDataString(query)}&limit={limit}", ct) ?? new();

    public async Task<List<PredictionStatInfo>> GetStatsAsync(CancellationToken ct = default) =>
        await GetAsync<List<PredictionStatInfo>>("api/predictive-modeling/stats", ct) ?? new();

    public async Task<List<PredictionTournament>> GetTournamentsAsync(string scope, int year, CancellationToken ct = default) =>
        await GetAsync<List<PredictionTournament>>($"api/predictive-modeling/tournaments?scope={scope}&year={year}", ct) ?? new();

    public async Task<List<GolferEvent>> GetGolferEventsAsync(string playerId, int fromSeason, int toSeason, CancellationToken ct = default) =>
        await GetAsync<List<GolferEvent>>($"api/predictive-modeling/golfers/{Uri.EscapeDataString(playerId)}/events?fromSeason={fromSeason}&toSeason={toSeason}", ct) ?? new();

    public async Task<List<FieldPlayerLite>> GetFieldAsync(string tournamentId, CancellationToken ct = default) =>
        await GetAsync<List<FieldPlayerLite>>($"api/pgatour/tournaments/{Uri.EscapeDataString(tournamentId)}/field", ct) ?? new();

    public async Task<SimulateResponse> SimulateAsync(SimulateRequest request, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("api/predictive-modeling/simulate", request, ct);
        if (!response.IsSuccessStatusCode) throw new PredictionApiException(await ReadProblemAsync(response));
        return (await response.Content.ReadFromJsonAsync<SimulateResponse>(cancellationToken: ct))!;
    }

    public async Task<(byte[] Content, string ContentType, string FileName)> ExportAsync(SimulateResponse result, string format, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync($"api/predictive-modeling/export?format={format}", result, ct);
        if (!response.IsSuccessStatusCode) throw new PredictionApiException(await ReadProblemAsync(response));
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var type = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? $"predictions.{format}";
        return (bytes, type, name);
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) throw new PredictionApiException(await ReadProblemAsync(response));
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    private static async Task<string> ReadProblemAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            if (body.TrimStart().StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    var messages = errors.EnumerateObject().SelectMany(p => p.Value.EnumerateArray().Select(v => v.GetString())).Where(m => m != null);
                    var joined = string.Join(" ", messages);
                    if (joined.Length > 0) return joined;
                }
                if (root.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 } t) return t;
            }
        }
        catch (JsonException) { }

        return response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => "Too many simulations are running. Please try again shortly.",
            HttpStatusCode.ServiceUnavailable => "Golf data is temporarily unavailable.",
            HttpStatusCode.GatewayTimeout => "The simulation timed out.",
            _ => await ApiClient.ReadErrorAsync(response),
        };
    }
}

public class PredictionApiException : Exception
{
    public PredictionApiException(string message) : base(message) { }
}

public class FieldPlayerLite
{
    public string? PlayerId { get; set; }
    public string? DisplayName { get; set; }
    public bool Alternate { get; set; }
    public bool? Withdrawn { get; set; }
}
