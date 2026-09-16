using Kitchen.Core;

namespace Kitchen.Infrastructure;

/// <summary>Wybiera repozytorium bez zmiany kontraktów używanych przez interfejs.</summary>
public sealed class SwitchingRepository(
    InMemoryRepository demo,
    GoogleSheetsRepository sheets,
    IAppModeStore mode) : IInventoryRepository, IReceiptRepository, IRecipeRepository, IShoppingListRepository, IHistoryRepository, IProductMappingRepository
{
    private bool UseSheets => mode.Mode == DataMode.GoogleSheets;

    public Task<IReadOnlyList<Product>> GetActiveAsync(CancellationToken ct = default) => UseSheets ? sheets.GetActiveAsync(ct) : demo.GetActiveAsync(ct);
    public Task<Product?> GetByIdAsync(string id, CancellationToken ct = default) => UseSheets ? sheets.GetByIdAsync(id, ct) : demo.GetByIdAsync(id, ct);
    public Task SaveAsync(Product value, CancellationToken ct = default) => UseSheets ? sheets.SaveAsync(value, ct) : demo.SaveAsync(value, ct);
    public Task SaveManyAsync(IEnumerable<Product> values, CancellationToken ct = default) => UseSheets ? sheets.SaveManyAsync(values, ct) : demo.SaveManyAsync(values, ct);

    Task<Receipt?> IReceiptRepository.GetAsync(string id, CancellationToken ct) => UseSheets ? ((IReceiptRepository)sheets).GetAsync(id, ct) : ((IReceiptRepository)demo).GetAsync(id, ct);
    public Task SaveAsync(Receipt value, CancellationToken ct = default) => UseSheets ? sheets.SaveAsync(value, ct) : demo.SaveAsync(value, ct);

    Task<IReadOnlyList<Recipe>> IRecipeRepository.GetAllAsync(CancellationToken ct) => UseSheets ? ((IRecipeRepository)sheets).GetAllAsync(ct) : ((IRecipeRepository)demo).GetAllAsync(ct);
    Task<Recipe?> IRecipeRepository.GetAsync(string id, CancellationToken ct) => UseSheets ? ((IRecipeRepository)sheets).GetAsync(id, ct) : ((IRecipeRepository)demo).GetAsync(id, ct);
    public Task SaveAsync(Recipe value, CancellationToken ct = default) => UseSheets ? sheets.SaveAsync(value, ct) : demo.SaveAsync(value, ct);

    Task<IReadOnlyList<ShoppingItem>> IShoppingListRepository.GetAllAsync(CancellationToken ct) => UseSheets ? ((IShoppingListRepository)sheets).GetAllAsync(ct) : ((IShoppingListRepository)demo).GetAllAsync(ct);
    public Task SaveAsync(ShoppingItem value, CancellationToken ct = default) => UseSheets ? sheets.SaveAsync(value, ct) : demo.SaveAsync(value, ct);
    public Task DeleteAsync(string id, CancellationToken ct = default) => UseSheets ? sheets.DeleteAsync(id, ct) : demo.DeleteAsync(id, ct);

    public Task<IReadOnlyList<HistoryEvent>> GetRecentAsync(int take, CancellationToken ct = default) => UseSheets ? sheets.GetRecentAsync(take, ct) : demo.GetRecentAsync(take, ct);
    public Task AppendAsync(IEnumerable<HistoryEvent> values, CancellationToken ct = default) => UseSheets ? sheets.AppendAsync(values, ct) : demo.AppendAsync(values, ct);

    Task<ProductMapping?> IProductMappingRepository.GetAsync(string rawName, CancellationToken ct) => UseSheets ? ((IProductMappingRepository)sheets).GetAsync(rawName, ct) : ((IProductMappingRepository)demo).GetAsync(rawName, ct);
    public Task SaveAsync(ProductMapping value, CancellationToken ct = default) => UseSheets ? sheets.SaveAsync(value, ct) : demo.SaveAsync(value, ct);
}

/// <summary>W trybie Demo zachowuje deterministyczne odpowiedzi, a w trybie Google wywołuje Gemini z przeglądarki.</summary>
public sealed class SwitchingGeminiService(MockGeminiService demo, BrowserGeminiService gemini, IAppModeStore mode) : IGeminiService
{
    private IGeminiService Current => mode.Mode == DataMode.GoogleSheets ? gemini : demo;
    public Task<ReceiptParseResult> ParseReceiptAsync(IReadOnlyCollection<UploadedImage> images, CancellationToken ct = default) => Current.ParseReceiptAsync(images, ct);
    public Task<RecipeGenerationResult> GenerateRecipesAsync(IReadOnlyList<Product> inventory, RecipeGenerationRequest request, CancellationToken ct = default) => Current.GenerateRecipesAsync(inventory, request, ct);
}
