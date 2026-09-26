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
        Assert.True(handler.RequestTokenCanBeCanceled);
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
        var service = new BrowserGeminiService(new HttpClient(handler), new TestSettings("gemini-2.5-flash"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.TestAsync());

        Assert.Equal(3, handler.RequestCount);
        Assert.Contains("chwilowo niedostępna (HTTP 503)", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Model is overloaded", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestAsync_uses_compatible_fallback_after_3_5_flash_is_unavailable()
    {
        var handler = new FallbackHandler();
        var service = new BrowserGeminiService(new HttpClient(handler), new TestSettings());

        await service.TestAsync();

        Assert.Equal(4, handler.Urls.Count);
        Assert.All(handler.Urls.Take(3), url => Assert.Contains("/gemini-3.5-flash:generateContent", url, StringComparison.Ordinal));
        Assert.Contains("/gemini-3.8-flash:generateContent", handler.Urls[3], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestAsync_uses_flash_lite_when_both_flash_models_are_unavailable()
    {
        var handler = new FallbackHandler(6);
        var service = new BrowserGeminiService(new HttpClient(handler), new TestSettings());

        await service.TestAsync();

        Assert.Equal(7, handler.Urls.Count);
        Assert.All(handler.Urls.Take(3), url => Assert.Contains("/gemini-3.5-flash:generateContent", url, StringComparison.Ordinal));
        Assert.All(handler.Urls.Skip(3).Take(3), url => Assert.Contains("/gemini-3.8-flash:generateContent", url, StringComparison.Ordinal));
        Assert.Contains("/gemini-3.5-flash-lite:generateContent", handler.Urls[6], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestAsync_moves_to_next_model_after_request_timeout()
    {
        var handler = new TimeoutHandler(failFirstOnly: true);
        var service = new BrowserGeminiService(new HttpClient(handler), new TestSettings());

        await service.TestAsync();

        Assert.Equal(2, handler.Urls.Count);
        Assert.Contains("/gemini-3.5-flash:generateContent", handler.Urls[0], StringComparison.Ordinal);
        Assert.Contains("/gemini-3.8-flash:generateContent", handler.Urls[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestAsync_explains_timeout_on_last_model()
    {
        var handler = new TimeoutHandler(failFirstOnly: false);
        var service = new BrowserGeminiService(new HttpClient(handler), new TestSettings("gemini-2.5-flash"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.TestAsync());

        Assert.Contains("nie odpowiedział w ciągu 60 sekund", exception.Message, StringComparison.Ordinal);
        Assert.Single(handler.Urls);
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
        public bool RequestTokenCanBeCanceled { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            Url = request.RequestUri?.OriginalString;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(content) };
        }
    }

    private sealed class FallbackHandler(int failuresBeforeSuccess = 3) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.OriginalString);
            var response = Urls.Count <= failuresBeforeSuccess
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"error\":{\"message\":\"Overloaded\"}}") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"ok\\\":true}\"}]}}]}") };
            return Task.FromResult(response);
        }
    }

    private sealed class TimeoutHandler(bool failFirstOnly) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.OriginalString);
            if (!failFirstOnly || Urls.Count == 1) throw new TaskCanceledException("Simulated request timeout");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"ok\\\":true}\"}]}}]}")
            });
        }
    }

    private sealed class TestSettings(string model = "gemini-3.5-flash") : ILocalSettingsService
    {
        public KitchenSettings Current { get; } = new() { GeminiApiKey = "test-secret", GeminiModel = model };
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(KitchenSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
