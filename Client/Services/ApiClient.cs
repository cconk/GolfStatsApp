using System.Net;

namespace GolfStatsApp.Client.Services;

public static class ApiClient
{
    public static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        var body = (await response.Content.ReadAsStringAsync()).Trim().Trim('"');
        if (!string.IsNullOrWhiteSpace(body) && !body.StartsWith('{') && !body.StartsWith('<'))
            return body;

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Please sign in to continue.",
            HttpStatusCode.Forbidden => "You don't have permission to do that.",
            HttpStatusCode.NotFound => "Not found.",
            _ => $"Request failed ({(int)response.StatusCode})."
        };
    }
}
