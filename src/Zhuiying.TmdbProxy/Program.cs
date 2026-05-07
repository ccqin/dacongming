using System.Text.Json;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

// 配置
var tmdbApiKey = builder.Configuration["Tmdb:ApiKey"]
    ?? Environment.GetEnvironmentVariable("TMDB_API_KEY")
    ?? throw new InvalidOperationException("Environment variable 'TMDB_API_KEY' is missing. Please set it in docker-compose or environment.");
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "tmdb_cache.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

// 数据库初始化
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

builder.Services.AddSingleton<IDbCache>(new SqliteCache(dbPath));
builder.Services.AddHttpClient("tmdb", c => { c.BaseAddress = new Uri("https://api.themoviedb.org/3/"); });
builder.Services.AddCors();

var app = builder.Build();
app.UseCors(c => c.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

var cache = app.Services.GetRequiredService<IDbCache>();

// 健康检查
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow.ToString("o"), service = "TmdbProxy", cache = cache.GetStats() }));

// 热门
app.MapGet("/api/trending", async (IHttpClientFactory f, [AsParameters] QueryParams qp) =>
{
    var key = $"trending?lang={qp.Language}&page={qp.Page}";
    var cached = await cache.Get(key);
    if (cached != null) return Results.Content(cached, "application/json");
    var r = await ProxyGet(f, $"trending/all/week?api_key={tmdbApiKey}&language={qp.Language}&page={qp.Page}");
    if (r != null) await cache.Set(key, r);
    return r != null ? Results.Content(r, "application/json") : Results.StatusCode(502);
});

// 搜索
app.MapGet("/api/search", async (string q, IHttpClientFactory f, [AsParameters] QueryParams qp) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest(new { error = "Missing query" });
    var key = $"search?q={q}&lang={qp.Language}&page={qp.Page}";
    var cached = await cache.Get(key);
    if (cached != null) return Results.Content(cached, "application/json");

    var c = f.CreateClient("tmdb");
    var enc = Uri.EscapeDataString(q);
    var (m, t) = await Task.WhenAll(
        c.GetAsync($"search/movie?api_key={tmdbApiKey}&query={enc}&language={qp.Language}&page={qp.Page}"),
        c.GetAsync($"search/tv?api_key={tmdbApiKey}&query={enc}&language={qp.Language}&page={qp.Page}")
    );
    var mj = JsonDocument.Parse(await m.Content.ReadAsStringAsync());
    var tj = JsonDocument.Parse(await t.Content.ReadAsStringAsync());
    var results = new List<Dictionary<string, object?>>();
    foreach (var i in mj.RootElement.GetProperty("results").EnumerateArray()) { var d = i.ToDict(); d["media_type"] = "movie"; results.Add(d); }
    foreach (var i in tj.RootElement.GetProperty("results").EnumerateArray()) { var d = i.ToDict(); d["media_type"] = "tv"; results.Add(d); }
    results.Sort((a, b) =>
    {
        var da = a.GetValueOrDefault("release_date")?.ToString() ?? a.GetValueOrDefault("first_air_date")?.ToString();
        var db = b.GetValueOrDefault("release_date")?.ToString() ?? b.GetValueOrDefault("first_air_date")?.ToString();
        return string.IsNullOrEmpty(da) ? 1 : string.IsNullOrEmpty(db) ? -1 : string.Compare(db, da, StringComparison.Ordinal);
    });
    var json = JsonSerializer.Serialize(new { success = true, data = results, total = results.Count, query = q });
    await cache.Set(key, json);
    return Results.Content(json, "application/json");
});

// 详情
app.MapGet("/api/detail/{type}/{id}", async (string type, int id, IHttpClientFactory f, [AsParameters] QueryParams qp) =>
{
    var key = $"detail/{type}/{id}?lang={qp.Language}";
    var cached = await cache.Get(key);
    if (cached != null) return Results.Content(cached, "application/json");
    var r = await ProxyGet(f, $"{type}/{id}?api_key={tmdbApiKey}&language={qp.Language}");
    if (r != null) await cache.Set(key, r);
    return r != null ? Results.Content(r, "application/json") : Results.StatusCode(502);
});

// 其他路由按需扩展...
app.MapFallback(() => Results.NotFound(new { error = "Not Found" }));
app.Run();

// ===== 辅助 =====
static async Task<string?> ProxyGet(IHttpClientFactory f, string url)
{
    try { var r = await f.CreateClient("tmdb").GetAsync(url); var c = await r.Content.ReadAsStringAsync(); return r.IsSuccessStatusCode ? c : null; }
    catch { return null; }
}

record QueryParams(string Language = "zh-CN", string Page = "1");

static Dictionary<string, object?> ToDict(this JsonElement el)
{
    var d = new Dictionary<string, object?>();
    foreach (var p in el.EnumerateObject()) d[p.Name] = p.Value.ValueKind switch { JsonValueKind.String => p.Value.GetString(), JsonValueKind.Number => p.Value.GetDouble(), JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Null => null, _ => p.Value.GetRawText() };
    return d;
}

interface IDbCache { Task<string?> Get(string key); Task Set(string key, string data); Dictionary<string, object> GetStats(); }
class SqliteCache(string dbPath) : IDbCache
{
    public async Task<string?> Get(string key)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT response_data FROM tmdb_cache WHERE cache_key = @key AND created_at > datetime('now', '-7 days')";
        cmd.Parameters.AddWithValue("@key", key);
        var r = await cmd.ExecuteScalarAsync();
        return r?.ToString();
    }
    public async Task Set(string key, string data)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO tmdb_cache (cache_key, response_data, created_at) VALUES (@key, @data, CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@key", key); cmd.Parameters.AddWithValue("@data", data);
        await cmd.ExecuteNonQueryAsync();
    }
    public Dictionary<string, object> GetStats()
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM tmdb_cache";
        return new Dictionary<string, object> { ["active"] = Convert.ToInt64(cmd.ExecuteScalar()) };
    }
}
