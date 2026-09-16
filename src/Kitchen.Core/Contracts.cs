namespace Kitchen.Core;

public interface IInventoryRepository
{
    Task<IReadOnlyList<Product>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<Product?> GetByIdAsync(string productId, CancellationToken cancellationToken = default);
    Task SaveAsync(Product product, CancellationToken cancellationToken = default);
    Task SaveManyAsync(IEnumerable<Product> products, CancellationToken cancellationToken = default);
}
public interface IReceiptRepository
{
    Task<Receipt?> GetAsync(string receiptId, CancellationToken cancellationToken = default);
    Task SaveAsync(Receipt receipt, CancellationToken cancellationToken = default);
}
public interface IRecipeRepository
{
    Task<IReadOnlyList<Recipe>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Recipe?> GetAsync(string recipeId, CancellationToken cancellationToken = default);
    Task SaveAsync(Recipe recipe, CancellationToken cancellationToken = default);
}
public interface IShoppingListRepository
{
    Task<IReadOnlyList<ShoppingItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ShoppingItem item, CancellationToken cancellationToken = default);
    Task DeleteAsync(string itemId, CancellationToken cancellationToken = default);
}
public interface IHistoryRepository
{
    Task<IReadOnlyList<HistoryEvent>> GetRecentAsync(int take, CancellationToken cancellationToken = default);
    Task AppendAsync(IEnumerable<HistoryEvent> events, CancellationToken cancellationToken = default);
}
public interface IProductMappingRepository
{
    Task<ProductMapping?> GetAsync(string rawName, CancellationToken cancellationToken = default);
    Task SaveAsync(ProductMapping mapping, CancellationToken cancellationToken = default);
}
public interface IGeminiService
{
    Task<ReceiptParseResult> ParseReceiptAsync(IReadOnlyCollection<UploadedImage> images, CancellationToken cancellationToken = default);
    Task<RecipeGenerationResult> GenerateRecipesAsync(IReadOnlyList<Product> inventory, RecipeGenerationRequest request, CancellationToken cancellationToken = default);
}
public sealed record UploadedImage(byte[] Bytes, string MimeType, string FileName);
public interface IUnitConversionService
{
    bool TryConvert(decimal quantity, string fromUnit, string toUnit, out decimal converted);
    string Format(decimal quantity, string unit);
}
