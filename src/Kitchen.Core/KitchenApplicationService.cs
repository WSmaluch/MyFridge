namespace Kitchen.Core;

public sealed class KitchenApplicationService(
    IInventoryRepository inventory, IReceiptRepository receipts, IRecipeRepository recipes,
    IShoppingListRepository shopping, IHistoryRepository history, IProductMappingRepository mappings,
    IUnitConversionService units)
{
    public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken ct = default) => inventory.GetActiveAsync(ct);
    public Task<IReadOnlyList<ShoppingItem>> GetShoppingAsync(CancellationToken ct = default) => shopping.GetAllAsync(ct);
    public Task<IReadOnlyList<Recipe>> GetRecipesAsync(CancellationToken ct = default) => recipes.GetAllAsync(ct);
    public Task<IReadOnlyList<HistoryEvent>> GetHistoryAsync(int take = 100, CancellationToken ct = default) => history.GetRecentAsync(take, ct);
    public Task<Product?> GetProductAsync(string id, CancellationToken ct = default) => inventory.GetByIdAsync(id, ct);
    public Task<Recipe?> GetRecipeAsync(string id, CancellationToken ct = default) => recipes.GetAsync(id, ct);

    public async Task SaveProductAsync(Product updated, string description = "Ręczna korekta", CancellationToken ct = default)
    {
        updated.NormalizedName = PolishText.Normalize(updated.Name); updated.Unit = Units.Canonical(updated.Unit); updated.LastUpdated = DateTimeOffset.UtcNow;
        var existing = await inventory.GetByIdAsync(updated.ProductId, ct);
        await inventory.SaveAsync(updated, ct);
        var delta = updated.Quantity - (existing?.Quantity ?? 0m);
        if (delta != 0m) await history.AppendAsync([Event(HistoryType.ManualAdjustment, updated, delta, null, description)], ct);
    }

    public async Task AdjustQuantityAsync(string productId, decimal delta, HistoryType type, string description, CancellationToken ct = default)
    {
        var product = await inventory.GetByIdAsync(productId, ct) ?? throw new InvalidOperationException("Nie znaleziono produktu.");
        var actualDelta = Math.Max(-product.Quantity, delta);
        product.Quantity += actualDelta; product.LastUpdated = DateTimeOffset.UtcNow;
        await inventory.SaveAsync(product, ct);
        await history.AppendAsync([Event(type, product, actualDelta, null, description)], ct);
    }

    public async Task ConfirmReceiptAsync(Receipt receipt, IEnumerable<ReceiptItem> selectedItems, CancellationToken ct = default)
    {
        var stored = await receipts.GetAsync(receipt.ReceiptId, ct);
        if (stored?.Status == "Imported") return;
        var all = (await inventory.GetActiveAsync(ct)).ToList(); var events = new List<HistoryEvent>();
        foreach (var item in selectedItems.Where(x => x.IncludeInInventory && x.Quantity is > 0 && !string.IsNullOrWhiteSpace(x.Unit)))
        {
            item.NormalizedName = string.IsNullOrWhiteSpace(item.NormalizedName) ? PolishText.Normalize(item.DisplayName) : PolishText.Normalize(item.NormalizedName);
            item.Unit = Units.Canonical(item.Unit);
            var product = all.FirstOrDefault(x => x.NormalizedName == item.NormalizedName && units.TryConvert(item.Quantity!.Value, item.Unit!, x.Unit, out _));
            if (product is null)
            {
                product = new Product { Name = item.DisplayName, NormalizedName = item.NormalizedName, Quantity = item.Quantity!.Value, Unit = item.Unit!, Category = item.Category, Location = item.Location };
                all.Add(product); events.Add(Event(HistoryType.Purchase, product, item.Quantity.Value, receipt.ReceiptId, $"Zakupy • {receipt.Store ?? "paragon"}"));
            }
            else
            {
                units.TryConvert(item.Quantity!.Value, item.Unit!, product.Unit, out var added);
                product.Quantity += added; product.LastUpdated = DateTimeOffset.UtcNow;
                events.Add(Event(HistoryType.Purchase, product, added, receipt.ReceiptId, $"Zakupy • {receipt.Store ?? "paragon"}"));
            }
            await mappings.SaveAsync(new ProductMapping { RawName = item.RawName, NormalizedName = item.NormalizedName, DisplayName = item.DisplayName, Category = item.Category, DefaultUnit = item.Unit!, DefaultLocation = item.Location }, ct);
            var matchingShopping = (await shopping.GetAllAsync(ct)).Where(x => x.NormalizedName == item.NormalizedName).ToList();
            foreach (var listItem in matchingShopping) { listItem.Checked = true; await shopping.SaveAsync(listItem, ct); }
        }
        receipt.Status = "Imported"; receipt.ItemsCount = selectedItems.Count(x => x.IncludeInInventory); await inventory.SaveManyAsync(all, ct); await receipts.SaveAsync(receipt, ct); await history.AppendAsync(events, ct);
    }

    public async Task<RecipeAvailability> CheckAvailabilityAsync(Recipe recipe, CancellationToken ct = default)
    {
        var items = new List<IngredientAvailability>();
        foreach (var ingredient in recipe.Ingredients)
        {
            if (ingredient.Optional) { items.Add(new IngredientAvailability { Ingredient = ingredient, Available = true }); continue; }
            var product = ingredient.ProductId is null ? null : await inventory.GetByIdAsync(ingredient.ProductId, ct);
            var available = product is not null && (product.AlwaysAvailable || (ingredient.Quantity is null || ingredient.Unit is null || (units.TryConvert(ingredient.Quantity.Value, ingredient.Unit, product.Unit, out var needed) && product.Quantity >= needed)));
            items.Add(new IngredientAvailability { Ingredient = ingredient, Available = available, Reason = available ? null : product is null ? "Brakuje w domu" : "Za mało produktu" });
        }
        return new RecipeAvailability { Recipe = recipe, Ingredients = items };
    }

    public async Task CookRecipeAsync(string recipeId, CancellationToken ct = default)
    {
        var recipe = await recipes.GetAsync(recipeId, ct) ?? throw new InvalidOperationException("Nie znaleziono przepisu.");
        var availability = await CheckAvailabilityAsync(recipe, ct);
        if (!availability.CanCook) throw new InvalidOperationException("Stan produktów zmienił się. Sprawdź brakujące składniki.");
        var changed = new List<Product>(); var events = new List<HistoryEvent>();
        foreach (var ingredient in recipe.Ingredients.Where(x => !x.Optional && x.ProductId is not null && x.Quantity is not null && x.Unit is not null))
        {
            var product = await inventory.GetByIdAsync(ingredient.ProductId!, ct) ?? throw new InvalidOperationException("Produkt zniknął z zapasów.");
            if (product.AlwaysAvailable) continue;
            if (!units.TryConvert(ingredient.Quantity!.Value, ingredient.Unit!, product.Unit, out var amount) || product.Quantity < amount) throw new InvalidOperationException("Nie można bezpiecznie odjąć składników.");
            product.Quantity -= amount; product.LastUpdated = DateTimeOffset.UtcNow; changed.Add(product); events.Add(Event(HistoryType.Recipe, product, -amount, recipe.RecipeId, recipe.Name));
        }
        await inventory.SaveManyAsync(changed, ct); await history.AppendAsync(events, ct);
        recipe.TimesCooked++; recipe.LastCookedAt = DateTimeOffset.UtcNow; await recipes.SaveAsync(recipe, ct);
    }

    public async Task AddMissingToShoppingAsync(Recipe recipe, CancellationToken ct = default)
    {
        var availability = await CheckAvailabilityAsync(recipe, ct); var list = (await shopping.GetAllAsync(ct)).ToList();
        foreach (var missing in availability.Ingredients.Where(x => !x.Available && !x.Ingredient.Optional))
        {
            var ingredient = missing.Ingredient; var normalized = PolishText.Normalize(ingredient.Name);
            var current = list.FirstOrDefault(x => x.NormalizedName == normalized && !x.Checked);
            if (current is null) { current = new ShoppingItem { ProductName = ingredient.Name, NormalizedName = normalized, Quantity = ingredient.Quantity, Unit = ingredient.Unit, Reason = $"Brakuje do: {recipe.Name}" }; list.Add(current); }
            else if (ingredient.Quantity is not null && ingredient.Unit is not null && current.Quantity is not null && current.Unit is not null && units.TryConvert(ingredient.Quantity.Value, ingredient.Unit, current.Unit, out var converted)) current.Quantity = Math.Max(current.Quantity.Value, converted);
            await shopping.SaveAsync(current, ct);
        }
    }

    public async Task AddShoppingAsync(ShoppingItem item, CancellationToken ct = default)
    {
        item.NormalizedName = PolishText.Normalize(item.ProductName); await shopping.SaveAsync(item, ct);
    }
    public Task SaveShoppingAsync(ShoppingItem item, CancellationToken ct = default) => shopping.SaveAsync(item, ct);
    public Task DeleteShoppingAsync(string id, CancellationToken ct = default) => shopping.DeleteAsync(id, ct);
    public async Task SaveRecipeAsync(Recipe recipe, CancellationToken ct = default) => await recipes.SaveAsync(recipe, ct);
    private static HistoryEvent Event(HistoryType type, Product product, decimal delta, string? sourceId, string description) => new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, type, product.ProductId, product.Name, delta, product.Unit, sourceId, description);
}
