using Kitchen.Core;

namespace Kitchen.Infrastructure;

public sealed class InMemoryKitchenStore
{
    public List<Product> Products { get; } = [];
    public List<Receipt> Receipts { get; } = [];
    public List<Recipe> Recipes { get; } = [];
    public List<ShoppingItem> Shopping { get; } = [];
    public List<HistoryEvent> History { get; } = [];
    public List<ProductMapping> Mappings { get; } = [];
    public SemaphoreSlim Gate { get; } = new(1, 1);
}
public sealed class InMemoryRepository(InMemoryKitchenStore store) : IInventoryRepository, IReceiptRepository, IRecipeRepository, IShoppingListRepository, IHistoryRepository, IProductMappingRepository
{
    public async Task<IReadOnlyList<Product>> GetActiveAsync(CancellationToken ct = default) { await store.Gate.WaitAsync(ct); try { return store.Products.Where(x => x.Active).Select(Clone).ToList(); } finally { store.Gate.Release(); } }
    public async Task<Product?> GetByIdAsync(string id, CancellationToken ct = default) { await store.Gate.WaitAsync(ct); try { var product = store.Products.FirstOrDefault(x => x.ProductId == id); return product is null ? null : Clone(product); } finally { store.Gate.Release(); } }
    public async Task SaveAsync(Product p, CancellationToken ct = default) { await store.Gate.WaitAsync(ct); try { Upsert(store.Products, p, x => x.ProductId, Clone); } finally { store.Gate.Release(); } }
    public async Task SaveManyAsync(IEnumerable<Product> products, CancellationToken ct = default) { await store.Gate.WaitAsync(ct); try { foreach (var product in products) Upsert(store.Products, product, x => x.ProductId, Clone); } finally { store.Gate.Release(); } }
    async Task<Receipt?> IReceiptRepository.GetAsync(string id, CancellationToken ct) { await store.Gate.WaitAsync(ct); try { return store.Receipts.FirstOrDefault(x => x.ReceiptId == id) is { } x ? Clone(x) : null; } finally { store.Gate.Release(); } }
    public async Task SaveAsync(Receipt receipt, CancellationToken ct = default) { await store.Gate.WaitAsync(ct); try { Upsert(store.Receipts, receipt, x => x.ReceiptId, Clone); } finally { store.Gate.Release(); } }
    async Task<IReadOnlyList<Recipe>> IRecipeRepository.GetAllAsync(CancellationToken ct) { await store.Gate.WaitAsync(ct); try { return store.Recipes.Select(Clone).ToList(); } finally { store.Gate.Release(); } }
    async Task<Recipe?> IRecipeRepository.GetAsync(string id, CancellationToken ct) { await store.Gate.WaitAsync(ct); try { return store.Recipes.FirstOrDefault(x => x.RecipeId == id) is { } x ? Clone(x) : null; } finally { store.Gate.Release(); } }
    public async Task SaveAsync(Recipe recipe, CancellationToken ct = default) { await store.Gate.WaitAsync(ct); try { Upsert(store.Recipes, recipe, x => x.RecipeId, Clone); } finally { store.Gate.Release(); } }
    async Task<IReadOnlyList<ShoppingItem>> IShoppingListRepository.GetAllAsync(CancellationToken ct) { await store.Gate.WaitAsync(ct); try { return store.Shopping.OrderBy(x => x.Checked).ThenBy(x => x.CreatedAt).Select(Clone).ToList(); } finally { store.Gate.Release(); } }
    public async Task SaveAsync(ShoppingItem item, CancellationToken ct = default) { await store.Gate.WaitAsync(ct); try { Upsert(store.Shopping, item, x => x.ShoppingItemId, Clone); } finally { store.Gate.Release(); } }
    public async Task DeleteAsync(string id, CancellationToken ct = default) { await store.Gate.WaitAsync(ct); try { store.Shopping.RemoveAll(x => x.ShoppingItemId == id); } finally { store.Gate.Release(); } }
    public async Task<IReadOnlyList<HistoryEvent>> GetRecentAsync(int take, CancellationToken ct = default) { await store.Gate.WaitAsync(ct); try { return store.History.OrderByDescending(x => x.Timestamp).Take(take).ToList(); } finally { store.Gate.Release(); } }
    public async Task AppendAsync(IEnumerable<HistoryEvent> events, CancellationToken ct = default) { await store.Gate.WaitAsync(ct); try { store.History.AddRange(events); } finally { store.Gate.Release(); } }
    async Task<ProductMapping?> IProductMappingRepository.GetAsync(string raw, CancellationToken ct) { await store.Gate.WaitAsync(ct); try { return store.Mappings.FirstOrDefault(x => x.RawName == raw) is { } x ? Clone(x) : null; } finally { store.Gate.Release(); } }
    public async Task SaveAsync(ProductMapping mapping, CancellationToken ct = default) { await store.Gate.WaitAsync(ct); try { var index = store.Mappings.FindIndex(x => x.RawName == mapping.RawName); if (index >= 0) store.Mappings[index] = Clone(mapping); else store.Mappings.Add(Clone(mapping)); } finally { store.Gate.Release(); } }
    private static void Upsert<T, TKey>(List<T> list, T value, Func<T, TKey> key, Func<T, T> clone) where TKey : notnull { var index = list.FindIndex(x => EqualityComparer<TKey>.Default.Equals(key(x), key(value))); if (index >= 0) list[index] = clone(value); else list.Add(clone(value)); }
    private static Product Clone(Product p) => new() { ProductId = p.ProductId, Name = p.Name, NormalizedName = p.NormalizedName, Category = p.Category, Location = p.Location, Quantity = p.Quantity, Unit = p.Unit, AlwaysAvailable = p.AlwaysAvailable, LowStockThreshold = p.LowStockThreshold, LastUpdated = p.LastUpdated, Notes = p.Notes, Active = p.Active };
    private static Receipt Clone(Receipt x) => new() { ReceiptId = x.ReceiptId, CreatedAt = x.CreatedAt, PurchaseDate = x.PurchaseDate, Store = x.Store, Total = x.Total, Currency = x.Currency, ItemsCount = x.ItemsCount, Status = x.Status };
    private static Recipe Clone(Recipe x) => new() { RecipeId = x.RecipeId, Name = x.Name, Description = x.Description, TimeMinutes = x.TimeMinutes, Servings = x.Servings, Ingredients = x.Ingredients.Select(i => new RecipeIngredient { ProductId = i.ProductId, Name = i.Name, Quantity = i.Quantity, Unit = i.Unit, Optional = i.Optional, Notes = i.Notes }).ToList(), Steps = [.. x.Steps], IsFavorite = x.IsFavorite, Source = x.Source, CreatedAt = x.CreatedAt, LastCookedAt = x.LastCookedAt, TimesCooked = x.TimesCooked };
    private static ShoppingItem Clone(ShoppingItem x) => new() { ShoppingItemId = x.ShoppingItemId, ProductName = x.ProductName, NormalizedName = x.NormalizedName, Quantity = x.Quantity, Unit = x.Unit, Checked = x.Checked, Reason = x.Reason, CreatedAt = x.CreatedAt };
    private static ProductMapping Clone(ProductMapping x) => new() { RawName = x.RawName, NormalizedName = x.NormalizedName, DisplayName = x.DisplayName, Category = x.Category, DefaultUnit = x.DefaultUnit, DefaultLocation = x.DefaultLocation, LastUsedAt = x.LastUsedAt };
}

public static class DemoSeed
{
    public static async Task SeedAsync(InMemoryKitchenStore store, CancellationToken ct = default)
    {
        if (store.Products.Count != 0) return;
        var repo = new InMemoryRepository(store);
        var products = new[]
        {
            Product("Kurczak", "Mięso", FoodLocation.Lodowka, 650, "g"), Product("Makaron Penne", "Makaron i ryż", FoodLocation.Szafka, 800, "g"),
            Product("Śmietanka 30%", "Nabiał", FoodLocation.Lodowka, 300, "ml"), Product("Ser Gouda", "Nabiał", FoodLocation.Lodowka, 200, "g"),
            Product("Jajka", "Nabiał", FoodLocation.Lodowka, 8, "szt."), Product("Mleko", "Nabiał", FoodLocation.Lodowka, 1, "l"),
            Product("Pomidory", "Warzywa", FoodLocation.Lodowka, 4, "szt."), Product("Ryż", "Makaron i ryż", FoodLocation.Szafka, 1, "kg"),
            new Product { Name = "Sól", NormalizedName = "sol", Category = "Przyprawy", Location = FoodLocation.Szafka, Quantity = 1, Unit = "szt.", AlwaysAvailable = true }
        };
        await repo.SaveManyAsync(products, ct);
        await repo.SaveAsync(new ShoppingItem { ProductName = "Szpinak", NormalizedName = "szpinak", Quantity = 100, Unit = "g", Reason = "Brakuje do przepisu" }, ct);
        await repo.SaveAsync(new ShoppingItem { ProductName = "Banany", NormalizedName = "banany", Quantity = 6, Unit = "szt.", Reason = "Dodane ręcznie" }, ct);
        var chicken = products[0]; var pasta = products[1]; var cream = products[2]; var cheese = products[3];
        await repo.SaveAsync(new Recipe { Name = "Makaron z kurczakiem w sosie serowym", Description = "Kremowy, szybki obiad z produktów, które masz pod ręką.", TimeMinutes = 25, Servings = 2, Source = "Manual", IsFavorite = true, Ingredients = [new() { ProductId = chicken.ProductId, Name = chicken.Name, Quantity = 300, Unit = "g" }, new() { ProductId = pasta.ProductId, Name = pasta.Name, Quantity = 250, Unit = "g" }, new() { ProductId = cream.ProductId, Name = cream.Name, Quantity = 150, Unit = "ml" }, new() { ProductId = cheese.ProductId, Name = cheese.Name, Quantity = 80, Unit = "g" }, new() { Name = "Szpinak", Quantity = 100, Unit = "g" }], Steps = ["Ugotuj makaron al dente.", "Podsmaż kurczaka na złoto.", "Dodaj śmietankę i ser, mieszaj do powstania sosu.", "Połącz z makaronem i podawaj."] }, ct);
    }
    private static Product Product(string name, string category, FoodLocation location, decimal quantity, string unit) => new() { Name = name, NormalizedName = PolishText.Normalize(name), Category = category, Location = location, Quantity = quantity, Unit = unit };
}
