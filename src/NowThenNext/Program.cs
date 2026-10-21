using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using NowThenNext;
using NowThenNext.Services;
using NowThenNext.Services.Sync;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<IImageStorageService, LocalStorageImageService>();
builder.Services.AddSingleton<IPhonicsDataService, PhonicsDataService>();
builder.Services.AddScoped<IPhonicsProgressService, PhonicsProgressService>();
builder.Services.AddScoped<ILearningCardsDataService, LearningCardsDataService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<SyncService>();
builder.Services.AddScoped<IFeelingsLogService, FeelingsLogService>();

var host = builder.Build();

try
{
    var jsRuntime = host.Services.GetRequiredService<IJSRuntime>();
    await jsRuntime.InvokeVoidAsync("indexedDb.initialize");
}
catch (JSException ex)
{
    Console.Error.WriteLine($"IndexedDB initialization failed: {ex.Message}");
}

await host.RunAsync();
