using System.Globalization;
using System.Text.Json;
using Kitchen.Core;

namespace Kitchen.Infrastructure;

public sealed class GoogleSheetsInitializer(GoogleSheetsRestClient client)
{
    internal static readonly Dictionary<string, string[]> Schema = new()
    {
        ["Produkty"]=["ProductId","Name","NormalizedName","Category","Location","Quantity","Unit","AlwaysAvailable","LowStockThreshold","LastUpdated","Notes","Active"],
        ["Historia"]=["EventId","Timestamp","Type","ProductId","ProductName","DeltaQuantity","Unit","SourceId","Description"],
        ["Paragony"]=["ReceiptId","CreatedAt","PurchaseDate","Store","Total","Currency","ItemsCount","Status"],
        ["Przepisy"]=["RecipeId","Name","Description","TimeMinutes","Servings","IngredientsJson","StepsJson","IsFavorite","Source","CreatedAt","LastCookedAt","TimesCooked"],
        ["Zakupy"]=["ShoppingItemId","ProductName","NormalizedName","Quantity","Unit","Checked","Reason","CreatedAt"],
        ["Mapowania"]=["RawName","NormalizedName","DisplayName","Category","DefaultUnit","DefaultLocation","LastUsedAt"],
        ["App"]=["Key","Value"]
    };
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        using var meta = await client.GetAsync($"v4/spreadsheets/{Uri.EscapeDataString(client.SpreadsheetId)}?fields=sheets.properties", ct);
        var known = meta.RootElement.GetProperty("sheets").EnumerateArray().Select(x => x.GetProperty("properties").GetProperty("title").GetString()).ToHashSet(StringComparer.Ordinal);
        var requests = Schema.Keys.Where(x => !known.Contains(x)).Select(x => new { addSheet = new { properties = new { title = x } } }).ToArray();
        if (requests.Length > 0) using (await client.BatchUpdateAsync(new { requests }, ct)) { }
        foreach (var (sheet, headers) in Schema)
        {
            using var row = await client.GetAsync($"v4/spreadsheets/{Uri.EscapeDataString(client.SpreadsheetId)}/values/{Uri.EscapeDataString(sheet + "!1:1")}", ct);
            var exists = row.RootElement.TryGetProperty("values", out var values) && values.GetArrayLength() > 0 && values[0].GetArrayLength() >= headers.Length;
            if (!exists) using (await client.UpdateValuesAsync($"{sheet}!A1", new[] { headers }, ct)) { }
        }
    }
}

public sealed class GoogleSheetsRepository(GoogleSheetsRestClient client) : IInventoryRepository, IReceiptRepository, IRecipeRepository, IShoppingListRepository, IHistoryRepository, IProductMappingRepository
{
    public async Task<IReadOnlyList<Product>> GetActiveAsync(CancellationToken ct = default) => (await Rows("Produkty",ct)).Skip(1).Where(x=>Bool(x,11,true)).Select(Product).Where(x=>x.Active).ToList();
    public async Task<Product?> GetByIdAsync(string id,CancellationToken ct=default)=>(await GetActiveAsync(ct)).FirstOrDefault(x=>x.ProductId==id);
    public Task SaveAsync(Product x,CancellationToken ct=default)=>Upsert("Produkty",x.ProductId,[x.ProductId,x.Name,x.NormalizedName,x.Category,PolishText.Location(x.Location),D(x.Quantity),x.Unit,B(x.AlwaysAvailable),x.LowStockThreshold is null?"":D(x.LowStockThreshold.Value),x.LastUpdated.ToString("O"),x.Notes??"",B(x.Active)],ct);
    public Task SaveManyAsync(IEnumerable<Product> products,CancellationToken ct=default)=>Task.WhenAll(products.Select(x=>SaveAsync(x,ct)));
    async Task<Receipt?> IReceiptRepository.GetAsync(string id,CancellationToken ct)=>(await Rows("Paragony",ct)).Skip(1).FirstOrDefault(x=>S(x,0)==id) is { } x?new Receipt{ReceiptId=S(x,0),CreatedAt=Date(S(x,1)),PurchaseDate=DateOnly.TryParse(S(x,2),out var day)?day:null,Store=S(x,3),Total=Decimal(x,4),Currency=S(x,5),ItemsCount=Int(x,6),Status=S(x,7)}:null;
    public Task SaveAsync(Receipt x,CancellationToken ct=default)=>Upsert("Paragony",x.ReceiptId,[x.ReceiptId,x.CreatedAt.ToString("O"),x.PurchaseDate?.ToString("O")??"",x.Store??"",x.Total is null?"":D(x.Total.Value),x.Currency,x.ItemsCount.ToString(),x.Status],ct);
    async Task<IReadOnlyList<Recipe>> IRecipeRepository.GetAllAsync(CancellationToken ct)=>(await Rows("Przepisy",ct)).Skip(1).Where(x=>x.Length>0).Select(Recipe).ToList();
    async Task<Recipe?> IRecipeRepository.GetAsync(string id,CancellationToken ct)=>(await ((IRecipeRepository)this).GetAllAsync(ct)).FirstOrDefault(x=>x.RecipeId==id);
    public Task SaveAsync(Recipe x,CancellationToken ct=default)=>Upsert("Przepisy",x.RecipeId,[x.RecipeId,x.Name,x.Description,x.TimeMinutes.ToString(),x.Servings.ToString(),JsonSerializer.Serialize(x.Ingredients),JsonSerializer.Serialize(x.Steps),B(x.IsFavorite),x.Source,x.CreatedAt.ToString("O"),x.LastCookedAt?.ToString("O")??"",x.TimesCooked.ToString()],ct);
    async Task<IReadOnlyList<ShoppingItem>> IShoppingListRepository.GetAllAsync(CancellationToken ct)=>(await Rows("Zakupy",ct)).Skip(1).Where(x=>x.Length>0).Select(x=>new ShoppingItem{ShoppingItemId=S(x,0),ProductName=S(x,1),NormalizedName=S(x,2),Quantity=Decimal(x,3),Unit=S(x,4),Checked=Bool(x,5),Reason=S(x,6),CreatedAt=Date(S(x,7))}).ToList();
    public Task SaveAsync(ShoppingItem x,CancellationToken ct=default)=>Upsert("Zakupy",x.ShoppingItemId,[x.ShoppingItemId,x.ProductName,x.NormalizedName,x.Quantity is null?"":D(x.Quantity.Value),x.Unit??"",B(x.Checked),x.Reason,x.CreatedAt.ToString("O")],ct);
    public async Task DeleteAsync(string id,CancellationToken ct=default){var rows=await Rows("Zakupy",ct);var index=rows.Select((x,i)=>(x,i)).FirstOrDefault(v=>S(v.x,0)==id).i;if(index<=0)return;using var _=await client.ClearValuesAsync($"Zakupy!A{index+1}:H{index+1}",ct);}
    public async Task<IReadOnlyList<HistoryEvent>> GetRecentAsync(int take,CancellationToken ct=default)=>(await Rows("Historia",ct)).Skip(1).Where(x=>x.Length>0).Select(x=>new HistoryEvent(S(x,0),Date(S(x,1)),Enum.TryParse<HistoryType>(S(x,2),out var type)?type:HistoryType.ManualAdjustment,S(x,3),S(x,4),Decimal(x,5)??0,S(x,6),S(x,7),S(x,8))).OrderByDescending(x=>x.Timestamp).Take(take).ToList();
    public async Task AppendAsync(IEnumerable<HistoryEvent> events,CancellationToken ct=default){var values=events.Select(x=>new[]{x.EventId,x.Timestamp.ToString("O"),x.Type.ToString(),x.ProductId,x.ProductName,D(x.DeltaQuantity),x.Unit,x.SourceId??"",x.Description}).ToArray();if(values.Length==0)return;using var _=await client.AppendValuesAsync("Historia!A:I",values,ct);}
    async Task<ProductMapping?> IProductMappingRepository.GetAsync(string raw,CancellationToken ct)=>(await Rows("Mapowania",ct)).Skip(1).FirstOrDefault(x=>S(x,0)==raw) is { } x?new ProductMapping{RawName=S(x,0),NormalizedName=S(x,1),DisplayName=S(x,2),Category=S(x,3),DefaultUnit=S(x,4),DefaultLocation=Location(S(x,5)),LastUsedAt=Date(S(x,6))}:null;
    public Task SaveAsync(ProductMapping x,CancellationToken ct=default)=>Upsert("Mapowania",x.RawName,[x.RawName,x.NormalizedName,x.DisplayName,x.Category,x.DefaultUnit,PolishText.Location(x.DefaultLocation),x.LastUsedAt.ToString("O")],ct);
    private async Task<List<string[]>> Rows(string sheet,CancellationToken ct){using var doc=await client.GetAsync($"v4/spreadsheets/{Uri.EscapeDataString(client.SpreadsheetId)}/values/{Uri.EscapeDataString(sheet)}",ct);return doc.RootElement.TryGetProperty("values",out var values)?values.EnumerateArray().Select(r=>r.EnumerateArray().Select(c=>c.GetString()??"").ToArray()).ToList():[];}
    private async Task Upsert(string sheet,string id,string[] values,CancellationToken ct){var rows=await Rows(sheet,ct);var index=rows.Select((x,i)=>(x,i)).FirstOrDefault(v=>S(v.x,0)==id).i;var range=index>0?$"{sheet}!A{index+1}":$"{sheet}!A{rows.Count+1}";using var _=await client.UpdateValuesAsync(range,new[]{values},ct);}
    private static Product Product(string[] x)=>new(){ProductId=S(x,0),Name=S(x,1),NormalizedName=S(x,2),Category=S(x,3),Location=Location(S(x,4)),Quantity=Decimal(x,5)??0,Unit=S(x,6),AlwaysAvailable=Bool(x,7),LowStockThreshold=Decimal(x,8),LastUpdated=Date(S(x,9)),Notes=S(x,10),Active=Bool(x,11,true)};
    private static Recipe Recipe(string[] x)=>new(){RecipeId=S(x,0),Name=S(x,1),Description=S(x,2),TimeMinutes=Int(x,3),Servings=Int(x,4),Ingredients=JsonSerializer.Deserialize<List<RecipeIngredient>>(S(x,5))??[],Steps=JsonSerializer.Deserialize<List<string>>(S(x,6))??[],IsFavorite=Bool(x,7),Source=S(x,8),CreatedAt=Date(S(x,9)),LastCookedAt=string.IsNullOrEmpty(S(x,10))?null:Date(S(x,10)),TimesCooked=Int(x,11)};
    private static string S(string[] x,int i)=>i<x.Length?x[i]:"";private static decimal? Decimal(string[] x,int i)=>decimal.TryParse(S(x,i),NumberStyles.Any,CultureInfo.InvariantCulture,out var d)?d:null;private static int Int(string[] x,int i)=>int.TryParse(S(x,i),out var d)?d:0;private static bool Bool(string[] x,int i,bool fallback=false)=>string.IsNullOrWhiteSpace(S(x,i))?fallback:bool.TryParse(S(x,i),out var d)&&d;private static DateTimeOffset Date(string x)=>DateTimeOffset.TryParse(x,out var d)?d:DateTimeOffset.UtcNow;private static string D(decimal x)=>x.ToString(CultureInfo.InvariantCulture);private static string B(bool x)=>x?"TRUE":"FALSE";private static FoodLocation Location(string x)=>x switch{"Lodówka"=>FoodLocation.Lodowka,"Zamrażarka"=>FoodLocation.Zamrazarka,"Szafka"=>FoodLocation.Szafka,_=>FoodLocation.Inne};
}
