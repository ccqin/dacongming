using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Zhuiying.MainSite;
using Zhuiying.MainSite.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Hub API ����
var hubUrl = builder.Configuration["HubUrl"] ?? "https://zhuiyinghub.19856789.xyz";
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(hubUrl) });

// 注册服务
builder.Services.AddScoped<MovieService>();
builder.Services.AddScoped<SearchService>();
builder.Services.AddScoped<CacheService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<FavoritesService>();

// MudBlazor
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomEnd;
    config.SnackbarConfiguration.PreventDuplicates = true;
    config.SnackbarConfiguration.NewestOnTop = true;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 3000;
});

await builder.Build().RunAsync();
