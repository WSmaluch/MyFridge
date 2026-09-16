using System.Globalization;
using Kitchen.Core;
using Kitchen.Infrastructure;
using Kitchen.Web.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var polish = new CultureInfo("pl-PL");
CultureInfo.DefaultThreadCurrentCulture = polish;
CultureInfo.DefaultThreadCurrentUICulture = polish;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<ILocalSettingsService, LocalSettingsService>();
builder.Services.AddScoped<IAppModeStore, AppModeStore>();
builder.Services.AddScoped<IGoogleAuthService, GoogleAuthService>();
builder.Services.AddScoped<GoogleSheetsRestClient>();
builder.Services.AddScoped<GoogleSheetsInitializer>();
builder.Services.AddScoped<GoogleSheetsRepository>();
builder.Services.AddScoped<InMemoryKitchenStore>();
builder.Services.AddScoped<InMemoryRepository>();
builder.Services.AddScoped<SwitchingRepository>();
builder.Services.AddScoped<IInventoryRepository>(x => x.GetRequiredService<SwitchingRepository>());
builder.Services.AddScoped<IReceiptRepository>(x => x.GetRequiredService<SwitchingRepository>());
builder.Services.AddScoped<IRecipeRepository>(x => x.GetRequiredService<SwitchingRepository>());
builder.Services.AddScoped<IShoppingListRepository>(x => x.GetRequiredService<SwitchingRepository>());
builder.Services.AddScoped<IHistoryRepository>(x => x.GetRequiredService<SwitchingRepository>());
builder.Services.AddScoped<IProductMappingRepository>(x => x.GetRequiredService<SwitchingRepository>());
builder.Services.AddScoped<IUnitConversionService, UnitConversionService>();
builder.Services.AddScoped<KitchenApplicationService>();
builder.Services.AddScoped<MockGeminiService>();
builder.Services.AddScoped<BrowserGeminiService>();
builder.Services.AddScoped<SwitchingGeminiService>();
builder.Services.AddScoped<IGeminiService>(x => x.GetRequiredService<SwitchingGeminiService>());

await builder.Build().RunAsync();
