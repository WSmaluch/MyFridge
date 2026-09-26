using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kitchen.Core;

namespace Kitchen.Infrastructure;

public sealed class BrowserGeminiService(HttpClient http, ILocalSettingsService settings) : IGeminiService
{
    public const long MaxInlineImageBytes = 14 * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();
    public async Task<ReceiptParseResult> ParseReceiptAsync(IReadOnlyCollection<UploadedImage> images, CancellationToken ct = default)
    {
        if (images.Count == 0) throw new InvalidOperationException("Dodaj przynajmniej jedno zdjęcie paragonu.");
        if (images.Sum(image => (long)image.Bytes.Length) > MaxInlineImageBytes)
            throw new InvalidOperationException("Zdjęcia są łącznie za duże dla Gemini (maks. 14 MB). Wybierz mniej zdjęć lub zmniejsz je.");
        var prompt = "Odczytaj paragon. Zwróć wyłącznie JSON: store, purchaseDate (YYYY-MM-DD lub null), total, currency, items. Każda pozycja: rawName, displayName, normalizedName, quantity (lub null), unit (lub null), unitPrice, totalPrice, category, location (Lodowka|Zamrazarka|Szafka|Inne), confidence 0..1, includeInInventory. Nie wymyślaj ilości. Chemia i kosmetyki mają includeInInventory=false.";
        var parts = new List<object> { new { text = prompt } };
        parts.AddRange(images.Select(x => (object)new { inlineData = new { mimeType = x.MimeType, data = Convert.ToBase64String(x.Bytes) } }));
        return await GenerateAsync<ReceiptParseResult>(parts, ReceiptSchema, ct) ?? throw new InvalidOperationException("Gemini nie zwrócił pozycji paragonu.");
    }
    public async Task<RecipeGenerationResult> GenerateRecipesAsync(IReadOnlyList<Product> inventory, RecipeGenerationRequest request, CancellationToken ct = default)
    {
        var supplied = inventory.Select(x => new { x.ProductId, x.Name, x.Quantity, x.Unit, x.AlwaysAvailable }).ToArray();
        var prompt = $"Jesteś polskim asystentem kulinarnym. Zaproponuj 3 realistyczne przepisy dla {request.Servings} osób. Preferuj: {JsonSerializer.Serialize(supplied)}. Maksymalny czas: {request.MaxTimeMinutes?.ToString() ?? "dowolny"}; limit braków: {request.MaxMissingIngredients?.ToString() ?? "dowolny"}; ochota: {request.Craving ?? "dowolna"}. Istniejące ProductId kopiuj dokładnie, nigdy ich nie wymyślaj. Dla produktu spoza inventory ustaw productId null. Zwróć wyłącznie JSON.";
        return await GenerateAsync<RecipeGenerationResult>([new { text = prompt }], RecipeSchema, ct) ?? throw new InvalidOperationException("Gemini nie zwrócił przepisów.");
    }
    public async Task TestAsync(CancellationToken ct = default)
    {
        _ = await GenerateAsync<JsonElement>([new { text = "Odpowiedz dokładnie JSON: {\"ok\":true}." }], new { type = "object", properties = new { ok = new { type = "boolean" } }, required = new[] { "ok" } }, ct);
    }
    private async Task<T?> GenerateAsync<T>(IEnumerable<object> parts, object schema, CancellationToken ct)
    {
        var key = settings.Current.GeminiApiKey;
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Uzupełnij Gemini API Key w ustawieniach.");
        var model = string.IsNullOrWhiteSpace(settings.Current.GeminiModel) ? "gemini-2.5-flash" : settings.Current.GeminiModel.Trim();
        // Try compatible image/structured-output models when a Flash model is temporarily overloaded.
        string[] models = model switch
        {
            "gemini-3.5-flash" => [model, "gemini-3.8-flash", "gemini-3.5-flash-lite"],
            "gemini-3.8-flash" => [model, "gemini-3.5-flash-lite"],
            _ => [model]
        };
        var body = new { contents = new[] { new { role = "user", parts } }, generationConfig = new { responseMimeType = "application/json", responseJsonSchema = schema, temperature = 0.2 } };
        foreach (var activeModel in models)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(activeModel)}:generateContent";
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
                request.Headers.Add("x-goog-api-key", key);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                requestTimeout.CancelAfter(RequestTimeout);
                try
                {
                    using var response = await http.SendAsync(request, requestTimeout.Token);
                    var text = await response.Content.ReadAsStringAsync(requestTimeout.Token);
                    if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout)
                    {
                        if (attempt < 2)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct);
                            continue;
                        }
                        if (activeModel != models[^1]) break;
                    }
                    if (!response.IsSuccessStatusCode) throw CreateApiException(response.StatusCode, text, activeModel, key);
                    using var doc = JsonDocument.Parse(text);
                    var json = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                    return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Json);
                }
                catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
                {
                    if (activeModel != models[^1]) break;
                    throw new InvalidOperationException($"Model Gemini „{activeModel}” nie odpowiedział w ciągu {RequestTimeout.TotalSeconds:0} sekund. Spróbuj ponownie później lub wybierz inny model.", ex);
                }
                catch (HttpRequestException ex) { throw new InvalidOperationException("Nie można połączyć się z Gemini. Sprawdź internet, konfigurację klucza i czy przeglądarka nie blokuje CORS.", ex); }
            }
        }
        throw new InvalidOperationException("Gemini nie zwrócił odpowiedzi.");
    }
    private static InvalidOperationException CreateApiException(HttpStatusCode status, string body, string model, string key)
    {
        string? detail = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message))
                detail = message.GetString();
        }
        catch (JsonException) { /* The status code remains useful when the server returns non-JSON. */ }
        if (!string.IsNullOrEmpty(detail))
        {
            detail = detail.Replace(key, "[ukryty klucz]", StringComparison.Ordinal)
                .Replace('\r', ' ').Replace('\n', ' ');
            if (detail.Length > 300) detail = detail[..300] + "…";
        }
        var prefix = status switch
        {
            HttpStatusCode.BadRequest => "Gemini odrzucił żądanie (400).",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Brak dostępu do Gemini (401/403). Sprawdź klucz API, ograniczenia i dostępność modelu.",
            HttpStatusCode.NotFound => $"Model Gemini „{model}” jest niedostępny (404). Sprawdź model w ustawieniach.",
            HttpStatusCode.RequestEntityTooLarge => "Zdjęcia są za duże dla Gemini (413).",
            HttpStatusCode.TooManyRequests => "Limit Gemini został osiągnięty (429). Spróbuj ponownie później.",
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout => $"Usługa Gemini dla modelu „{model}” jest chwilowo niedostępna (HTTP {(int)status}). Spróbuj później albo wybierz inny model w ustawieniach.",
            _ => $"Gemini zwrócił błąd HTTP {(int)status}."
        };
        return new InvalidOperationException(string.IsNullOrWhiteSpace(detail) ? prefix : $"{prefix} {detail}");
    }
    private static JsonSerializerOptions CreateJsonOptions() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true }; options.Converters.Add(new JsonStringEnumConverter()); return options; }
    private static readonly object ReceiptSchema = new { type = "object", properties = new { store = new { type = new[] { "string", "null" } }, purchaseDate = new { type = new[] { "string", "null" } }, total = new { type = new[] { "number", "null" } }, currency = new { type = "string" }, items = new { type = "array", items = new { type = "object", properties = new { rawName = new { type = "string" }, displayName = new { type = "string" }, normalizedName = new { type = "string" }, quantity = new { type = new[] { "number", "null" } }, unit = new { type = new[] { "string", "null" } }, unitPrice = new { type = new[] { "number", "null" } }, totalPrice = new { type = new[] { "number", "null" } }, category = new { type = "string" }, location = new { type = "string", @enum = new[] { "Lodowka", "Zamrazarka", "Szafka", "Inne" } }, confidence = new { type = "number" }, includeInInventory = new { type = "boolean" } }, required = new[] { "rawName", "displayName", "normalizedName", "quantity", "unit", "category", "location", "confidence", "includeInInventory" } } } }, required = new[] { "currency", "items" } };
    private static readonly object RecipeSchema = new { type = "object", properties = new { recipes = new { type = "array", items = new { type = "object", properties = new { name = new { type = "string" }, description = new { type = "string" }, timeMinutes = new { type = "integer" }, servings = new { type = "integer" }, ingredients = new { type = "array", items = new { type = "object", properties = new { productId = new { type = new[] { "string", "null" } }, name = new { type = "string" }, quantity = new { type = new[] { "number", "null" } }, unit = new { type = new[] { "string", "null" } }, optional = new { type = "boolean" }, notes = new { type = new[] { "string", "null" } } }, required = new[] { "productId", "name", "quantity", "unit", "optional" } } }, steps = new { type = "array", items = new { type = "string" } } }, required = new[] { "name", "description", "timeMinutes", "servings", "ingredients", "steps" } } } }, required = new[] { "recipes" } };
}
