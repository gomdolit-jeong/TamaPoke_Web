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
builder.Services.AddScoped<CloudSave>();
builder.Services.AddScoped<SoundPlayer>();
builder.Services.AddScoped<SpriteLoader>();
builder.Services.AddScoped<PokemonDex>();
builder.Services.AddScoped<SkillDex>();
builder.Services.AddScoped<MegaDex>();

var host = builder.Build();
await Task.WhenAll(
    host.Services.GetRequiredService<Loc>().InitializeAsync(),
    host.Services.GetRequiredService<PokemonDex>().InitializeAsync(),
    host.Services.GetRequiredService<SkillDex>().InitializeAsync(),
    host.Services.GetRequiredService<MegaDex>().InitializeAsync());
await host.RunAsync();
