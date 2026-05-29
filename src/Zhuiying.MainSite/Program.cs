using Zhuiying.Shared;
using System.Security.Claims;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var hubUrl = Environment.GetEnvironmentVariable("ZhuiyingHubUrl") ?? "http://zhuiying-hub:5002";
builder.Services.AddHttpClient("Hub", c => { 
    c.BaseAddress = new Uri(hubUrl);
    c.Timeout = TimeSpan.FromSeconds(15); // 增加超时设置
});

builder.Services.AddCors(c => c.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// Middleware
app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();

var hubClient = app.Services.CreateScope().ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Hub");

// 路由配置 (以后根据新 API 文档在此处更新)
var categoryRoutes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    { "trending", "/api/movie/trending?language=zh-CN" },
    { "upcoming", "/api/movie/latest?language=zh-CN" },
    { "top_rated", "/api/movie/latest?language=zh-CN" }, // 临时映射，等待 Hub 更新 top_rated 接口
    { "tv", "/api/movie/trending?language=zh-CN&type=tv" }
};

// ================= API ROUTES =================

// 1. Movies (Proxy to Hub)
app.MapGet("/api/movies", async (string? category = null) =>
{
    try 
    {
        var routeKey = string.IsNullOrEmpty(category) ? "trending" : category.ToLower();
        
        if (!categoryRoutes.TryGetValue(routeKey, out var path))
        {
            return Results.BadRequest(new { error = $"不支持的分类：{category}" });
        }

        Console.WriteLine($"[MainSite] Proxying to Hub: {path}");
        
        // 创建请求并添加自定义头（如果需要）
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        // 可以在这里添加认证 Token 等
        
        var resp = await hubClient.SendAsync(request);
        
        if (!resp.IsSuccessStatusCode) 
        {
            Console.WriteLine($"[MainSite] Hub Error: {resp.StatusCode} - {await resp.Content.ReadAsStringAsync()}");
            return Results.StatusCode((int)resp.StatusCode);
        }

        var body = await resp.Content.ReadAsStringAsync();
        
        // Transform Hub response format to TMDB-like format for frontend compatibility
        try 
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                var resultsJson = dataElement.GetRawText();
                var responseJson = $"{{\"page\":1,\"results\":{resultsJson}}}";
                return Results.Content(responseJson, "application/json");
            }
            // 如果已经是 {results: ...} 格式，直接返回
            if (doc.RootElement.TryGetProperty("results", out _))
            {
                return Results.Content(body, "application/json");
            }
        }
        catch { /* Fallback to original body if parsing fails */ }
        
        return Results.Content(body, "application/json");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[MainSite] Exception: {ex.Message}");
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// 2. Auth (Simplified)
var tokens = new Dictionary<string, (string User, DateTime Expires)>();

app.MapPost("/api/auth/login", (LoginReq req) =>
{
    if ((req.Username == "ccqin" && req.Password == "bukeshuo123!") || 
        (req.Username == "admin" && req.Password == "admin"))
    {
        var token = Guid.NewGuid().ToString();
        tokens[token] = (req.Username, DateTime.UtcNow.AddDays(7));
        return Results.Ok(new { Token = token, Username = req.Username });
    }
    return Results.Unauthorized();
});

app.MapGet("/api/me", (HttpRequest req) =>
{
    var auth = req.Headers["Authorization"].ToString().Replace("Bearer ", "");
    if (tokens.TryGetValue(auth, out var user) && user.Expires > DateTime.UtcNow)
    {
        return Results.Ok(new { Username = user.User, IsAdmin = user.User == "ccqin" });
    }
    return Results.Unauthorized();
});

// 3. Admin Proxy (Forward to Hub)
app.MapGet("/api/admin/{**path}", async (string path, HttpRequest req) =>
{
    try
    {
        var targetUrl = $"/api/admin/{path}";
        Console.WriteLine($"[MainSite] Admin Proxying to Hub: {targetUrl}");
        
        // Clone headers (Authorization etc)
        var hubReq = new HttpRequestMessage(HttpMethod.Get, targetUrl);
        if (req.Headers.TryGetValue("Authorization", out var auth))
        {
            hubReq.Headers.Add("Authorization", auth.ToString());
        }

        var resp = await hubClient.SendAsync(hubReq);
        if (!resp.IsSuccessStatusCode) 
        {
            return Results.StatusCode((int)resp.StatusCode);
        }
        var body = await resp.Content.ReadAsStringAsync();
        
        // Transform Hub response format to TMDB-like format for frontend compatibility
        // Hub: {"success":true, "data":[...]} -> TMDB: {"results":[...], "page":1}
        try 
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                // Extract raw JSON string of the data array to avoid disposal issues
                var resultsJson = dataElement.GetRawText();
                var responseJson = $"{{\"page\":1,\"results\":{resultsJson}}}";
                return Results.Content(responseJson, "application/json");
            }
        }
        catch { /* Fallback to original body if parsing fails */ }
        
        return Results.Content(body, "application/json");
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// 4. Fallback SPA Routing
app.MapFallbackToFile("index.html");

app.Run();

public record LoginReq(string Username, string Password);
