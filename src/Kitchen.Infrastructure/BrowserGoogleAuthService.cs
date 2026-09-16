using Microsoft.JSInterop;

namespace Kitchen.Infrastructure;

public enum GoogleAuthState { NotConfigured, Disconnected, Connecting, Connected, Error }
public interface IGoogleAuthService
{
    GoogleAuthState State { get; }
    bool IsConnected { get; }
    string? ErrorMessage { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
public sealed class GoogleAuthService(IJSRuntime js, ILocalSettingsService settings) : IGoogleAuthService
{
    private string? _token;
    public GoogleAuthState State { get; private set; } = GoogleAuthState.NotConfigured;
    public bool IsConnected => State == GoogleAuthState.Connected && !string.IsNullOrWhiteSpace(_token);
    public string? ErrorMessage { get; private set; }
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.Current.GoogleClientId)) { State = GoogleAuthState.NotConfigured; ErrorMessage = "Uzupełnij Google OAuth Client ID."; return; }
        try { State = GoogleAuthState.Connecting; ErrorMessage = null; _token = await js.InvokeAsync<string>("googleKitchenAuth.connect", cancellationToken, settings.Current.GoogleClientId); State = string.IsNullOrWhiteSpace(_token) ? GoogleAuthState.Error : GoogleAuthState.Connected; }
        catch (JSException) { _token = null; State = GoogleAuthState.Error; ErrorMessage = "Nie udało się połączyć z Google. Sprawdź Client ID i dozwolony origin."; }
    }
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_token)) await js.InvokeVoidAsync("googleKitchenAuth.revoke", cancellationToken, _token);
        _token = null; State = settings.Current.IsGoogleConfigured ? GoogleAuthState.Disconnected : GoogleAuthState.NotConfigured;
    }
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected) await ConnectAsync(cancellationToken);
        return _token;
    }
}
