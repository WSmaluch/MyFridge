using System.Text.Json;
using Microsoft.JSInterop;

namespace Kitchen.Infrastructure;

public enum DataMode { Unset, Demo, GoogleSheets }
public sealed class KitchenSettings
{
    public string SpreadsheetId { get; set; } = string.Empty;
    public string GoogleClientId { get; set; } = string.Empty;
    public string GeminiApiKey { get; set; } = string.Empty;
    public string GeminiModel { get; set; } = "gemini-2.5-flash";
    public bool IsGoogleConfigured => !string.IsNullOrWhiteSpace(SpreadsheetId) && !string.IsNullOrWhiteSpace(GoogleClientId);
}
public interface ILocalSettingsService
{
    KitchenSettings Current { get; }
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(KitchenSettings settings, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
public interface IAppModeStore
{
    DataMode Mode { get; }
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SetModeAsync(DataMode mode, CancellationToken cancellationToken = default);
}
public sealed class LocalSettingsService(IJSRuntime js) : ILocalSettingsService
{
    private const string Key = "KitchenSettings";
    public KitchenSettings Current { get; private set; } = new();
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var json = await js.InvokeAsync<string?>("kitchenStorage.get", cancellationToken, Key);
        Current = string.IsNullOrWhiteSpace(json) ? new KitchenSettings() : JsonSerializer.Deserialize<KitchenSettings>(json) ?? new KitchenSettings();
    }
    public async Task SaveAsync(KitchenSettings settings, CancellationToken cancellationToken = default)
    {
        Current = settings; await js.InvokeVoidAsync("kitchenStorage.set", cancellationToken, Key, JsonSerializer.Serialize(settings));
    }
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Current = new KitchenSettings(); await js.InvokeVoidAsync("kitchenStorage.remove", cancellationToken, Key);
    }
}
public sealed class AppModeStore(IJSRuntime js) : IAppModeStore
{
    private const string Key = "KitchenDataMode";
    public DataMode Mode { get; private set; } = DataMode.Unset;
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var value = await js.InvokeAsync<string?>("kitchenStorage.get", cancellationToken, Key);
        Mode = Enum.TryParse<DataMode>(value, out var mode) ? mode : DataMode.Unset;
    }
    public async Task SetModeAsync(DataMode mode, CancellationToken cancellationToken = default)
    {
        Mode = mode; await js.InvokeVoidAsync("kitchenStorage.set", cancellationToken, Key, mode.ToString());
    }
}
