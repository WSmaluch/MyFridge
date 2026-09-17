using System.Net;
using System.Text.Json;
using Kitchen.Infrastructure;

namespace Kitchen.Tests;

public sealed class GoogleSheetsRestClientTests
{
    [Fact]
    public async Task UpdateValuesAsync_uses_put_encoded_range_and_value_range_body()
    {
        var handler = new CapturingHandler();
        var client = Create(handler);

        using var _ = await client.UpdateValuesAsync("Produkty!A1", [new[] { "p1", "Mleko" }]);

        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.Equal("https://sheets.googleapis.com/v4/spreadsheets/sheet-123/values/Produkty%21A1?valueInputOption=RAW", handler.Url);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("Produkty!A1", body.RootElement.GetProperty("range").GetString());
        Assert.Equal("ROWS", body.RootElement.GetProperty("majorDimension").GetString());
        Assert.Equal("p1", body.RootElement.GetProperty("values")[0][0].GetString());
        Assert.Equal("Mleko", body.RootElement.GetProperty("values")[0][1].GetString());
    }

    [Fact]
    public async Task AppendValuesAsync_uses_post_append_endpoint_and_value_range_body()
    {
        var handler = new CapturingHandler();
        var client = Create(handler);

        using var _ = await client.AppendValuesAsync("Historia!A:I", [new[] { "event-1", "Dodano" }]);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://sheets.googleapis.com/v4/spreadsheets/sheet-123/values/Historia%21A%3AI:append?valueInputOption=RAW&insertDataOption=INSERT_ROWS", handler.Url);
        Assert.Contains(":append", handler.Url, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("Historia!A:I", body.RootElement.GetProperty("range").GetString());
        Assert.Equal("ROWS", body.RootElement.GetProperty("majorDimension").GetString());
        Assert.Equal("event-1", body.RootElement.GetProperty("values")[0][0].GetString());
    }

    [Fact]
    public async Task Initialize_empty_sheet_creates_tabs_and_writes_every_header_with_put()
    {
        var handler = new InitializerHandler();
        var client = Create(handler);

        await new GoogleSheetsInitializer(client).InitializeAsync();

        var batch = Assert.Single(handler.Requests, x => x.Method == HttpMethod.Post && x.Url.Contains("/spreadsheets/sheet-123:batchUpdate", StringComparison.Ordinal));
        using (var batchBody = JsonDocument.Parse(batch.Body))
            Assert.Equal(7, batchBody.RootElement.GetProperty("requests").GetArrayLength());

        var headerWrites = handler.Requests.Where(x => x.Url.Contains("/values/", StringComparison.Ordinal) && x.Method != HttpMethod.Get).ToList();
        Assert.Equal(7, headerWrites.Count);
        Assert.All(headerWrites, request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Contains("?valueInputOption=RAW", request.Url, StringComparison.Ordinal);
            using var body = JsonDocument.Parse(request.Body);
            Assert.Equal("ROWS", body.RootElement.GetProperty("majorDimension").GetString());
            Assert.Equal(1, body.RootElement.GetProperty("values").GetArrayLength());
        });
    }

    private static GoogleSheetsRestClient Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new TokenAuth(), new TestSettings());

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Url { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Url = request.RequestUri?.OriginalString;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private sealed class InitializerHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.OriginalString ?? string.Empty;
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, url, body));
            var response = request.Method == HttpMethod.Get && url.Contains("fields=sheets.properties", StringComparison.Ordinal)
                ? "{\"sheets\":[]}" : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, string Url, string Body);

    private sealed class TokenAuth : IGoogleAuthService
    {
        public GoogleAuthState State => GoogleAuthState.Connected;
        public bool IsConnected => true;
        public string? ErrorMessage => null;
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("test-token");
    }

    private sealed class TestSettings : ILocalSettingsService
    {
        public KitchenSettings Current { get; } = new() { SpreadsheetId = "sheet-123" };
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(KitchenSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
