using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TamaPoke.Web;
using TamaPoke.Web.Game;
using TamaPoke.Web.Sprites;
using TamaPoke.Web.Utils;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped<Loc>();
builder.Services.AddScoped<SaveStore>();
builder.Services.AddScoped<SpriteLoader>();
builder.Services.AddScoped<PokemonDex>();

var host = builder.Build();
await Task.WhenAll(
    host.Services.GetRequiredService<Loc>().InitializeAsync(),
    host.Services.GetRequiredService<PokemonDex>().InitializeAsync());
await host.RunAsync();
