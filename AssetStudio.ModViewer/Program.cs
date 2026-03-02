using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using AssetStudio.Extraction.Core.Services;
using AssetStudio.ModViewer;
using AssetStudio.ModViewer.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<ExtractionConfigurationService>();
builder.Services.AddScoped<UnityPackageExtractionService>();
builder.Services.AddScoped<DiagnosticsService>();
builder.Services.AddScoped<AssetExtractor>();
builder.Services.AddScoped<BasePackageLoader>();

await builder.Build().RunAsync();
