using Kitchen.Core;
using Kitchen.Infrastructure;

namespace Kitchen.Tests;

public sealed class KitchenApplicationTests
{
    [Theory]
    [InlineData(1, "kg", "g", 1000)]
    [InlineData(1500, "g", "kg", 1.5)]
    [InlineData(2, "l", "ml", 2000)]
    public void Converts_compatible_units(decimal amount, string from, string to, decimal expected)
    {
        var service = new UnitConversionService();
        Assert.True(service.TryConvert(amount, from, to, out var actual)); Assert.Equal(expected, actual);
    }
    [Fact]
    public void Does_not_convert_pieces_to_weight() => Assert.False(new UnitConversionService().TryConvert(2, "szt.", "g", out _));
    [Fact]
    public async Task Receipt_merges_the_same_normalized_product()
    {
        var (app, repo) = Create(); var pasta = Product("Makaron Penne", 300, "g"); await repo.SaveAsync(pasta);
        await app.ConfirmReceiptAsync(new Receipt { Store = "Lidl" }, [new ReceiptItem { RawName = "PENNE", DisplayName = "Makaron Penne", NormalizedName = "makaron penne", Quantity = 500, Unit = "g", IncludeInInventory = true }]);
        var products = await ((IInventoryRepository)repo).GetActiveAsync(); Assert.Single(products); Assert.Equal(800, products[0].Quantity);
    }
    [Fact]
    public async Task Cooking_never_allows_quantity_below_zero()
    {
        var (app, repo) = Create(); var chicken = Product("Kurczak", 100, "g"); await repo.SaveAsync(chicken);
        var recipe = new Recipe { Name = "Test", Ingredients = [new RecipeIngredient { ProductId = chicken.ProductId, Name = chicken.Name, Quantity = 200, Unit = "g" }] }; await ((IRecipeRepository)repo).SaveAsync(recipe);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.CookRecipeAsync(recipe.RecipeId));
        Assert.Equal(100, (await ((IInventoryRepository)repo).GetByIdAsync(chicken.ProductId))!.Quantity);
    }
    [Fact]
    public async Task Always_available_ingredient_is_available()
    {
        var (app, repo) = Create(); var salt = Product("Sól", 0, "szt."); salt.AlwaysAvailable = true; await repo.SaveAsync(salt);
        var availability = await app.CheckAvailabilityAsync(new Recipe { Ingredients = [new RecipeIngredient { ProductId = salt.ProductId, Name = "Sól", Quantity = 1, Unit = "szt." }] });
        Assert.True(availability.CanCook);
    }
    [Fact]
    public async Task Recipe_without_product_is_missing()
    {
        var (app, _) = Create(); var availability = await app.CheckAvailabilityAsync(new Recipe { Ingredients = [new RecipeIngredient { Name = "Szpinak", Quantity = 100, Unit = "g" }] });
        Assert.False(availability.CanCook); Assert.Equal("Brakuje w domu", availability.Ingredients[0].Reason);
    }
    [Fact]
    public async Task Missing_items_are_deduplicated_on_shopping_list()
    {
        var (app, repo) = Create(); var recipe = new Recipe { Name = "Zielony makaron", Ingredients = [new RecipeIngredient { Name = "Szpinak", Quantity = 100, Unit = "g" }] };
        await app.AddMissingToShoppingAsync(recipe); await app.AddMissingToShoppingAsync(recipe);
        Assert.Single(await ((IShoppingListRepository)repo).GetAllAsync());
    }
    [Theory]
    [InlineData(" MLEKO   ŁACIATE 3,2% ", "mleko laciate 32")]
    [InlineData("Pierś z kurczaka", "piers z kurczaka")]
    public void Normalizes_names(string input, string expected) => Assert.Equal(expected, PolishText.Normalize(input));

    private static Product Product(string name, decimal quantity, string unit) => new() { Name = name, NormalizedName = PolishText.Normalize(name), Quantity = quantity, Unit = unit };
    private static (KitchenApplicationService app, InMemoryRepository repo) Create()
    {
        var repo = new InMemoryRepository(new InMemoryKitchenStore());
        return (new KitchenApplicationService(repo, (IReceiptRepository)repo, (IRecipeRepository)repo, (IShoppingListRepository)repo, (IHistoryRepository)repo, (IProductMappingRepository)repo, new UnitConversionService()), repo);
    }
}
