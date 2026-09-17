using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kitchen.Infrastructure;

public sealed class GoogleApiException(HttpStatusCode statusCode, string message) : Exception(message) { public HttpStatusCode StatusCode { get; } = statusCode; }
public sealed class GoogleSheetsRestClient(HttpClient http, IGoogleAuthService auth, ILocalSettingsService settings)
{
    public string SpreadsheetId => settings.Current.SpreadsheetId;
    public async Task<JsonDocument> GetAsync(string relativeUrl, CancellationToken ct = default) => await SendAsync(HttpMethod.Get, relativeUrl, null, ct);
    public Task<JsonDocument> UpdateValuesAsync(string range, IEnumerable<string[]> values, CancellationToken ct = default)
    {
        var url = $"v4/spreadsheets/{Uri.EscapeDataString(SpreadsheetId)}/values/{Uri.EscapeDataString(range)}?valueInputOption=RAW";
        return SendAsync(HttpMethod.Put, url, ValueRange(range, values), ct);
    }
    public Task<JsonDocument> AppendValuesAsync(string range, IEnumerable<string[]> values, CancellationToken ct = default)
    {
        var url = $"v4/spreadsheets/{Uri.EscapeDataString(SpreadsheetId)}/values/{Uri.EscapeDataString(range)}:append?valueInputOption=RAW&insertDataOption=INSERT_ROWS";
        return SendAsync(HttpMethod.Post, url, ValueRange(range, values), ct);
    }
    public Task<JsonDocument> BatchUpdateAsync(object body, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"v4/spreadsheets/{Uri.EscapeDataString(SpreadsheetId)}:batchUpdate", body, ct);
    public Task<JsonDocument> ClearValuesAsync(string range, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"v4/spreadsheets/{Uri.EscapeDataString(SpreadsheetId)}/values/{Uri.EscapeDataString(range)}:clear", new { }, ct);
    public async Task EnsureAccessAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(SpreadsheetId)) throw new InvalidOperationException("Uzupełnij Spreadsheet ID w ustawieniach.");
        using var _ = await GetAsync($"v4/spreadsheets/{Uri.EscapeDataString(SpreadsheetId)}?fields=properties.title", ct);
    }
    private async Task<JsonDocument> SendAsync(HttpMethod method, string relativeUrl, object? body, CancellationToken ct)
    {
        if (!NavigatorOnline()) throw new InvalidOperationException("Brak połączenia z internetem.");
        var token = await auth.GetAccessTokenAsync(ct);
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Sesja Google wygasła. Połącz konto ponownie.");
        using var request = new HttpRequestMessage(method, new Uri(new Uri("https://sheets.googleapis.com/"), relativeUrl));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, ct); }
        catch (HttpRequestException ex) { throw new InvalidOperationException("Nie można połączyć się z Google Sheets. Sprawdź internet, dozwolony origin OAuth i CORS.", ex); }
        using (response)
        {
        var content = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new GoogleApiException(response.StatusCode, FriendlyError(response.StatusCode));
        return JsonDocument.Parse(content);
        }
    }
    private static object ValueRange(string range, IEnumerable<string[]> values) => new { range, majorDimension = "ROWS", values = values.ToArray() };
    private static string FriendlyError(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Sesja Google wygasła. Połącz konto ponownie.",
        HttpStatusCode.Forbidden => "Nie masz dostępu do tego arkusza albo Google Sheets API nie jest włączone.",
        HttpStatusCode.NotFound => "Spreadsheet ID jest nieprawidłowe.",
        _ => "Nie udało się połączyć z Google Sheets. Spróbuj ponownie."
    };
    private static bool NavigatorOnline() => true; // Przeglądarka sygnalizuje offline/CORS jako HttpRequestException.
}
