using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kitchen.Core;

namespace Kitchen.Infrastructure;

public sealed class BrowserGeminiService(HttpClient http, ILocalSettingsService settings) : IGeminiService
{
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();
    public async Task<ReceiptParseResult> ParseReceiptAsync(IReadOnlyCollection<UploadedImage> images, CancellationToken ct = default)
    {
        if (images.Count == 0) throw new InvalidOperationException("Dodaj przynajmniej jedno zdjęcie paragonu.");
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
        var model = string.IsNullOrWhiteSpace(settings.Current.GeminiModel) ? "gemini-2.5-flash" : settings.Current.GeminiModel;
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
        var body = new { contents = new[] { new { role = "user", parts } }, generationConfig = new { responseMimeType = "application/json", responseJsonSchema = schema, temperature = 0.2 } };
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("x-goog-api-key", key);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, ct); }
        catch (HttpRequestException ex) { throw new InvalidOperationException("Nie można połączyć się z Gemini. Sprawdź internet, konfigurację klucza i czy przeglądarka nie blokuje CORS.", ex); }
        using (response)
        {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new InvalidOperationException("Limit Gemini został chwilowo osiągnięty. Spróbuj ponownie za chwilę.");
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new InvalidOperationException("Nieprawidłowy klucz Gemini API.");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Gemini jest chwilowo niedostępne. Spróbuj ponownie.");
        using var doc = JsonDocument.Parse(text);
        var json = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
        return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Json);
        }
    }
    private static JsonSerializerOptions CreateJsonOptions() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true }; options.Converters.Add(new JsonStringEnumConverter()); return options; }
    private static readonly object ReceiptSchema = new { type = "object", properties = new { store = new { type = new[] { "string", "null" } }, purchaseDate = new { type = new[] { "string", "null" } }, total = new { type = new[] { "number", "null" } }, currency = new { type = "string" }, items = new { type = "array", items = new { type = "object", properties = new { rawName = new { type = "string" }, displayName = new { type = "string" }, normalizedName = new { type = "string" }, quantity = new { type = new[] { "number", "null" } }, unit = new { type = new[] { "string", "null" } }, unitPrice = new { type = new[] { "number", "null" } }, totalPrice = new { type = new[] { "number", "null" } }, category = new { type = "string" }, location = new { type = "string", @enum = new[] { "Lodowka", "Zamrazarka", "Szafka", "Inne" } }, confidence = new { type = "number" }, includeInInventory = new { type = "boolean" } }, required = new[] { "rawName", "displayName", "normalizedName", "quantity", "unit", "category", "location", "confidence", "includeInInventory" } } } }, required = new[] { "currency", "items" } };
    private static readonly object RecipeSchema = new { type = "object", properties = new { recipes = new { type = "array", items = new { type = "object", properties = new { name = new { type = "string" }, description = new { type = "string" }, timeMinutes = new { type = "integer" }, servings = new { type = "integer" }, ingredients = new { type = "array", items = new { type = "object", properties = new { productId = new { type = new[] { "string", "null" } }, name = new { type = "string" }, quantity = new { type = new[] { "number", "null" } }, unit = new { type = new[] { "string", "null" } }, optional = new { type = "boolean" }, notes = new { type = new[] { "string", "null" } } }, required = new[] { "productId", "name", "quantity", "unit", "optional" } } }, steps = new { type = "array", items = new { type = "string" } } }, required = new[] { "name", "description", "timeMinutes", "servings", "ingredients", "steps" } } } }, required = new[] { "recipes" } };
}
