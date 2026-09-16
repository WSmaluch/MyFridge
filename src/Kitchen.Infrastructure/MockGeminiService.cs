using Kitchen.Core;

namespace Kitchen.Infrastructure;
public sealed class MockGeminiService : IGeminiService
{
    public Task<ReceiptParseResult> ParseReceiptAsync(IReadOnlyCollection<UploadedImage> images, CancellationToken ct = default) => Task.FromResult(new ReceiptParseResult { Store = "Lidl", PurchaseDate = DateOnly.FromDateTime(DateTime.Today), Total = 48.76m, Items = [new() { RawName = "MLEKO LAC 3.2 1L", DisplayName = "Mleko Łaciate 3,2%", NormalizedName = "mleko laciate 32", Quantity = 1, Unit = "l", Category = "Nabiał", Location = FoodLocation.Lodowka, Confidence = .96m, IncludeInInventory = true }, new() { RawName = "PIERS KURCZ 0.742", DisplayName = "Pierś z kurczaka", NormalizedName = "piers z kurczaka", Quantity = .742m, Unit = "kg", Category = "Mięso", Location = FoodLocation.Lodowka, Confidence = .72m, IncludeInInventory = true }, new() { RawName = "PŁYN DO NACZYN", DisplayName = "Płyn do naczyń", NormalizedName = "plyn do naczyn", Quantity = 1, Unit = "szt.", Category = "Chemia", Location = FoodLocation.Inne, Confidence = .99m, IncludeInInventory = false }] });
    public Task<RecipeGenerationResult> GenerateRecipesAsync(IReadOnlyList<Product> inventory, RecipeGenerationRequest request, CancellationToken ct = default)
    {
        Product? Find(string name) => inventory.FirstOrDefault(x => x.NormalizedName == PolishText.Normalize(name));
        var chicken = Find("Kurczak"); var rice = Find("Ryż"); var tomatoes = Find("Pomidory"); var eggs = Find("Jajka");
        return Task.FromResult(new RecipeGenerationResult { Recipes = [
            new Recipe { Name = "Kurczak z ryżem i pomidorami", Description = "Lekki, domowy obiad w jednej patelni.", TimeMinutes = 30, Servings = request.Servings, Source = "Gemini", Ingredients = [new() { ProductId = chicken?.ProductId, Name = "Kurczak", Quantity = 350, Unit = "g" }, new() { ProductId = rice?.ProductId, Name = "Ryż", Quantity = 180, Unit = "g" }, new() { ProductId = tomatoes?.ProductId, Name = "Pomidory", Quantity = 2, Unit = "szt." }], Steps = ["Ugotuj ryż.", "Podsmaż kurczaka.", "Dodaj pomidory i duś 8 minut.", "Podawaj z ryżem."] },
            new Recipe { Name = "Omlet pomidorowy", Description = "Szybkie śniadanie lub kolacja.", TimeMinutes = 15, Servings = request.Servings, Source = "Gemini", Ingredients = [new() { ProductId = eggs?.ProductId, Name = "Jajka", Quantity = 4, Unit = "szt." }, new() { ProductId = tomatoes?.ProductId, Name = "Pomidory", Quantity = 2, Unit = "szt." }, new() { Name = "Szczypiorek", Quantity = 1, Unit = "szt.", Optional = true }], Steps = ["Roztrzep jajka.", "Dodaj pokrojone pomidory.", "Smaż na małym ogniu do ścięcia."] }
        ] });
    }
}
