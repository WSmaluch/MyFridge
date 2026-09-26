using System.Net;
using Kitchen.Core;
using Kitchen.Infrastructure;

namespace Kitchen.Tests;

public sealed class BrowserGeminiServiceTests
{
    [Fact]
    public async Task TestAsync_uses_selected_model_and_json_schema()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"ok\\\":true}\"}]}}]}");
        var service = new BrowserGeminiService(new HttpClient(handler), new TestSettings());

        await service.TestAsync();

        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash:generateContent", handler.Url);
        Assert.Contains("\"responseJsonSchema\"", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("test-secret", handler.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Gemini odrzucił żądanie (400).")]
    [InlineData(HttpStatusCode.NotFound, "jest niedostępny (404)")]
    [InlineData(HttpStatusCode.TooManyRequests, "Limit Gemini został osiągnięty (429)")]
    public async Task TestAsync_explains_api_errors(HttpStatusCode status, string expected)
    {
        var handler = new StubHandler(status, "{\"error\":{\"message\":\"Request rejected\"}}");
        var service = new BrowserGeminiService(new HttpClient(handler), new TestSettings());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.TestAsync());

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Request rejected", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestAsync_retries_service_unavailable_twice_then_explains_failure()
    {
        var handler = new StubHandler(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"message\":\"Model is overloaded\"}}");
        var service = new BrowserGeminiService(new HttpClient(handler), new TestSettings());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.TestAsync());

        Assert.Equal(3, handler.RequestCount);
        Assert.Contains("chwilowo niedostępna (HTTP 503)", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Model is overloaded", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParseReceiptAsync_rejects_images_over_inline_limit_before_http_request()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var service = new BrowserGeminiService(new HttpClient(handler), new TestSettings());
        var image = new UploadedImage(new byte[BrowserGeminiService.MaxInlineImageBytes + 1], "image/jpeg", "receipt.jpg");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ParseReceiptAsync([image]));

        Assert.Contains("14 MB", exception.Message, StringComparison.Ordinal);
        Assert.Null(handler.Url);
    }

    private sealed class StubHandler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string Body { get; private set; } = string.Empty;
        public int RequestCount { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Url = request.RequestUri?.OriginalString;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(content) };
        }
    }

    private sealed class TestSettings : ILocalSettingsService
    {
        public KitchenSettings Current { get; } = new() { GeminiApiKey = "test-secret", GeminiModel = "gemini-3.5-flash" };
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(KitchenSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
