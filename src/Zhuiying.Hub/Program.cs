// v3 rebuild bust cache
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

// ====== 配置 ======
var tmdbApiKey = builder.Configuration["Tmdb:ApiKey"]
    ?? Environment.GetEnvironmentVariable("TMDB_API_KEY")
    ?? throw new InvalidOperationException("Environment variable 'TMDB_API_KEY' is missing.");
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "tmdb_cache.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

// ====== 数据库初始化 ======
await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
{
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS tmdb_cache (
            cache_key TEXT PRIMARY KEY,
            response_data TEXT NOT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        );
        CREATE INDEX IF NOT EXISTS idx_created_at ON tmdb_cache(created_at);";
    await cmd.ExecuteNonQueryAsync();
}

// ====== 服务注册 ======
builder.Services.AddSingleton<IDbCache>(new SqliteCache(dbPath));
builder.Services.AddHttpClient("tmdb", c => { c.BaseAddress = new Uri("https://api.themoviedb.org/3/"); });
builder.Services.AddHttpClient();
builder.Services.AddCors();
builder.Services.AddSingleton<HubService>();

var app = builder.Build();
app.UseCors(c => c.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

var cache = app.Services.GetRequiredService<IDbCache>();
var hub = app.Services.GetRequiredService<HubService>();

// ====== 健康检查 ======
app.MapGet("/health", () => Results.Ok(new {
    status = "ok",
    time = DateTime.UtcNow.ToString("o"),
    service = "Zhuiying.Hub",
    tmdbCache = cache.GetStats(),
    hubSources = hub.GetSources().Count
}));

// ====== 🎬 TMDB 模块 (/api/tmdb) ======
app.MapGet("/api/tmdb/trending", async (IHttpClientFactory f, [AsParameters] QueryParams qp) =>
{
    var key = $"trending?lang={qp.Language}&page={qp.Page}";
    var cached = await cache.Get(key);
    if (cached != null) return Results.Content(cached, "application/json");
    var r = await ProxyGet(f, $"trending/all/week?api_key={tmdbApiKey}&language={qp.Language}&page={qp.Page}");
    if (r != null) await cache.Set(key, r);
    return r != null ? Results.Content(r, "application/json") : Results.StatusCode(502);
});

app.MapGet("/api/tmdb/search", async (string q, IHttpClientFactory f, [AsParameters] QueryParams qp) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest(new { error = "Missing query" });
    var key = $"search?q={q}&lang={qp.Language}&page={qp.Page}";
    var cached = await cache.Get(key);
    if (cached != null) return Results.Content(cached, "application/json");
    var client = f.CreateClient("tmdb");
    var enc = Uri.EscapeDataString(q);
    var responses = await Task.WhenAll(
        client.GetAsync($"search/movie?api_key={tmdbApiKey}&query={enc}&language={qp.Language}&page={qp.Page}"),
        client.GetAsync($"search/tv?api_key={tmdbApiKey}&query={enc}&language={qp.Language}&page={qp.Page}")
    );
    var movieResponse = responses[0];
    var tvResponse = responses[1];
    var mj = JsonDocument.Parse(await movieResponse.Content.ReadAsStringAsync());
    var tj = JsonDocument.Parse(await tvResponse.Content.ReadAsStringAsync());
    var merged = new {
        page = mj.RootElement.GetProperty("page").GetInt32(),
        total_pages = Math.Max(mj.RootElement.GetProperty("total_pages").GetInt32(), tj.RootElement.GetProperty("total_pages").GetInt32()),
        total_results = mj.RootElement.GetProperty("total_results").GetInt32() + tj.RootElement.GetProperty("total_results").GetInt32(),
        results = mj.RootElement.GetProperty("results").EnumerateArray().Concat(tj.RootElement.GetProperty("results").EnumerateArray()).ToArray()
    };
    var json = JsonSerializer.Serialize(merged);
    if (movieResponse.IsSuccessStatusCode) await cache.Set(key, json);
    return Results.Content(json, "application/json");
});

app.MapGet("/api/tmdb/discover", async (string type, IHttpClientFactory f, [AsParameters] QueryParams qp) =>
{
    var path = $"discover/{type}?api_key={tmdbApiKey}&language={qp.Language}&page={qp.Page}";
    return await ProxyGet(f, path) is { } j ? Results.Content(j, "application/json") : Results.StatusCode(502);
});

app.MapGet("/api/tmdb/movie/{id}", async (int id, IHttpClientFactory f, [AsParameters] QueryParams qp) =>
{
    var key = $"movie/{id}?lang={qp.Language}";
    var cached = await cache.Get(key);
    if (cached != null) return Results.Content(cached, "application/json");
    var r = await ProxyGet(f, $"movie/{id}?api_key={tmdbApiKey}&language={qp.Language}");
    if (r != null) await cache.Set(key, r);
    return r != null ? Results.Content(r, "application/json") : Results.StatusCode(502);
});

app.MapGet("/api/tmdb/tv/{id}", async (int id, IHttpClientFactory f, [AsParameters] QueryParams qp) =>
{
    var key = $"tv/{id}?lang={qp.Language}";
    var cached = await cache.Get(key);
    if (cached != null) return Results.Content(cached, "application/json");
    var r = await ProxyGet(f, $"tv/{id}?api_key={tmdbApiKey}&language={qp.Language}");
    if (r != null) await cache.Set(key, r);
    return r != null ? Results.Content(r, "application/json") : Results.StatusCode(502);
});

app.MapGet("/api/tmdb/movie/{id}/credits", async (int id, IHttpClientFactory f) =>
    await ProxyGet(f, $"movie/{id}/credits?api_key={tmdbApiKey}") is { } j ? Results.Content(j, "application/json") : Results.StatusCode(502));

app.MapGet("/api/tmdb/tv/{id}/credits", async (int id, IHttpClientFactory f) =>
    await ProxyGet(f, $"tv/{id}/credits?api_key={tmdbApiKey}") is { } j ? Results.Content(j, "application/json") : Results.StatusCode(502));

// ====== 🔍 聚合搜索模块 (/api/hub) ======
app.MapGet("/api/hub/search", async (string q, HubService hub) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest(new { error = "Missing 'q' parameter" });
    var result = await hub.SearchAsync(q);
    return Results.Ok(result);
});

// ====== ⚙️ 管理后台 (/api/admin) ======
app.MapGet("/api/admin/sources", (HubService hub) => Results.Ok(hub.GetSources()));
app.MapPost("/api/admin/sources", async (SourceConfig config, HubService hub) => {
    await hub.AddOrUpdateAsync(config);
    return Results.Ok(new { message = "Source updated", name = config.Name });
});
app.MapDelete("/api/admin/sources/{name}", async (string name, HubService hub) => {
    await hub.RemoveAsync(name);
    return Results.Ok(new { message = "Source removed", name });
});

app.Run();

// ====== 辅助方法 ======
async Task<string?> ProxyGet(IHttpClientFactory f, string path)
{
    var c = f.CreateClient("tmdb");
    var r = await c.GetAsync(path);
    return r.IsSuccessStatusCode ? await r.Content.ReadAsStringAsync() : null;
}

// ====== 类型声明 (Top-level 规范: 必须放最后) ======
public interface IDbCache
{
    Task<string?> Get(string key);
    Task Set(string key, string value);
    (int Total, string? Latest) GetStats();
}

public class SqliteCache(string dbPath) : IDbCache
{
    public async Task<string?> Get(string key)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT response_data FROM tmdb_cache WHERE cache_key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString();
    }
    public async Task Set(string key, string value)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO tmdb_cache (cache_key, response_data) VALUES (@key, @value)";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        await cmd.ExecuteNonQueryAsync();
    }
    public (int Total, string? Latest) GetStats()
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*), MAX(created_at) FROM tmdb_cache";
            using var reader = cmd.ExecuteReader();
            if (reader.Read()) return (reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1));
        } catch {}
        return (0, null);
    }
}

public record QueryParams([property: JsonPropertyName("language")] string Language = "zh-CN", [property: JsonPropertyName("page")] int Page = 1);

public record SearchResponse(string Query, List<SourceResult> Results);
public record SourceResult(string Name, List<System.Text.Json.Nodes.JsonNode?> Items);
public class SourceConfig
{
    public string Name { get; set; } = string.Empty;
    public string ApiUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Type { get; set; } = string.Empty;
}

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
        if (existing != null) { Sources.Remove(existing); SaveConfig(); }
    }

    public async Task<SearchResponse> SearchAsync(string query)
    {
        var tasks = Sources.Where(s => s.Enabled).Select(async s =>
        {
            try
            {
                var client = _factory.CreateClient();
                var res = await client.PostAsJsonAsync(s.ApiUrl, new { query });
                var raw = await res.Content.ReadAsStringAsync();
                if (!res.IsSuccessStatusCode) return new SourceResult(s.Name, new List<System.Text.Json.Nodes.JsonNode?> { System.Text.Json.Nodes.JsonNode.Parse("{\"error\":\"HTTP " + (int)res.StatusCode + "\"}") });
                
                List<System.Text.Json.Nodes.JsonNode?> items;
                try
                {
                    var node = System.Text.Json.Nodes.JsonNode.Parse(raw);
                    if (node is System.Text.Json.Nodes.JsonArray arr) items = arr.ToList();
                    else if (node is System.Text.Json.Nodes.JsonObject obj)
                    {
                        // PanSou returns: { code, message, data: { total, merged_by_type: { type: [items...] } } }
                        // Flatten merged_by_type into a single array with type info
                        var dataNode = obj["data"] ?? obj["results"] ?? obj["items"];
                        if (dataNode is System.Text.Json.Nodes.JsonObject dataObj)
                        {
                            var mergedByType = dataObj["merged_by_type"];
                            if (mergedByType is System.Text.Json.Nodes.JsonObject groups)
                            {
                                items = new List<System.Text.Json.Nodes.JsonNode?>();
                                foreach (var prop in groups)
                                {
                                    if (prop.Value is System.Text.Json.Nodes.JsonArray groupItems)
                                    {
                                        foreach (var item in groupItems)
                                        {
                                            if (item is System.Text.Json.Nodes.JsonObject objItem)
                                            {
                                                objItem["cloud_type"] = prop.Key;
                                                items.Add(objItem);
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Fallback: try other common structures
                                var list = dataObj["list"] ?? dataObj["resources"] ?? dataObj["items"];
                                items = list is System.Text.Json.Nodes.JsonArray a ? a.ToList() : new List<System.Text.Json.Nodes.JsonNode?> { dataNode };
                            }
                        }
                        else if (dataNode is System.Text.Json.Nodes.JsonArray arr2) items = arr2.ToList();
                        else items = new List<System.Text.Json.Nodes.JsonNode?> { dataNode };
                    }
                    else items = new List<System.Text.Json.Nodes.JsonNode?>();
                }
                catch { items = new List<System.Text.Json.Nodes.JsonNode?> { System.Text.Json.Nodes.JsonNode.Parse("{\"raw\":" + JsonSerializer.Serialize(raw) + "}") }; }
                return new SourceResult(s.Name, items);
            }
            catch (Exception ex) { return new SourceResult(s.Name, new List<System.Text.Json.Nodes.JsonNode?> { System.Text.Json.Nodes.JsonNode.Parse("{\"error\":\"" + ex.Message.Replace("\"", "\\\"") + "\"}") }); }
        });
        var results = await Task.WhenAll(tasks);
        return new SearchResponse(query, results.ToList());
    }
}
