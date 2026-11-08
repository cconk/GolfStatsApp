using Microsoft.JSInterop;

namespace GolfStatsApp.Client.Services;

public class ThemeService
{
    private readonly IJSRuntime _js;

    public ThemeService(IJSRuntime js) => _js = js;

    public string Theme { get; private set; } = "light";
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        Theme = await _js.InvokeAsync<string>("golfTheme.get");
        Changed?.Invoke();
    }

    public async Task ToggleAsync()
    {
        Theme = Theme == "dark" ? "light" : "dark";
        await _js.InvokeVoidAsync("golfTheme.set", Theme);
        Changed?.Invoke();
    }
}
