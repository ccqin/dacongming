using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
builder.Services.AddCors();
builder.Services.AddSingleton<HubService>();

var app = builder.Build();
app.UseCors(b => b.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

var hub = app.Services.GetRequiredService<HubService>();

// 1. 搜索接口
app.MapGet("/api/v1/search", async (string q, HubService hub) => {
    if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest(new { error = "Missing 'q' parameter" });
    var result = await hub.SearchAsync(q);
    return Results.Ok(result);
});

// 2. 管理接口
app.MapGet("/api/v1/admin/sources", (HubService hub) => Results.Ok(hub.GetSources()));
app.MapPost("/api/v1/admin/sources", async (SourceConfig config, HubService hub) => {
    await hub.AddOrUpdateAsync(config);
    return Results.Ok(new { message = "Source updated", name = config.Name });
});
app.MapDelete("/api/v1/admin/sources/{name}", async (string name, HubService hub) => {
    await hub.RemoveAsync(name);
    return Results.Ok(new { message = "Source removed", name });
});

app.Run();

public class HubService
{
    private readonly IHttpClientFactory _factory;
    private readonly string _configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
    public List<SourceConfig> Sources { get; private set; } = new();

    public HubService(IHttpClientFactory factory)
    {
        _factory = factory;
        LoadConfig();
    }

    public HubService() : this(CreateClientFactory()) { }

    private static IHttpClientFactory CreateClientFactory()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    private void LoadConfig()
    {
        if (File.Exists(_configPath))
        {
            var json = File.ReadAllText(_configPath);
            Sources = JsonSerializer.Deserialize<List<SourceConfig>>(json) ?? new List<SourceConfig>();
        }
        if (!Sources.Any())
        {
            Sources.Add(new SourceConfig { Name = "PanSou", ApiUrl = "http://zhuiying-pansou:8888/api/search", Enabled = true, Type = "pansou" });
            SaveConfig();
        }
    }

    private void SaveConfig() => File.WriteAllText(_configPath, JsonSerializer.Serialize(Sources, new JsonSerializerOptions { WriteIndented = true }));

    public List<SourceConfig> GetSources() => Sources;

    public async Task AddOrUpdateAsync(SourceConfig config)
    {
        var existing = Sources.FirstOrDefault(s => s.Name == config.Name);
        if (existing != null)
        {
            existing.ApiUrl = config.ApiUrl;
            existing.Enabled = config.Enabled;
            existing.Type = config.Type;
        }
        else Sources.Add(config);
        SaveConfig();
    }

    public async Task RemoveAsync(string name)
    {
        var existing = Sources.FirstOrDefault(s => s.Name == name);
        if (existing != null) Sources.Remove(existing);
        SaveConfig();
    }

    public async Task<SearchResponse> SearchAsync(string q)
    {
        LoadConfig(); 
        var tasks = Sources.Where(s => s.Enabled).Select(s => QuerySource(s, q));
        var results = await Task.WhenAll(tasks);
        return new SearchResponse(q, results.ToList());
    }

    private async Task<SourceResult> QuerySource(SourceConfig s, string q)
    {
        try
        {
            var client = _factory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            var url = s.ApiUrl.Contains("?") ? $"{s.ApiUrl}&q={Uri.EscapeDataString(q)}" : $"{s.ApiUrl}?q={Uri.EscapeDataString(q)}";
            var resp = await client.GetStringAsync(url);
            var doc = JsonDocument.Parse(resp);
            var normalized = new List<SourceItem>();

            if (s.Type == "pansou")
            {
                if (doc.RootElement.TryGetProperty("data", out var data) && 
                    data.TryGetProperty("merged_by_type", out var types))
                {
                    foreach (var prop in types.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in prop.Value.EnumerateArray())
                            {
                                var u = item.TryGetProperty("url", out var uProp) ? uProp.GetString() : "";
                                var n = item.TryGetProperty("note", out var nProp) ? nProp.GetString() : "";
                                normalized.Add(new SourceItem(prop.Name, u ?? "", n, s.Name));
                            }
                        }
                    }
                }
            }

            return new SourceResult(s.Name, normalized, null);
        }
        catch (Exception ex)
        {
            return new SourceResult(s.Name, new List<SourceItem>(), ex.Message);
        }
    }
}

public class SourceConfig { public string Name { get; set; } = ""; public string ApiUrl { get; set; } = ""; public bool Enabled { get; set; } public string Type { get; set; } = ""; }
public record SearchResponse(string Query, List<SourceResult> Results);
public record SourceResult(string Name, List<SourceItem> Items, string? Error);
public record SourceItem(string Type, string Url, string? Note, string Source);
