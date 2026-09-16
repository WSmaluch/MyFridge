using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Kitchen.Core;

public enum DataProvider { Demo, GoogleSheets }
public enum FoodLocation { Lodowka, Zamrazarka, Szafka, Inne }
public enum HistoryType { Purchase, Recipe, ManualAdjustment, Waste, Delete, Restore }

public sealed class Product
{
    public string ProductId { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Category { get; set; } = "Inne spożywcze";
    public FoodLocation Location { get; set; } = FoodLocation.Szafka;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "szt.";
    public bool AlwaysAvailable { get; set; }
    public decimal? LowStockThreshold { get; set; }
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }
    public bool Active { get; set; } = true;
}

public sealed record HistoryEvent(string EventId, DateTimeOffset Timestamp, HistoryType Type, string ProductId,
    string ProductName, decimal DeltaQuantity, string Unit, string? SourceId, string Description);

public sealed class Receipt
{
    public string ReceiptId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateOnly? PurchaseDate { get; set; }
    public string? Store { get; set; }
    public decimal? Total { get; set; }
    public string Currency { get; set; } = "PLN";
    public int ItemsCount { get; set; }
    public string Status { get; set; } = "Preview";
}

public sealed class ReceiptParseResult
{
    public string? Store { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public decimal? Total { get; set; }
    public string Currency { get; set; } = "PLN";
    public List<ReceiptItem> Items { get; set; } = [];
}

public sealed class ReceiptItem
{
    public string RawName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? TotalPrice { get; set; }
    public string Category { get; set; } = "Inne spożywcze";
    public FoodLocation Location { get; set; } = FoodLocation.Szafka;
    public decimal Confidence { get; set; }
    public bool IncludeInInventory { get; set; }
}

public sealed class ProductMapping
{
    public string RawName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = "Inne spożywcze";
    public string DefaultUnit { get; set; } = "szt.";
    public FoodLocation DefaultLocation { get; set; } = FoodLocation.Szafka;
    public DateTimeOffset LastUsedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Recipe
{
    public string RecipeId { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TimeMinutes { get; set; }
    public int Servings { get; set; }
    public List<RecipeIngredient> Ingredients { get; set; } = [];
    public List<string> Steps { get; set; } = [];
    public bool IsFavorite { get; set; }
    public string Source { get; set; } = "Manual";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastCookedAt { get; set; }
    public int TimesCooked { get; set; }
}

public sealed class RecipeIngredient
{
    public string? ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public bool Optional { get; set; }
    public string? Notes { get; set; }
}

public sealed class RecipeGenerationRequest
{
    public int Servings { get; set; } = 2;
    public int? MaxTimeMinutes { get; set; }
    public string? MealType { get; set; }
    public int? MaxMissingIngredients { get; set; }
    public string? Craving { get; set; }
}

public sealed class RecipeAvailability
{
    public required Recipe Recipe { get; init; }
    public List<IngredientAvailability> Ingredients { get; init; } = [];
    public int AvailableCount => Ingredients.Count(x => x.Available || x.Ingredient.Optional);
    public int RequiredCount => Ingredients.Count(x => !x.Ingredient.Optional);
    public bool CanCook => Ingredients.Where(x => !x.Ingredient.Optional).All(x => x.Available);
}

public sealed class IngredientAvailability
{
    public required RecipeIngredient Ingredient { get; init; }
    public bool Available { get; init; }
    public string? Reason { get; init; }
}

public sealed class ShoppingItem
{
    public string ShoppingItemId { get; init; } = Guid.NewGuid().ToString("N");
    public string ProductName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public bool Checked { get; set; }
    public string Reason { get; set; } = "Dodane ręcznie";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class RecipeGenerationResult { public List<Recipe> Recipes { get; set; } = []; }

public static class PolishText
{
    public static string Location(FoodLocation location) => location switch
    {
        FoodLocation.Lodowka => "Lodówka", FoodLocation.Zamrazarka => "Zamrażarka", FoodLocation.Szafka => "Szafka", _ => "Inne"
    };
    public static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))) sb.Append(c);
        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

public static class Units
{
    public const string Gram = "g"; public const string Kilogram = "kg"; public const string Millilitre = "ml"; public const string Litre = "l"; public const string Piece = "szt.";
    public static string Canonical(string? unit) => unit?.Trim().ToLowerInvariant() switch
    {
        "szt" or "szt." or "sztuki" => Piece, "gram" or "gramy" => Gram, "kilogram" or "kilogramy" => Kilogram,
        "mililitr" or "mililitry" => Millilitre, "litr" or "litry" => Litre, _ => unit?.Trim().ToLowerInvariant() ?? string.Empty
    };
}
