// v3 rebuild bust cache
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// ====== 配置 ======
var tmdbApiKey = builder.Configuration["Tmdb:ApiKey"]
    ?? Environment.GetEnvironmentVariable("TMDB_API_KEY")
    ?? throw new InvalidOperationException("Environment variable 'TMDB_API_KEY' is missing.");
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "tmdb_cache.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

// JWT 配置
var jwtKey = Environment.GetEnvironmentVariable("JWT_SECRET") ?? "zhuiying-dev-secret-key-change-in-production-2026";
var jwtIssuer = "zhuiying-hub";
var jwtAudience = "zhuiying-main-site";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();

// 注册后台搜索服务
builder.Services.AddHostedService<FavoriteSearchWorker>();

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
        CREATE INDEX IF NOT EXISTS idx_expires_at ON tmdb_cache(expires_at);

        CREATE TABLE IF NOT EXISTS users (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT UNIQUE NOT NULL,
            email TEXT UNIQUE,
            password_hash TEXT NOT NULL,
            salt TEXT NOT NULL,
            role TEXT DEFAULT 'user',
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        );
        CREATE INDEX IF NOT EXISTS idx_username ON users(username);

        CREATE TABLE IF NOT EXISTS favorites (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER NOT NULL,
            tmdb_id INTEGER NOT NULL,
            media_type TEXT NOT NULL DEFAULT 'movie',
            title TEXT,
            poster_path TEXT,
            added_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (user_id) REFERENCES users(id),
            UNIQUE(user_id, tmdb_id, media_type)
        );
        CREATE INDEX IF NOT EXISTS idx_favorites_user ON favorites(user_id);

        CREATE TABLE IF NOT EXISTS search_results (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            tmdb_id INTEGER NOT NULL,
            media_type TEXT NOT NULL DEFAULT 'movie',
            source TEXT NOT NULL,
            cloud_type TEXT,
            title TEXT,
            url TEXT,
            password TEXT,
            found_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(tmdb_id, media_type, url)
        );
        CREATE INDEX IF NOT EXISTS idx_search_tmdb ON search_results(tmdb_id, media_type);";

    // 添加网盘转存相关表
    cmd.CommandText += @"
        CREATE TABLE IF NOT EXISTS cloud_drives (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER NOT NULL,
            type TEXT NOT NULL,
            name TEXT,
            encrypted_cookie TEXT NOT NULL,
            status TEXT DEFAULT 'active',
            expires_at DATETIME,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME,
            FOREIGN KEY (user_id) REFERENCES users(id)
        );
        CREATE INDEX IF NOT EXISTS idx_cloud_drives_user ON cloud_drives(user_id);
        CREATE INDEX IF NOT EXISTS idx_cloud_drives_type ON cloud_drives(type);

        CREATE TABLE IF NOT EXISTS transfers (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER NOT NULL,
            drive_id INTEGER NOT NULL,
            tmdb_id INTEGER NOT NULL,
            media_type TEXT NOT NULL,
            season INTEGER,
            episode INTEGER,
            source_url TEXT NOT NULL,
            source_title TEXT,
            file_size INTEGER,
            quality TEXT,
            target_path TEXT NOT NULL,
            status TEXT DEFAULT 'pending',
            error_message TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            completed_at DATETIME,
            FOREIGN KEY (user_id) REFERENCES users(id),
            FOREIGN KEY (drive_id) REFERENCES cloud_drives(id)
        );
        CREATE INDEX IF NOT EXISTS idx_transfers_user ON transfers(user_id);
        CREATE INDEX IF NOT EXISTS idx_transfers_tmdb ON transfers(tmdb_id, media_type);
        CREATE INDEX IF NOT EXISTS idx_transfers_status ON transfers(status);

        CREATE TABLE IF NOT EXISTS transferred_episodes (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            tmdb_id INTEGER NOT NULL,
            media_type TEXT NOT NULL,
            season INTEGER NOT NULL,
            episode INTEGER NOT NULL,
            drive_id INTEGER NOT NULL,
            file_path TEXT,
            file_size INTEGER,
            quality TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(tmdb_id, media_type, season, episode, drive_id)
        );
        CREATE INDEX IF NOT EXISTS idx_episodes_tmdb ON transferred_episodes(tmdb_id, media_type);

        CREATE TABLE IF NOT EXISTS storage_configs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER NOT NULL UNIQUE,
            config_json TEXT NOT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME,
            FOREIGN KEY (user_id) REFERENCES users(id)
        );
        CREATE INDEX IF NOT EXISTS idx_storage_configs_user ON storage_configs(user_id);";
    
    // 检查并迁移旧表结构
    cmd.CommandText = "PRAGMA table_info(search_results)";
    using (var reader = await cmd.ExecuteReaderAsync())
    {
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1)); // column name
        }
        reader.Close();
        
        // 如果表存在但没有 tmdb_id 列，说明是旧结构，需要迁移
        if (columns.Any() && !columns.Contains("tmdb_id"))
        {
            cmd.CommandText = "DROP TABLE search_results";
            await cmd.ExecuteNonQueryAsync();
            
            cmd.CommandText = @"
                CREATE TABLE search_results (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    tmdb_id INTEGER NOT NULL,
                    media_type TEXT NOT NULL DEFAULT 'movie',
                    source TEXT NOT NULL,
                    cloud_type TEXT,
                    title TEXT,
                    url TEXT,
                    password TEXT,
                    found_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(tmdb_id, media_type, url)
                );
                CREATE INDEX idx_search_tmdb ON search_results(tmdb_id, media_type);";
        }
    }
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "DELETE FROM tmdb_cache";
    await cmd.ExecuteNonQueryAsync();

    // 创建默认管理员账户
    var adminUser = Environment.GetEnvironmentVariable("ADMIN_USER") ?? "admin";
    var adminPass = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? "admin123";
    cmd.CommandText = "SELECT COUNT(*) FROM users WHERE username = @username";
    cmd.Parameters.AddWithValue("@username", adminUser);
    var adminExists = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    if (!adminExists)
    {
        var salt = GenerateSalt();
        cmd.CommandText = "INSERT INTO users (username, password_hash, salt, role) VALUES (@username, @hash, @salt, 'admin')";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@username", adminUser);
        cmd.Parameters.AddWithValue("@hash", HashPassword(adminPass, salt));
        cmd.Parameters.AddWithValue("@salt", salt);
        await cmd.ExecuteNonQueryAsync();
    }
}

// ====== 服务注册 ======
builder.Services.AddSingleton<IDbCache>(new SqliteCache(dbPath));
builder.Services.AddHttpClient("tmdb", c => { c.BaseAddress = new Uri("https://api.themoviedb.org/3/"); });
builder.Services.AddHttpClient();
builder.Services.AddCors();
builder.Services.AddSingleton<HubService>();

// 注册网盘服务
builder.Services.AddScoped<Zhuiying.Hub.Services.CloudDrive123Service>();
builder.Services.AddScoped<Zhuiying.Hub.Services.CloudDrive115Service>();

var app = builder.Build();
app.UseCors(c => c.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
app.UseAuthentication();
app.UseAuthorization();

var cache = app.Services.GetRequiredService<IDbCache>();
var hub = app.Services.GetRequiredService<HubService>();

// ====== 🔐 认证模块 (/api/auth) ======

app.MapPost("/api/auth/register", async (RegisterRequest req, IDbCache cache) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Length < 3)
        return Results.BadRequest(new { success = false, error = "用户名至少3个字符" });
    if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
        return Results.BadRequest(new { success = false, error = "密码至少6个字符" });

    var dbPath = Path.Combine(app.Environment.ContentRootPath, "data", "tmdb_cache.db");
    await using var conn = new SqliteConnection($"Data Source={dbPath}");
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();

    // 检查用户名是否已存在
    cmd.CommandText = "SELECT COUNT(*) FROM users WHERE username = @username";
    cmd.Parameters.AddWithValue("@username", req.Username);
    if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0)
        return Results.BadRequest(new { success = false, error = "用户名已存在" });

    // 创建用户
    var salt = GenerateSalt();
    cmd.CommandText = "INSERT INTO users (username, email, password_hash, salt) VALUES (@username, @email, @hash, @salt); SELECT last_insert_rowid();";
    cmd.Parameters.Clear();
    cmd.Parameters.AddWithValue("@username", req.Username);
    cmd.Parameters.AddWithValue("@email", req.Email ?? "");
    cmd.Parameters.AddWithValue("@hash", HashPassword(req.Password, salt));
    cmd.Parameters.AddWithValue("@salt", salt);
    var userId = Convert.ToInt32(await cmd.ExecuteScalarAsync());

    // 创建默认存储配置
    var defaultConfig = JsonSerializer.Serialize(new
    {
        moviePath = "",
        tvPath = "",
        templateVariables = new[] { "{title}", "{year}", "{tmdb_id}", "{season}", "{quality}", "{subtitle}", "{genre}", "{filename}" }
    });
    cmd.CommandText = "INSERT INTO storage_configs (user_id, config_json) VALUES (@userId, @config)";
    cmd.Parameters.Clear();
    cmd.Parameters.AddWithValue("@userId", userId);
    cmd.Parameters.AddWithValue("@config", defaultConfig);
    await cmd.ExecuteNonQueryAsync();

    var token = GenerateJwtToken(req.Username, userId, "user");
    return Results.Ok(new { success = true, data = new { userId, username = req.Username, token }, error = (string?)null });
});

app.MapPost("/api/auth/login", async (LoginRequest req, IDbCache cache) =>
{
    var dbPath = Path.Combine(app.Environment.ContentRootPath, "data", "tmdb_cache.db");
    await using var conn = new SqliteConnection($"Data Source={dbPath}");
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();

    cmd.CommandText = "SELECT id, username, password_hash, salt, role FROM users WHERE username = @username";
    cmd.Parameters.AddWithValue("@username", req.Username);
    using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.BadRequest(new { success = false, error = "用户名或密码错误" });

    var userId = reader.GetInt32(0);
    var username = reader.GetString(1);
    var storedHash = reader.GetString(2);
    var storedSalt = reader.GetString(3);
    var role = reader.GetString(4);

    if (HashPassword(req.Password, storedSalt) != storedHash)
        return Results.BadRequest(new { success = false, error = "用户名或密码错误" });

    var token = GenerateJwtToken(username, userId, role);
    return Results.Ok(new { success = true, data = new { userId, username, role, token }, error = (string?)null });
});

app.MapGet("/api/auth/me", (ClaimsPrincipal user) =>
{
    if (user.Identity?.IsAuthenticated != true)
        return Results.Unauthorized();
    var userId = user.FindFirst("userId")?.Value;
    var username = user.FindFirst(ClaimTypes.Name)?.Value;
    var role = user.FindFirst(ClaimTypes.Role)?.Value;
    return Results.Ok(new { success = true, data = new { userId, username, role }, error = (string?)null });
}).RequireAuthorization();

// ====== ⭐ 收藏模块 (/api/favorites) ======

app.MapPost("/api/favorites", async (HttpRequest req, ClaimsPrincipal user) =>
{
    if (user.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    var userId = int.Parse(user.FindFirst("userId")!.Value);

    using var reader = new StreamReader(req.Body, System.Text.Encoding.UTF8);
    var bodyText = await reader.ReadToEndAsync();
    var body = JsonSerializer.Deserialize<JsonElement>(bodyText);
    var tmdbId = body.GetProperty("tmdbId").GetInt32();
    var mediaType = body.TryGetProperty("mediaType", out var mt) ? mt.GetString() ?? "movie" : "movie";
    var title = body.TryGetProperty("title", out var t) ? t.GetString() : "";
    var posterPath = body.TryGetProperty("posterPath", out var pp) ? pp.GetString() : "";

    var dbPath = Path.Combine(app.Environment.ContentRootPath, "data", "tmdb_cache.db");
    await using var conn = new SqliteConnection($"Data Source={dbPath}");
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();

    cmd.CommandText = @"INSERT OR IGNORE INTO favorites (user_id, tmdb_id, media_type, title, poster_path)
                        VALUES (@userId, @tmdbId, @mediaType, @title, @posterPath)";
    cmd.Parameters.AddWithValue("@userId", userId);
    cmd.Parameters.AddWithValue("@tmdbId", tmdbId);
    cmd.Parameters.AddWithValue("@mediaType", mediaType);
    cmd.Parameters.AddWithValue("@title", title ?? "");
    cmd.Parameters.AddWithValue("@posterPath", posterPath ?? "");
    await cmd.ExecuteNonQueryAsync();

    return Results.Ok(new { success = true, error = (string?)null });
}).RequireAuthorization();

app.MapDelete("/api/favorites/{tmdbId:int}/{mediaType}", async (int tmdbId, string mediaType, ClaimsPrincipal user) =>
{
    if (user.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    var userId = int.Parse(user.FindFirst("userId")!.Value);

    var dbPath = Path.Combine(app.Environment.ContentRootPath, "data", "tmdb_cache.db");
    await using var conn = new SqliteConnection($"Data Source={dbPath}");
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();

    cmd.CommandText = "DELETE FROM favorites WHERE user_id = @userId AND tmdb_id = @tmdbId AND media_type = @mediaType";
    cmd.Parameters.AddWithValue("@userId", userId);
    cmd.Parameters.AddWithValue("@tmdbId", tmdbId);
    cmd.Parameters.AddWithValue("@mediaType", mediaType);
    await cmd.ExecuteNonQueryAsync();

    return Results.Ok(new { success = true, error = (string?)null });
}).RequireAuthorization();

app.MapGet("/api/favorites", async (ClaimsPrincipal user) =>
{
    if (user.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    var userId = int.Parse(user.FindFirst("userId")!.Value);

    var dbPath = Path.Combine(app.Environment.ContentRootPath, "data", "tmdb_cache.db");
    await using var conn = new SqliteConnection($"Data Source={dbPath}");
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();

    cmd.CommandText = @"SELECT f.id, f.tmdb_id, f.media_type, f.title, f.poster_path, f.added_at,
                        0 as link_count
                        FROM favorites f WHERE f.user_id = @userId ORDER BY f.added_at DESC";
    cmd.Parameters.AddWithValue("@userId", userId);

    var favorites = new List<object>();
    using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        favorites.Add(new {
            id = reader.GetInt32(0),
            tmdbId = reader.GetInt32(1),
            mediaType = reader.GetString(2),
            title = reader.GetString(3),
            posterPath = reader.IsDBNull(4) ? "" : reader.GetString(4),
            addedAt = reader.GetDateTime(5).ToString("o"),
            linkCount = reader.GetInt32(6)
        });
    }

    return Results.Ok(new { success = true, data = favorites, error = (string?)null });
}).RequireAuthorization();






// ====== 🔍 按 TMDB ID 搜索网盘资源 ======
app.MapGet("/api/search/links/{tmdbId:int}/{mediaType}", async (int tmdbId, string mediaType, IDbCache cache) =>
{
    var dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "tmdb_cache.db");
    await using var conn = new SqliteConnection($"Data Source={dbPath}");
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();

    // 查询24小时内的结果
    cmd.CommandText = @"SELECT id, source, cloud_type, title, url, password, found_at 
                        FROM search_results 
                        WHERE tmdb_id = @tmdbId AND media_type = @mediaType 
                        AND found_at > datetime('now', '-24 hours')
                        ORDER BY cloud_type, found_at DESC";
    cmd.Parameters.AddWithValue("@tmdbId", tmdbId);
    cmd.Parameters.AddWithValue("@mediaType", mediaType);

    var links = new List<object>();
    using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        links.Add(new {
            id = reader.GetInt32(0),
            source = reader.GetString(1),
            cloudType = reader.IsDBNull(2) ? "" : reader.GetString(2),
            title = reader.IsDBNull(3) ? "" : reader.GetString(3),
            url = reader.IsDBNull(4) ? "" : reader.GetString(4),
            password = reader.IsDBNull(5) ? null : reader.GetString(5),
            foundAt = reader.GetDateTime(6).ToString("o")
        });
    }

    return Results.Ok(new { success = true, data = links, error = (string?)null });
});

// ====== 🔍 按 TMDB ID 搜索并保存网盘资源 ======
app.MapPost("/api/search/links/{tmdbId:int}/{mediaType}", async (int tmdbId, string mediaType, HubService hubService) =>
{
    // 先查询影视标题
    var client = hubService.HttpFactory.CreateClient("tmdb");
    var path = $"{(mediaType == "tv" ? "tv" : "movie")}/{tmdbId}?api_key={tmdbApiKey}&language=zh-CN";
    var response = await client.GetAsync(path);
    if (!response.IsSuccessStatusCode)
        return Results.BadRequest(new { success = false, error = "影视不存在" });

    var json = await response.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(json);
    var title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString()
        : doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : "";

    if (string.IsNullOrEmpty(title))
        return Results.BadRequest(new { success = false, error = "无法获取影视标题" });

    // 搜索网盘
    var searchResult = await hubService.SearchAsync(title);
    var savedCount = 0;

    var dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "tmdb_cache.db");
    await using var conn = new SqliteConnection($"Data Source={dbPath}");
    await conn.OpenAsync();

    foreach (var source in searchResult.Results)
    {
        foreach (var item in source.Items)
        {
            if (item == null) continue;
            var obj = item.AsObject();
            var url = obj.ContainsKey("url") ? obj["url"]?.GetValue<string>() : "";
            if (string.IsNullOrEmpty(url)) continue;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT OR IGNORE INTO search_results (tmdb_id, media_type, source, cloud_type, title, url, password)
                                VALUES (@tmdbId, @mediaType, @source, @cloudType, @title, @url, @password)";
            cmd.Parameters.AddWithValue("@tmdbId", tmdbId);
            cmd.Parameters.AddWithValue("@mediaType", mediaType);
            cmd.Parameters.AddWithValue("@source", source.Name);
            cmd.Parameters.AddWithValue("@cloudType", obj.ContainsKey("cloud_type") ? obj["cloud_type"]?.GetValue<string>() : "");
            cmd.Parameters.AddWithValue("@title", obj.ContainsKey("title") ? obj["title"]?.GetValue<string>() : "");
            cmd.Parameters.AddWithValue("@url", url);
            cmd.Parameters.AddWithValue("@password", obj.ContainsKey("password") ? obj["password"]?.GetValue<string>() : null);
            savedCount += await cmd.ExecuteNonQueryAsync();
        }
    }

    return Results.Ok(new { success = true, data = new { savedCount }, error = (string?)null });
});

// ====== 健康检查 ======
// 兼容旧版搜索接口
app.MapGet("/api/search/movie", async (string keyword, string type, int page, IHttpClientFactory f, [AsParameters] QueryParams qp) =>
{
    if (string.IsNullOrWhiteSpace(keyword)) return Results.BadRequest(ApiResponse(new { }, "Missing keyword"));
    
    var client = f.CreateClient("tmdb");
    var enc = Uri.EscapeDataString(keyword);
    
    // 同时搜索电影和电视剧
    var movieTask = client.GetAsync($"search/movie?api_key={tmdbApiKey}&query={enc}&language={qp.Language}&page={page}");
    var tvTask = client.GetAsync($"search/tv?api_key={tmdbApiKey}&query={enc}&language={qp.Language}&page={page}");
    
    await Task.WhenAll(movieTask, tvTask);
    
    var movieResponse = await movieTask;
    var tvResponse = await tvTask;
    
    var results = new List<object>();
    
    // 解析电影结果
    if (movieResponse.IsSuccessStatusCode)
    {
        var movieJson = JsonDocument.Parse(await movieResponse.Content.ReadAsStringAsync());
        var movieResults = movieJson.RootElement.GetProperty("results").EnumerateArray()
            .Select(e => TmdbToDto(e, "movie"));
        results.AddRange(movieResults);
    }
    
    // 解析电视剧结果
    if (tvResponse.IsSuccessStatusCode)
    {
        var tvJson = JsonDocument.Parse(await tvResponse.Content.ReadAsStringAsync());
        var tvResults = tvJson.RootElement.GetProperty("results").EnumerateArray()
            .Select(e => TmdbToDto(e, "tv"));
        results.AddRange(tvResults);
    }
    
    // 按热度排序
    var sortedResults = results.OrderByDescending(r => 
    {
        var prop = r.GetType().GetProperty("tmdbVoteAverage");
        return prop?.GetValue(r) is double d ? d : 0;
    }).ToList();
    
    return Results.Content(ApiResponse(sortedResults), "application/json");
});

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

// GET /api/movie/discover - 发现影视（支持流派/年份/评分筛选）
app.MapGet("/api/movie/discover", async (string? type, int? genreId, int? year, double? minRating, int page, IHttpClientFactory f, IDbCache cache) =>
{
    var mediaType = type ?? "movie";
    var key = $"discover2/{mediaType}?genre={genreId}&year={year}&min={minRating}&page={page}";
    var cached = await cache.Get(key);
    if (cached != null) return Results.Content(cached, "application/json");

    var client = f.CreateClient("tmdb");
    var path = $"discover/{(mediaType == "tv" ? "tv" : "movie")}?api_key={tmdbApiKey}&language=zh-CN&page={page}&sort_by=popularity.desc";

    if (genreId.HasValue && genreId.Value > 0)
        path += $"&with_genres={genreId.Value}";
    if (year.HasValue && year.Value > 0)
    {
        var dateField = mediaType == "tv" ? "first_air_date_year" : "primary_release_year";
        path += $"&{dateField}={year.Value}";
    }
    if (minRating.HasValue && minRating.Value > 0)
        path += $"&vote_average.gte={minRating.Value}";

    var tmdbResp = await client.GetAsync(path);
    if (!tmdbResp.IsSuccessStatusCode) return Results.StatusCode(502);
    var raw = await tmdbResp.Content.ReadAsStringAsync();

    using var doc = JsonDocument.Parse(raw);
    var results = doc.RootElement.GetProperty("results").EnumerateArray()
        .Select(e => TmdbToDto(e, mediaType))
        .ToList();

    var totalPages = doc.RootElement.TryGetProperty("total_pages", out var tp) ? tp.GetInt32() : 1;
    var response = JsonSerializer.Serialize(new {
        success = true,
        data = results,
        page,
        totalPages,
        error = (string?)null
    });

    if (results.Count > 0) await cache.Set(key, response, 1800);
    return Results.Content(response, "application/json");
});

// GET /api/movie/genres - 获取流派列表
app.MapGet("/api/movie/genres", async (string? type, IHttpClientFactory f, IDbCache cache) =>
{
    var mediaType = type ?? "movie";
    var key = $"genres/{mediaType}";
    var cached = await cache.Get(key);
    if (cached != null) return Results.Content(cached, "application/json");

    var client = f.CreateClient("tmdb");
    var path = $"genre/{(mediaType == "tv" ? "tv" : "movie")}/list?api_key={tmdbApiKey}&language=zh-CN";
    var tmdbResp = await client.GetAsync(path);
    if (!tmdbResp.IsSuccessStatusCode) return Results.StatusCode(502);
    var raw = await tmdbResp.Content.ReadAsStringAsync();

    using var doc = JsonDocument.Parse(raw);
    var genres = doc.RootElement.GetProperty("genres").EnumerateArray()
        .Select(g => new { id = g.GetProperty("id").GetInt32(), name = g.GetProperty("name").GetString() ?? "" })
        .ToList();

    var response = JsonSerializer.Serialize(new { success = true, data = genres, error = (string?)null });
    await cache.Set(key, response, 86400); // 24h
    return Results.Content(response, "application/json");
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


// ====== 网盘管理 API ======

// 获取网盘列表
app.MapGet("/api/cloud-drives", async (HttpRequest req, IDbCache cache) =>
{
    var userId = req.HttpContext.User.FindFirst("userId")?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    await using var conn = new SqliteConnection($"Data Source={Path.Combine(app.Environment.ContentRootPath, "data", "tmdb_cache.db")}");
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT id, type, name, status, expires_at, created_at FROM cloud_drives WHERE user_id = @userId ORDER BY created_at DESC";
    cmd.Parameters.AddWithValue("@userId", int.Parse(userId));

    var drives = new List<object>();
    using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        drives.Add(new
        {
            id = reader.GetInt32(0),
            type = reader.GetString(1),
            name = reader.IsDBNull(2) ? null : reader.GetString(2),
            status = reader.GetString(3),
            expiresAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4).ToString("o"),
            createdAt = reader.GetDateTime(5).ToString("o")
        });
    }

    return Results.Ok(new { success = true, data = drives });
}).RequireAuthorization();

// 添加网盘账号
app.MapPost("/api/cloud-drives", async (HttpRequest req, IDbCache cache) =>
{
    var userId = req.HttpContext.User.FindFirst("userId")?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    var body = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body);
    var type = body.GetProperty("type").GetString();
    var name = body.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
    var cookie = body.GetProperty("cookie").GetString();

    if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(cookie))
        return Results.BadRequest(new { success = false, error = "类型和Cookie不能为空" });

    if (type != "123" && type != "115")
        return Results.BadRequest(new { success = false, error = "不支持的网盘类型" });

    // 加密 Cookie
    var encryptedCookie = Zhuiying.Hub.Services.CookieEncryption.Encrypt(cookie);

    await using var conn = new SqliteConnection($"Data Source={Path.Combine(app.Environment.ContentRootPath, "data", "tmdb_cache.db")}");
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"INSERT INTO cloud_drives (user_id, type, name, encrypted_cookie, status) 
                        VALUES (@userId, @type, @name, @cookie, 'active'); SELECT last_insert_rowid();";
    cmd.Parameters.AddWithValue("@userId", int.Parse(userId));
    cmd.Parameters.AddWithValue("@type", type);
    cmd.Parameters.AddWithValue("@name", name ?? "");
    cmd.Parameters.AddWithValue("@cookie", encryptedCookie);

    var driveId = Convert.ToInt32(await cmd.ExecuteScalarAsync());

    return Results.Ok(new { success = true, data = new { id = driveId, type, name, status = "active" } });
}).RequireAuthorization();

// 删除网盘账号
app.MapDelete("/api/cloud-drives/{id}", async (int id, HttpRequest req, IDbCache cache) =>
{
    var userId = req.HttpContext.User.FindFirst("userId")?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    await using var conn = new SqliteConnection($"Data Source={Path.Combine(app.Environment.ContentRootPath, "data", "tmdb_cache.db")}");
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "DELETE FROM cloud_drives WHERE id = @id AND user_id = @userId";
    cmd.Parameters.AddWithValue("@id", id);
    cmd.Parameters.AddWithValue("@userId", int.Parse(userId));

    var affected = await cmd.ExecuteNonQueryAsync();
    return Results.Ok(new { success = affected > 0 });
}).RequireAuthorization();

// 测试网盘连接
app.MapPost("/api/cloud-drives/{id}/test", async (int id, HttpRequest req, IDbCache cache,
    Zhuiying.Hub.Services.CloudDrive123Service drive123,
    Zhuiying.Hub.Services.CloudDrive115Service drive115) =>
{
    var userId = req.HttpContext.User.FindFirst("userId")?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    await using var conn = new SqliteConnection($"Data Source={Path.Combine(app.Environment.ContentRootPath, "data", "tmdb_cache.db")}");
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT type, encrypted_cookie FROM cloud_drives WHERE id = @id AND user_id = @userId";
    cmd.Parameters.AddWithValue("@id", id);
    cmd.Parameters.AddWithValue("@userId", int.Parse(userId));

    using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.NotFound(new { success = false, error = "网盘不存在" });

    var type = reader.GetString(0);
    var encryptedCookie = reader.GetString(1);
    var cookie = Zhuiying.Hub.Services.CookieEncryption.Decrypt(encryptedCookie);

    Zhuiying.Hub.Services.CloudDriveBase service = type switch
    {
        "123" => drive123,
        "115" => drive115,
        _ => throw new Exception("不支持的网盘类型")
    };

    var success = await service.TestConnectionAsync(cookie);
    var userInfo = success ? await service.GetUserInfoAsync(cookie) : null;

    // 更新状态
    using var updateCmd = conn.CreateCommand();
    updateCmd.CommandText = "UPDATE cloud_drives SET status = @status, updated_at = datetime('now') WHERE id = @id";
    updateCmd.Parameters.AddWithValue("@status", success ? "active" : "invalid");
    updateCmd.Parameters.AddWithValue("@id", id);
    await updateCmd.ExecuteNonQueryAsync();

    return Results.Ok(new { success, userInfo });
}).RequireAuthorization();

// ====== 转存 API ======

// 创建转存任务
app.MapPost("/api/transfers", async (HttpRequest req, IDbCache cache,
    Zhuiying.Hub.Services.CloudDrive123Service drive123,
    Zhuiying.Hub.Services.CloudDrive115Service drive115) =>
{
    var userId = req.HttpContext.User.FindFirst("userId")?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    var body = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body);
    var driveId = body.GetProperty("driveId").GetInt32();
    var tmdbId = body.GetProperty("tmdbId").GetInt32();
    var mediaType = body.GetProperty("mediaType").GetString() ?? "movie";
    var sourceUrl = body.GetProperty("sourceUrl").GetString();
    var sourceTitle = body.TryGetProperty("sourceTitle", out var titleEl) ? titleEl.GetString() : null;
    var season = body.TryGetProperty("season", out var seasonEl) ? seasonEl.GetInt32() : (int?)null;
    var episode = body.TryGetProperty("episode", out var episodeEl) ? episodeEl.GetInt32() : (int?)null;
    var targetPath = body.TryGetProperty("targetPath", out var pathEl) ? pathEl.GetString() : "";

    if (string.IsNullOrEmpty(sourceUrl))
        return Results.BadRequest(new { success = false, error = "分享链接不能为空" });

    await using var conn = new SqliteConnection($"Data Source={Path.Combine(app.Environment.ContentRootPath, "data", "tmdb_cache.db")}");
    await conn.OpenAsync();

    // 获取网盘信息
    using var driveCmd = conn.CreateCommand();
    driveCmd.CommandText = "SELECT type, encrypted_cookie FROM cloud_drives WHERE id = @id AND user_id = @userId";
    driveCmd.Parameters.AddWithValue("@id", driveId);
    driveCmd.Parameters.AddWithValue("@userId", int.Parse(userId));

    using var driveReader = await driveCmd.ExecuteReaderAsync();
    if (!await driveReader.ReadAsync())
        return Results.BadRequest(new { success = false, error = "网盘不存在" });

    var driveType = driveReader.GetString(0);
    var encryptedCookie = driveReader.GetString(1);
    var cookie = Zhuiying.Hub.Services.CookieEncryption.Decrypt(encryptedCookie);

    Zhuiying.Hub.Services.CloudDriveBase service = driveType switch
    {
        "123" => drive123,
        "115" => drive115,
        _ => throw new Exception("不支持的网盘类型")
    };

    // 解析分享链接
    var (shareId, sharePwd) = service.ParseShareLink(sourceUrl);
    if (string.IsNullOrEmpty(shareId))
        return Results.BadRequest(new { success = false, error = "无效的分享链接" });

    // 获取分享文件列表
    List<Zhuiying.Hub.Models.ShareFileItem> files;
    try
    {
        files = await service.GetShareFileListAsync(cookie, shareId, sharePwd);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { success = false, error = $"获取分享文件失败: {ex.Message}" });
    }

    // 创建转存记录
    var transferIds = new List<int>();
    using var insertCmd = conn.CreateCommand();
    
    foreach (var file in files.Where(f => !f.IsFolder))
    {
        insertCmd.CommandText = @"INSERT INTO transfers (user_id, drive_id, tmdb_id, media_type, season, episode, 
                                  source_url, source_title, file_size, target_path, status) 
                                  VALUES (@userId, @driveId, @tmdbId, @mediaType, @season, @episode, 
                                  @sourceUrl, @sourceTitle, @fileSize, @targetPath, 'pending'); SELECT last_insert_rowid();";
        insertCmd.Parameters.Clear();
        insertCmd.Parameters.AddWithValue("@userId", int.Parse(userId));
        insertCmd.Parameters.AddWithValue("@driveId", driveId);
        insertCmd.Parameters.AddWithValue("@tmdbId", tmdbId);
        insertCmd.Parameters.AddWithValue("@mediaType", mediaType);
        insertCmd.Parameters.AddWithValue("@season", season.HasValue ? (object)season.Value : DBNull.Value);
        insertCmd.Parameters.AddWithValue("@episode", episode.HasValue ? (object)episode.Value : DBNull.Value);
        insertCmd.Parameters.AddWithValue("@sourceUrl", sourceUrl);
        insertCmd.Parameters.AddWithValue("@sourceTitle", sourceTitle ?? "");
        insertCmd.Parameters.AddWithValue("@fileSize", file.FileSize);
        insertCmd.Parameters.AddWithValue("@targetPath", targetPath ?? "");

        var transferId = Convert.ToInt32(await insertCmd.ExecuteScalarAsync());
        transferIds.Add(transferId);
    }

    // 异步执行转存（后台任务）
    _ = Task.Run(async () =>
    {
        try
        {
            foreach (var (file, transferId) in files.Where(f => !f.IsFolder).Zip(transferIds))
            {
                // 更新状态为转存中
                using var updateCmd = conn.CreateCommand();
                updateCmd.CommandText = "UPDATE transfers SET status = 'transferring' WHERE id = @id";
                updateCmd.Parameters.AddWithValue("@id", transferId);
                await updateCmd.ExecuteNonQueryAsync();

                // 执行转存
                var result = await service.TransferFileAsync(cookie, shareId, sharePwd, file.FileId, targetPath ?? "0");

                // 更新结果
                updateCmd.CommandText = @"UPDATE transfers SET status = @status, error_message = @error, 
                                          completed_at = datetime('now') WHERE id = @id";
                updateCmd.Parameters.Clear();
                updateCmd.Parameters.AddWithValue("@status", result.Success ? "completed" : "failed");
                updateCmd.Parameters.AddWithValue("@error", result.ErrorMessage ?? (object)DBNull.Value);
                updateCmd.Parameters.AddWithValue("@id", transferId);
                await updateCmd.ExecuteNonQueryAsync();

                // 如果成功，记录到 transferred_episodes
                if (result.Success && season.HasValue && episode.HasValue)
                {
                    using var episodeCmd = conn.CreateCommand();
                    episodeCmd.CommandText = @"INSERT OR IGNORE INTO transferred_episodes 
                                              (tmdb_id, media_type, season, episode, drive_id, file_path, file_size) 
                                              VALUES (@tmdbId, @mediaType, @season, @episode, @driveId, @filePath, @fileSize)";
                    episodeCmd.Parameters.AddWithValue("@tmdbId", tmdbId);
                    episodeCmd.Parameters.AddWithValue("@mediaType", mediaType);
                    episodeCmd.Parameters.AddWithValue("@season", season.Value);
                    episodeCmd.Parameters.AddWithValue("@episode", episode.Value);
                    episodeCmd.Parameters.AddWithValue("@driveId", driveId);
                    episodeCmd.Parameters.AddWithValue("@filePath", file.FileName);
                    episodeCmd.Parameters.AddWithValue("@fileSize", file.FileSize);
                    await episodeCmd.ExecuteNonQueryAsync();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"转存后台任务出错: {ex.Message}");
        }
    });

    return Results.Ok(new { success = true, data = new { transferIds, totalFiles = transferIds.Count } });
}).RequireAuthorization();

// 获取转存记录
app.MapGet("/api/transfers", async (HttpRequest req, IDbCache cache, string? status, int page, int pageSize) =>
{
    var userId = req.HttpContext.User.FindFirst("userId")?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    page = page < 1 ? 1 : page;
    pageSize = pageSize < 1 || pageSize > 100 ? 20 : pageSize;
    var offset = (page - 1) * pageSize;

    await using var conn = new SqliteConnection($"Data Source={Path.Combine(app.Environment.ContentRootPath, "data", "tmdb_cache.db")}");
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();

    var whereClause = string.IsNullOrEmpty(status) ? "WHERE t.user_id = @userId" : "WHERE t.status = @status AND t.user_id = @userId";
    cmd.CommandText = $@"SELECT t.id, t.drive_id, t.tmdb_id, t.media_type, t.season, t.episode, 
                         t.source_url, t.source_title, t.file_size, t.target_path, t.status, 
                         t.error_message, t.created_at, t.completed_at, cd.type as drive_type
                         FROM transfers t 
                         LEFT JOIN cloud_drives cd ON t.drive_id = cd.id
                         {whereClause}
                         ORDER BY t.created_at DESC
                         LIMIT @limit OFFSET @offset";
    cmd.Parameters.AddWithValue("@userId", int.Parse(userId));
    cmd.Parameters.AddWithValue("@limit", pageSize);
    cmd.Parameters.AddWithValue("@offset", offset);
    if (!string.IsNullOrEmpty(status))
        cmd.Parameters.AddWithValue("@status", status);

    var transfers = new List<object>();
    using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        transfers.Add(new
        {
            id = reader.GetInt32(0),
            driveId = reader.GetInt32(1),
            tmdbId = reader.GetInt32(2),
            mediaType = reader.GetString(3),
            season = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
            episode = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
            sourceUrl = reader.GetString(6),
            sourceTitle = reader.IsDBNull(7) ? null : reader.GetString(7),
            fileSize = reader.IsDBNull(8) ? (long?)null : reader.GetInt64(8),
            targetPath = reader.GetString(9),
            status = reader.GetString(10),
            errorMessage = reader.IsDBNull(11) ? null : reader.GetString(11),
            createdAt = reader.GetDateTime(12).ToString("o"),
            completedAt = reader.IsDBNull(13) ? null : reader.GetDateTime(13).ToString("o"),
            driveType = reader.IsDBNull(14) ? null : reader.GetString(14)
        });
    }

    return Results.Ok(new { success = true, data = transfers, page, pageSize });
}).RequireAuthorization();

// 重试失败的转存
app.MapPost("/api/transfers/{id}/retry", async (int id, HttpRequest req, IDbCache cache,
    Zhuiying.Hub.Services.CloudDrive123Service drive123,
    Zhuiying.Hub.Services.CloudDrive115Service drive115) =>
{
    var userId = req.HttpContext.User.FindFirst("userId")?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    await using var conn = new SqliteConnection($"Data Source={Path.Combine(app.Environment.ContentRootPath, "data", "tmdb_cache.db")}");
    await conn.OpenAsync();

    // 获取转存记录
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"SELECT t.drive_id, t.source_url, t.target_path, cd.type, cd.encrypted_cookie 
                        FROM transfers t 
                        LEFT JOIN cloud_drives cd ON t.drive_id = cd.id
                        WHERE t.id = @id AND t.user_id = @userId AND t.status = 'failed'";
    cmd.Parameters.AddWithValue("@id", id);
    cmd.Parameters.AddWithValue("@userId", int.Parse(userId));

    using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.BadRequest(new { success = false, error = "转存记录不存在或状态不是失败" });

    var driveId = reader.GetInt32(0);
    var sourceUrl = reader.GetString(1);
    var targetPath = reader.GetString(2);
    var driveType = reader.GetString(3);
    var encryptedCookie = reader.GetString(4);
    var cookie = Zhuiying.Hub.Services.CookieEncryption.Decrypt(encryptedCookie);

    Zhuiying.Hub.Services.CloudDriveBase service = driveType switch
    {
        "123" => drive123,
        "115" => drive115,
        _ => throw new Exception("不支持的网盘类型")
    };

    var (shareId, sharePwd) = service.ParseShareLink(sourceUrl);
    
    // 更新状态为转存中
    using var updateCmd = conn.CreateCommand();
    updateCmd.CommandText = "UPDATE transfers SET status = 'transferring', error_message = NULL WHERE id = @id";
    updateCmd.Parameters.AddWithValue("@id", id);
    await updateCmd.ExecuteNonQueryAsync();

    // 异步执行转存
    _ = Task.Run(async () =>
    {
        try
        {
            var files = await service.GetShareFileListAsync(cookie, shareId, sharePwd);
            var file = files.FirstOrDefault(f => !f.IsFolder);
            if (file == null)
                throw new Exception("分享链接中没有文件");

            var result = await service.TransferFileAsync(cookie, shareId, sharePwd, file.FileId, targetPath);

            using var conn2 = new SqliteConnection($"Data Source={Path.Combine(app.Environment.ContentRootPath, "data", "tmdb_cache.db")}");
            await conn2.OpenAsync();
            using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = @"UPDATE transfers SET status = @status, error_message = @error, 
                                completed_at = datetime('now') WHERE id = @id";
            cmd2.Parameters.AddWithValue("@status", result.Success ? "completed" : "failed");
            cmd2.Parameters.AddWithValue("@error", result.ErrorMessage ?? (object)DBNull.Value);
            cmd2.Parameters.AddWithValue("@id", id);
            await cmd2.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"重试转存出错: {ex.Message}");
        }
    });

    return Results.Ok(new { success = true });
}).RequireAuthorization();

// ====== 存储配置 API ======

// 获取存储配置
app.MapGet("/api/storage-config", async (HttpRequest req, IDbCache cache) =>
{
    var userId = req.HttpContext.User.FindFirst("userId")?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    await using var conn = new SqliteConnection($"Data Source={Path.Combine(app.Environment.ContentRootPath, "data", "tmdb_cache.db")}");
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT config_json FROM storage_configs WHERE user_id = @userId";
    cmd.Parameters.AddWithValue("@userId", int.Parse(userId));

    var configJson = await cmd.ExecuteScalarAsync() as string;
    if (string.IsNullOrEmpty(configJson))
        return Results.Ok(new { success = true, data = new Zhuiying.Hub.Models.StoragePathConfig() });

    var config = JsonSerializer.Deserialize<Zhuiying.Hub.Models.StoragePathConfig>(configJson);
    return Results.Ok(new { success = true, data = config });
}).RequireAuthorization();

// 更新存储配置
app.MapPut("/api/storage-config", async (HttpRequest req, IDbCache cache) =>
{
    var userId = req.HttpContext.User.FindFirst("userId")?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    var config = await JsonSerializer.DeserializeAsync<Zhuiying.Hub.Models.StoragePathConfig>(req.Body);
    if (config == null)
        return Results.BadRequest(new { success = false, error = "配置格式错误" });

    var configJson = JsonSerializer.Serialize(config);

    await using var conn = new SqliteConnection($"Data Source={Path.Combine(app.Environment.ContentRootPath, "data", "tmdb_cache.db")}");
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"UPDATE storage_configs SET config_json = @config, updated_at = datetime('now') 
                        WHERE user_id = @userId";
    cmd.Parameters.AddWithValue("@config", configJson);
    cmd.Parameters.AddWithValue("@userId", int.Parse(userId));

    await cmd.ExecuteNonQueryAsync();
    return Results.Ok(new { success = true });
}).RequireAuthorization();

// 导出存储配置
app.MapGet("/api/storage-config/export", async (HttpRequest req, IDbCache cache) =>
{
    var userId = req.HttpContext.User.FindFirst("userId")?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    await using var conn = new SqliteConnection($"Data Source={Path.Combine(app.Environment.ContentRootPath, "data", "tmdb_cache.db")}");
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT config_json FROM storage_configs WHERE user_id = @userId";
    cmd.Parameters.AddWithValue("@userId", int.Parse(userId));

    var configJson = await cmd.ExecuteScalarAsync() as string;
    if (string.IsNullOrEmpty(configJson))
        return Results.BadRequest(new { success = false, error = "配置不存在" });

    return Results.File(System.Text.Encoding.UTF8.GetBytes(configJson), "application/json", "storage-config.json");
}).RequireAuthorization();


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

static string GenerateSalt()
{
    var bytes = RandomNumberGenerator.GetBytes(16);
    return Convert.ToBase64String(bytes);
}

static string HashPassword(string password, string salt)
{
    using var sha256 = SHA256.Create();
    var bytes = Encoding.UTF8.GetBytes(salt + password);
    var hash = sha256.ComputeHash(bytes);
    return Convert.ToBase64String(hash);
}

static string GenerateJwtToken(string username, int userId, string role)
{
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
        Environment.GetEnvironmentVariable("JWT_SECRET") ?? "zhuiying-dev-secret-key-change-in-production-2026"));
    var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var claims = new[]
    {
        new Claim(ClaimTypes.Name, username),
        new Claim("userId", userId.ToString()),
        new Claim(ClaimTypes.Role, role)
    };
    var token = new JwtSecurityToken(
        issuer: "zhuiying-hub",
        audience: "zhuiying-main-site",
        claims: claims,
        expires: DateTime.UtcNow.AddDays(7),
        signingCredentials: credentials);
    return new JwtSecurityTokenHandler().WriteToken(token);
}

// ====== 类型声明 (Top-level 规范: 必须放最后) ======
app.Run();
public record RegisterRequest(string Username, string Password, string? Email = null);
public record LoginRequest(string Username, string Password);
public record FavoriteRequest(int TmdbId, string? MediaType = null, string? Title = null, string? PosterPath = null);

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
    public IHttpClientFactory HttpFactory => _factory;
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
            Sources.Add(new SourceConfig { Name = "PanSou", ApiUrl = "http://pansou:8888/api/search", Enabled = true, Type = "pansou" });
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
                using var stream = await res.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                var raw = await reader.ReadToEndAsync();
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

// ====== 后台定时搜索服务 ======
public class FavoriteSearchWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<FavoriteSearchWorker> _logger;

    public FavoriteSearchWorker(IServiceProvider services, ILogger<FavoriteSearchWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FavoriteSearchWorker 启动，每小时搜索一次");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var hubService = scope.ServiceProvider.GetRequiredService<HubService>();

                var dbPath = Path.Combine(
                    scope.ServiceProvider.GetRequiredService<IHostEnvironment>().ContentRootPath,
                    "data", "tmdb_cache.db");

                await using var conn = new SqliteConnection($"Data Source={dbPath}");
                await conn.OpenAsync();

                // 查找超过24小时没有搜索结果的收藏
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT DISTINCT f.tmdb_id, f.media_type, f.title FROM favorites f
                    WHERE NOT EXISTS (
                        SELECT 1 FROM search_results sr 
                        WHERE sr.tmdb_id = f.tmdb_id AND sr.media_type = f.media_type
                        AND sr.found_at > datetime('now', '-24 hours')
                    )
                    ORDER BY f.added_at ASC";

                var favoritesToSearch = new List<(int TmdbId, string MediaType, string Title)>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    favoritesToSearch.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
                }
                reader.Close();

                if (favoritesToSearch.Count == 0)
                {
                    _logger.LogDebug("没有待搜索的收藏");
                }
                else
                {
                    _logger.LogInformation("开始搜索 {Count} 个收藏的链接", favoritesToSearch.Count);

                    foreach (var (tmdbId, mediaType, title) in favoritesToSearch)
                    {
                        if (string.IsNullOrEmpty(title)) continue;

                        _logger.LogInformation("搜索收藏 #{TmdbId}: {Title}", tmdbId, title);
                        var searchResult = await hubService.SearchAsync(title);

                        using var insertCmd = conn.CreateCommand();
                        foreach (var source in searchResult.Results)
                        {
                            foreach (var item in source.Items)
                            {
                                if (item == null) continue;
                                var obj = item.AsObject();
                                var url = obj.ContainsKey("url") ? obj["url"]?.GetValue<string>() : "";
                                if (string.IsNullOrEmpty(url)) continue;

                                insertCmd.CommandText = @"
                                    INSERT OR IGNORE INTO search_results (tmdb_id, media_type, source, cloud_type, title, url, password)
                                    VALUES (@tmdbId, @mediaType, @source, @cloudType, @title, @url, @password)";
                                insertCmd.Parameters.Clear();
                                insertCmd.Parameters.AddWithValue("@tmdbId", tmdbId);
                                insertCmd.Parameters.AddWithValue("@mediaType", mediaType);
                                insertCmd.Parameters.AddWithValue("@source", source.Name);
                                insertCmd.Parameters.AddWithValue("@cloudType", obj.ContainsKey("cloud_type") ? obj["cloud_type"]?.GetValue<string>() : "");
                                insertCmd.Parameters.AddWithValue("@title", obj.ContainsKey("title") ? obj["title"]?.GetValue<string>() : "");
                                insertCmd.Parameters.AddWithValue("@url", url);
                                insertCmd.Parameters.AddWithValue("@password", obj.ContainsKey("password") ? obj["password"]?.GetValue<string>() : null);
                                await insertCmd.ExecuteNonQueryAsync();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "后台搜索出错");
            }

            // 每小时搜索一次
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}

