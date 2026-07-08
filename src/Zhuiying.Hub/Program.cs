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
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            expires_at DATETIME NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_expires_at ON tmdb_cache(expires_at);";
    await cmd.ExecuteNonQueryAsync();
    // 修复旧数据：如果旧表没有 expires_at，这里会失败，但既然重建了缓存机制，可以直接忽略或清理
    // 为安全起见，强制删除旧表重建（因为旧缓存没有 TTL，数据已过时）
    cmd.CommandText = "DELETE FROM tmdb_cache"; 
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
    if (r != null) await cache.Set(key, r, 3600); // 1 hour
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
    if (movieResponse.IsSuccessStatusCode) await cache.Set(key, json, 600); // 10 mins
    return Results.Content(json, "application/json");
});

app.MapGet("/api/tmdb/discover", async (string type, IHttpClientFactory f, [AsParameters] QueryParams qp) =>
{
    var key = $"discover/{type}?lang={qp.Language}&page={qp.Page}";
    var cached = await cache.Get(key);
    if (cached != null) return Results.Content(cached, "application/json");
    var path = $"discover/{type}?api_key={tmdbApiKey}&language={qp.Language}&page={qp.Page}";
    var r = await ProxyGet(f, path);
    if (r != null) await cache.Set(key, r, 3600); // 1 hour
    return r != null ? Results.Content(r, "application/json") : Results.StatusCode(502);
});

app.MapGet("/api/tmdb/movie/{id}", async (int id, IHttpClientFactory f, [AsParameters] QueryParams qp) =>
{
    var key = $"movie/{id}?lang={qp.Language}";
    var cached = await cache.Get(key);
    if (cached != null) return Results.Content(cached, "application/json");
    var r = await ProxyGet(f, $"movie/{id}?api_key={tmdbApiKey}&language={qp.Language}");
    if (r != null) await cache.Set(key, r, 86400); // 24 hours
    return r != null ? Results.Content(r, "application/json") : Results.StatusCode(502);
});

app.MapGet("/api/tmdb/tv/{id}", async (int id, IHttpClientFactory f, [AsParameters] QueryParams qp) =>
{
    var key = $"tv/{id}?lang={qp.Language}";
    var cached = await cache.Get(key);
    if (cached != null) return Results.Content(cached, "application/json");
    var r = await ProxyGet(f, $"tv/{id}?api_key={tmdbApiKey}&language={qp.Language}");
    if (r != null) await cache.Set(key, r, 86400); // 24 hours
    return r != null ? Results.Content(r, "application/json") : Results.StatusCode(502);
});

app.MapGet("/api/tmdb/movie/{id}/credits", async (int id, IHttpClientFactory f) =>
    await ProxyGet(f, $"movie/{id}/credits?api_key={tmdbApiKey}") is { } j ? Results.Content(j, "application/json") : Results.StatusCode(502));

app.MapGet("/api/tmdb/tv/{id}/credits", async (int id, IHttpClientFactory f) =>
    await ProxyGet(f, $"tv/{id}/credits?api_key={tmdbApiKey}") is { } j ? Results.Content(j, "application/json") : Results.StatusCode(502));

// ====== 🎬 统一影视 API (API.md v1.0.0 规范) ======

// GET /api/movie/trending - 热门影视
app.MapGet("/api/movie/trending", async (string? type, string? region, int page, IHttpClientFactory f, IDbCache cache) =>
{
    var mediaType = type ?? "movie";
    var key = $"trending?type={mediaType}&lang=zh-CN&page={page}&region={region ?? ""}";
    var cached = await cache.Get(key);
    if (cached != null) return Results.Content(cached, "application/json");
    
    // Fetch from TMDB trending
    var client = f.CreateClient("tmdb");
    var tmdbResp = await client.GetAsync($"trending/{(mediaType == "tv" ? "tv" : "movie")}/week?api_key={tmdbApiKey}&language=zh-CN&page={page}");
    if (!tmdbResp.IsSuccessStatusCode) return Results.StatusCode(502);
    var raw = await tmdbResp.Content.ReadAsStringAsync();
    
    using var doc = JsonDocument.Parse(raw);
    var results = doc.RootElement.GetProperty("results").EnumerateArray()
        .Select(e => TmdbToDto(e, mediaType))
        .ToList();
    
    var response = ApiResponse(results);
    if (results.Count > 0) await cache.Set(key, response, 1800); // 30 min
    return Results.Content(response, "application/json");
});

// GET /api/movie/latest - 最新影视
app.MapGet("/api/movie/latest", async (string? type, int page, IHttpClientFactory f, IDbCache cache) =>
{
    var mediaType = type ?? "movie";
    var key = $"latest?lang=zh-CN&page={page}&type={mediaType}";
    var cached = await cache.Get(key);
    if (cached != null) return Results.Content(cached, "application/json");
    
    var client = f.CreateClient("tmdb");
    var path = mediaType == "tv" 
        ? $"tv/on_the_air?api_key={tmdbApiKey}&language=zh-CN&page={page}"
        : $"movie/now_playing?api_key={tmdbApiKey}&language=zh-CN&page={page}";
    
    var tmdbResp = await client.GetAsync(path);
    if (!tmdbResp.IsSuccessStatusCode) return Results.StatusCode(502);
    var raw = await tmdbResp.Content.ReadAsStringAsync();
    
    using var doc2 = JsonDocument.Parse(raw);
    var results2 = doc2.RootElement.GetProperty("results").EnumerateArray()
        .Select(e => TmdbToDto(e, mediaType))
        .ToList();
    
    var response2 = ApiResponse(results2);
    if (results2.Count > 0) await cache.Set(key, response2, 1800);
    return Results.Content(response2, "application/json");
});

// GET /api/movie/{id} - 影视详情（包含演员、相似推荐、视频）
app.MapGet("/api/movie/{id:int}", async (int id, string? type, IHttpClientFactory f, IDbCache cache) =>
{
    var mediaType = type ?? "movie";
    var key = $"detail/{mediaType}/{id}";
    var cached = await cache.Get(key);
    if (cached != null) return Results.Content(cached, "application/json");
    
    var client = f.CreateClient("tmdb");
    
    // 并行请求详情、演员、相似推荐、视频
    var detailTask = client.GetAsync($"{mediaType}/{id}?api_key={tmdbApiKey}&language=zh-CN");
    var creditsTask = client.GetAsync($"{mediaType}/{id}/credits?api_key={tmdbApiKey}&language=zh-CN");
    var similarTask = client.GetAsync($"{mediaType}/{id}/similar?api_key={tmdbApiKey}&language=zh-CN&page=1");
    var videosTask = client.GetAsync($"{mediaType}/{id}/videos?api_key={tmdbApiKey}&language=zh-CN");
    
    await Task.WhenAll(detailTask, creditsTask, similarTask, videosTask);
    
    if (!detailTask.Result.IsSuccessStatusCode) return Results.StatusCode(502);
    
    var detailRaw = await detailTask.Result.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(detailRaw);
    var el = doc.RootElement;
    
    // 解析基本信息
    var detail = new Dictionary<string, object?>
    {
        ["tmdbId"] = id,
        ["title"] = el.TryGetProperty("title", out var t) ? t.GetString() : el.TryGetProperty("name", out var n) ? n.GetString() : "",
        ["originalTitle"] = el.TryGetProperty("original_title", out var ot) ? ot.GetString() : el.TryGetProperty("original_name", out var on) ? on.GetString() : "",
        ["overview"] = el.TryGetProperty("overview", out var ov) ? ov.GetString() : "",
        ["posterPath"] = el.TryGetProperty("poster_path", out var pp) ? pp.GetString() : "",
        ["backdropPath"] = el.TryGetProperty("backdrop_path", out var bp) ? bp.GetString() : "",
        ["tmdbVoteAverage"] = el.TryGetProperty("vote_average", out var va) ? va.GetDouble() : 0,
        ["tmdbVoteCount"] = el.TryGetProperty("vote_count", out var vc) ? vc.GetInt32() : 0,
        ["mediaType"] = mediaType,
        ["releaseDate"] = el.TryGetProperty("release_date", out var rd) ? rd.GetString() : el.TryGetProperty("first_air_date", out var fad) ? fad.GetString() : "",
        ["runtime"] = el.TryGetProperty("runtime", out var rt) ? rt.GetInt32() : (int?)null,
        ["originalLanguage"] = el.TryGetProperty("original_language", out var ol) ? ol.GetString() : "",
        ["genres"] = el.TryGetProperty("genres", out var gen) ? string.Join(", ", gen.EnumerateArray().Select(g => g.GetProperty("name").GetString())) : "",
        ["productionCountries"] = el.TryGetProperty("production_countries", out var pc) ? string.Join(", ", pc.EnumerateArray().Select(c => c.GetProperty("name").GetString())) : ""
    };
    
    // 解析演员信息
    List<object>? cast = null;
    if (creditsTask.Result.IsSuccessStatusCode)
    {
        var creditsRaw = await creditsTask.Result.Content.ReadAsStringAsync();
        using var creditsDoc = JsonDocument.Parse(creditsRaw);
        if (creditsDoc.RootElement.TryGetProperty("cast", out var castArray))
        {
            cast = castArray.EnumerateArray().Take(10).Select(c => new
            {
                id = c.GetProperty("id").GetInt32(),
                name = c.GetProperty("name").GetString() ?? "",
                character = c.TryGetProperty("character", out var ch) ? ch.GetString() : "",
                profilePath = c.TryGetProperty("profile_path", out var pp) ? pp.GetString() : ""
            }).Cast<object>().ToList();
        }
    }
    
    // 解析相似推荐
    List<object>? similar = null;
    if (similarTask.Result.IsSuccessStatusCode)
    {
        var similarRaw = await similarTask.Result.Content.ReadAsStringAsync();
        using var similarDoc = JsonDocument.Parse(similarRaw);
        if (similarDoc.RootElement.TryGetProperty("results", out var similarArray))
        {
            similar = similarArray.EnumerateArray().Take(12).Select(s => TmdbToDto(s, mediaType)).Cast<object>().ToList();
        }
    }
    
    // 解析视频
    List<object>? videos = null;
    if (videosTask.Result.IsSuccessStatusCode)
    {
        var videosRaw = await videosTask.Result.Content.ReadAsStringAsync();
        using var videosDoc = JsonDocument.Parse(videosRaw);
        if (videosDoc.RootElement.TryGetProperty("results", out var videosArray))
        {
            videos = videosArray.EnumerateArray()
                .Where(v => v.TryGetProperty("site", out var site) && site.GetString() == "YouTube")
                .Where(v => v.TryGetProperty("type", out var type2) && (type2.GetString() == "Trailer" || type2.GetString() == "Teaser"))
                .Select(v => new
                {
                    key = v.GetProperty("key").GetString() ?? "",
                    name = v.TryGetProperty("name", out var name) ? name.GetString() : "",
                    type = v.GetProperty("type").GetString() ?? ""
                }).Cast<object>().ToList();
        }
    }
    
    // 组装完整响应（扁平化）
    var responseData = new Dictionary<string, object?>(detail)
    {
        ["credits"] = cast,
        ["similar"] = similar,
        ["videos"] = videos
    };
    
    var response = ApiResponse(responseData);
    
    await cache.Set(key, response, 86400); // 24h
    return Results.Content(response, "application/json");
});

// POST /api/search - 聚合网盘搜索 (MainSite 调用)
app.MapPost("/api/search", async (HttpRequest req, HubService hub) =>
{
    var body = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body, cancellationToken: req.HttpContext.RequestAborted);
    var keyword = body.TryGetProperty("keyword", out var kw) ? kw.GetString() : null;
    if (string.IsNullOrWhiteSpace(keyword)) 
        return Results.BadRequest(new { success = false, data = (object?)null, error = "关键词不能为空" });
    
    var result = await hub.SearchAsync(keyword);
    // Transform to flat list for MainSite/frontend
    var flatItems = new List<object>();
    foreach (var source in result.Results)
    {
        foreach (var item in source.Items)
        {
            if (item == null) continue;
            var obj = item.AsObject();
            flatItems.Add(new {
                cloudType = obj.ContainsKey("cloud_type") ? obj["cloud_type"]?.GetValue<string>() : source.Name,
                url = obj.ContainsKey("url") ? obj["url"]?.GetValue<string>() : "",
                title = obj.ContainsKey("title") ? obj["title"]?.GetValue<string>() : "",
                password = obj.ContainsKey("password") ? obj["password"]?.GetValue<string>() : null,
                note = obj.ContainsKey("note") ? obj["note"]?.GetValue<string>() : null,
                source = source.Name
            });
        }
    }
    return Results.Ok(new { success = true, data = flatItems, error = (string?)null });
});

// ====== 🔍 聚合搜索模块 (GET) ======
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

// ====== Helper 方法 ======
static object TmdbToDto(JsonElement e, string mediaType)
{
    var id = e.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
    var title = e.TryGetProperty("title", out var t) ? t.GetString() 
        : e.TryGetProperty("name", out var n) ? n.GetString() : "";
    var overview = e.TryGetProperty("overview", out var ov) ? ov.GetString() : "";
    var posterPath = e.TryGetProperty("poster_path", out var pp) ? pp.GetString() : "";
    var backdropPath = e.TryGetProperty("backdrop_path", out var bp) ? bp.GetString() : "";
    var voteAverage = e.TryGetProperty("vote_average", out var va) ? va.GetDouble() : 0;
    var releaseDate = e.TryGetProperty("release_date", out var rd) ? rd.GetString() 
        : e.TryGetProperty("first_air_date", out var fad) ? fad.GetString() : "";
    
    return new {
        tmdbId = id,
        title,
        overview,
        posterPath,
        backdropPath,
        tmdbVoteAverage = voteAverage,
        mediaType,
        releaseDate
    };
}

static string ApiResponse(object data, string? error = null)
{
    return JsonSerializer.Serialize(new {
        success = error == null,
        data,
        error
    });
}

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
    Task Set(string key, string value, int ttlSeconds = 3600);
    (int Total, string? Latest) GetStats();
}

public class SqliteCache(string dbPath) : IDbCache
{
    public async Task<string?> Get(string key)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT response_data FROM tmdb_cache WHERE cache_key = @key AND expires_at > datetime('now')";
        cmd.Parameters.AddWithValue("@key", key);
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString();
    }
    public async Task Set(string key, string value, int ttlSeconds = 3600)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO tmdb_cache (cache_key, response_data, expires_at) 
            VALUES (@key, @value, datetime('now', '+' || @ttl || ' seconds'));
            DELETE FROM tmdb_cache WHERE expires_at < datetime('now');";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.Parameters.AddWithValue("@ttl", ttlSeconds);
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
