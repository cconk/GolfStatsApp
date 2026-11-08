using System.Net.Http.Json;
using GolfStatsApp.Shared.Models;

namespace GolfStatsApp.Client.Services;

public class AuthState
{
    private readonly HttpClient _http;
    private Task? _loading;

    public AuthState(HttpClient http) => _http = http;

    public UserInfo User { get; private set; } = new();
    public bool IsLoaded { get; private set; }
    public event Action? Changed;

    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            User = await _http.GetFromJsonAsync<UserInfo>("api/auth/me") ?? new UserInfo();
        }
        catch (HttpRequestException)
        {
            User = new UserInfo();
        }
        IsLoaded = true;
        Changed?.Invoke();
    }

    public Task<string?> LoginAsync(string email, string password) =>
        SubmitAsync("api/auth/login", new AuthRequest { Email = email, Password = password });

    public Task<string?> RegisterAsync(string email, string password, string displayName) =>
        SubmitAsync("api/auth/register", new AuthRequest { Email = email, Password = password, DisplayName = displayName });

    // Returns an error message, or null on success.
    private async Task<string?> SubmitAsync(string url, AuthRequest request)
    {
        var response = await _http.PostAsJsonAsync(url, request);
        if (!response.IsSuccessStatusCode)
            return await ApiClient.ReadErrorAsync(response);

        User = await response.Content.ReadFromJsonAsync<UserInfo>() ?? new UserInfo();
        IsLoaded = true;
        Changed?.Invoke();
        return null;
    }

    public async Task LogoutAsync()
    {
        await _http.PostAsync("api/auth/logout", null);
        User = new UserInfo();
        Changed?.Invoke();
    }
}
