using Zhuiying.Shared;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// ================= Configuration =================
var hubUrl = Environment.GetEnvironmentVariable("ZhuiyingHubUrl") ?? "http://zhuiying-hub:5002";
builder.Services.AddHttpClient("Hub", c => 
{ 
    c.BaseAddress = new Uri(hubUrl);
    c.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddCors(c => c.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// ================= Middleware =================
app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();

var hubClient = app.Services.CreateScope().ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Hub");

// ================= Category Routing Config =================
// Maps frontend category parameter to Hub API path (aligned with REST API v1.0.0)
var categoryRoutes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    { "trending", "/api/movie/trending" },
    { "upcoming", "/api/movie/latest" },
    { "latest", "/api/movie/latest" },
    { "top_rated", "/api/movie/trending" }, // Hub 暂未开放 top_rated，暂用 trending 替代
};

// ================= Helper Methods =================

/// <summary>
/// Forward request to Hub and transform response to frontend-compatible format.
/// Hub: {"success": true, "data": [...], "error": null}
/// Frontend expects: {"page": 1, "results": [...]}
/// </summary>
static async Task<IResult> ProxyHubAndTransform(IHttpClientFactory factory, string hubPath, HttpRequest? originalReq = null)
{
    try
    {
        var client = factory.CreateClient("Hub");
        var request = new HttpRequestMessage(HttpMethod.Get, hubPath);
        
        // Forward authorization headers if present
        if (originalReq?.Headers.TryGetValue("Authorization", out var auth) == true)
        {
            request.Headers.Add("Authorization", auth.ToString());
        }

        Console.WriteLine($"[MainSite] → Hub GET {hubPath}");
        var resp = await client.SendAsync(request);
        
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync();
            Console.WriteLine($"[MainSite] ← Hub Error {resp.StatusCode}: {errorBody}");
            return Results.StatusCode((int)resp.StatusCode);
        }

        var body = await resp.Content.ReadAsStringAsync();
        return TransformHubResponse(body);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[MainSite] Exception: {ex.Message}");
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
}

/// <summary>
/// Transform Hub response format to TMDB-like format for frontend compatibility.
/// </summary>
static IResult TransformHubResponse(string body)
{
    try
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("success", out var successEl) && successEl.GetBoolean())
        {
            if (doc.RootElement.TryGetProperty("data", out var dataEl))
            {
                var rawJson = dataEl.GetRawText();
                // If data is an array, wrap it
                if (dataEl.ValueKind == JsonValueKind.Array)
                {
                    var responseJson = $"{{\"page\":1,\"results\":{rawJson}}}";
                    return Results.Content(responseJson, "application/json");
                }
                // If data is an object (e.g. detail endpoint), return as-is with page wrapper
                if (dataEl.ValueKind == JsonValueKind.Object)
                {
                    var responseJson = $"{{\"page\":1,\"results\":[{rawJson}]}}";
                    return Results.Content(responseJson, "application/json");
                }
            }
        }
        // If already in frontend format, return as-is
        if (doc.RootElement.TryGetProperty("results", out _))
        {
            return Results.Content(body, "application/json");
        }
    }
    catch { /* Fallback */ }
    
    return Results.Content(body, "application/json");
}

// ================= API ROUTES =================

// 1. Movie Categories (GET /api/movies?category=trending|upcoming|top_rated&type=movie|tv&page=N)
app.MapGet("/api/movies", async (string? category, string? type, int page = 1, [FromServices] IHttpClientFactory? factory = null) =>
{
    if (factory == null) return Results.Problem("Internal error", statusCode: 500);
    
    var routeKey = string.IsNullOrEmpty(category) ? "trending" : category.ToLower();
    if (!categoryRoutes.TryGetValue(routeKey, out var hubPath))
    {
        return Results.BadRequest(new { error = $"不支持的分类：{category}" });
    }

    // Build query string for Hub
    var queryParams = new List<string>();
    if (!string.IsNullOrEmpty(type)) queryParams.Add($"type={Uri.EscapeDataString(type)}");
    queryParams.Add($"page={page}");
    
    var fullPath = $"{hubPath}?{string.Join("&", queryParams)}";
    return await ProxyHubAndTransform(factory, fullPath);
});

// 2. Movie Detail (GET /api/movies/{id}?type=movie|tv)
app.MapGet("/api/movies/{id}", async (int id, string? type = "movie", [FromServices] IHttpClientFactory? factory = null) =>
{
    if (factory == null) return Results.Problem("Internal error", statusCode: 500);
    
    var hubPath = $"/api/movie/{id}?type={Uri.EscapeDataString(type ?? "movie")}";
    return await ProxyHubAndTransform(factory, hubPath);
});

// 3. Search (POST /api/search or GET /api/search?q=keyword)
// Hub API: POST /api/search with {"keyword": "...", "cloudTypes": [...], "forceRefresh": false}
app.MapPost("/api/search", async (SearchReq req, [FromServices] IHttpClientFactory? factory = null) =>
{
    if (factory == null) return Results.Problem("Internal error", statusCode: 500);
    
    if (string.IsNullOrWhiteSpace(req.Keyword))
    {
        return Results.BadRequest(new { success = false, data = (object?)null, error = "关键词不能为空" });
    }

    var searchBody = JsonSerializer.Serialize(new 
    { 
        keyword = req.Keyword,
        cloudTypes = req.CloudTypes ?? Array.Empty<string>(),
        forceRefresh = req.ForceRefresh
    });
    
    var content = new StringContent(searchBody, Encoding.UTF8, "application/json");
    var request = new HttpRequestMessage(HttpMethod.Post, "/api/search") { Content = content };
    
    Console.WriteLine($"[MainSite] → Hub POST /api/search keyword={req.Keyword}");
    var resp = await factory.CreateClient("Hub").SendAsync(request);
    
    if (!resp.IsSuccessStatusCode)
    {
        var errorBody = await resp.Content.ReadAsStringAsync();
        Console.WriteLine($"[MainSite] ← Hub Search Error {resp.StatusCode}: {errorBody}");
        return Results.StatusCode((int)resp.StatusCode);
    }

    var body = await resp.Content.ReadAsStringAsync();
    return Results.Content(body, "application/json");
});

// GET fallback for search: /api/search?q=keyword
app.MapGet("/api/search", async (string? q, [FromServices] IHttpClientFactory? factory = null) =>
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.BadRequest(new { success = false, data = (object?)null, error = "关键词不能为空" });
    }
    
    var req = new SearchReq { Keyword = q };
    // Reuse the POST handler via inline call
    if (factory == null) return Results.Problem("Internal error", statusCode: 500);

    var searchBody = JsonSerializer.Serialize(new { keyword = q, cloudTypes = Array.Empty<string>(), forceRefresh = false });
    var content = new StringContent(searchBody, Encoding.UTF8, "application/json");
    var request = new HttpRequestMessage(HttpMethod.Post, "/api/search") { Content = content };
    
    var resp = await factory.CreateClient("Hub").SendAsync(request);
    var body = await resp.Content.ReadAsStringAsync();
    return Results.Content(body, "application/json");
});

// 4. Subscriptions (Proxy to Hub)
// POST /api/subscribe
app.MapPost("/api/subscribe", async (SubscribeReq req, [FromServices] IHttpClientFactory? factory = null) =>
{
    if (factory == null) return Results.Problem("Internal error", statusCode: 500);

    var body = JsonSerializer.Serialize(new { req.UserId, req.MovieId, req.Keyword });
    var content = new StringContent(body, Encoding.UTF8, "application/json");
    var request = new HttpRequestMessage(HttpMethod.Post, "/api/subscribe") { Content = content };
    
    var resp = await factory.CreateClient("Hub").SendAsync(request);
    var respBody = await resp.Content.ReadAsStringAsync();
    return Results.Content(respBody, "application/json");
});

// GET /api/subscribe/{userId}
app.MapGet("/api/subscribe/{userId}", async (string userId, [FromServices] IHttpClientFactory? factory = null) =>
{
    if (factory == null) return Results.Problem("Internal error", statusCode: 500);
    
    var resp = await factory.CreateClient("Hub").GetAsync($"/api/subscribe/{userId}");
    var body = await resp.Content.ReadAsStringAsync();
    return Results.Content(body, "application/json");
});

// DELETE /api/subscribe/{id}
app.MapDelete("/api/subscribe/{id}", async (int id, HttpRequest req, [FromServices] IHttpClientFactory? factory = null) =>
{
    if (factory == null) return Results.Problem("Internal error", statusCode: 500);
    
    var userId = req.Query["userId"].ToString();
    if (string.IsNullOrEmpty(userId))
    {
        return Results.BadRequest(new { success = false, data = (object?)null, error = "userId 参数不能为空" });
    }
    
    var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/subscribe/{id}?userId={Uri.EscapeDataString(userId)}");
    var resp = await factory.CreateClient("Hub").SendAsync(request);
    var body = await resp.Content.ReadAsStringAsync();
    return Results.Content(body, "application/json");
});

// 5. Douban Data (GET /api/movie/douban/{doubanId})
app.MapGet("/api/movie/douban/{doubanId}", async (string doubanId, [FromServices] IHttpClientFactory? factory = null) =>
{
    if (factory == null) return Results.Problem("Internal error", statusCode: 500);
    
    return await ProxyHubAndTransform(factory, $"/api/movie/douban/{doubanId}");
});

// 6. Health Check (Proxy)
app.MapGet("/api/health", async ([FromServices] IHttpClientFactory? factory = null) =>
{
    if (factory == null) return Results.Problem("Internal error", statusCode: 500);
    
    return await ProxyHubAndTransform(factory, "/api/health");
});

// ================= Auth (Simplified) =================
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

// ================= Admin Proxy (Forward to Hub) =================
app.MapGet("/api/admin/{**path}", async (string path, HttpRequest req, [FromServices] IHttpClientFactory? factory = null) =>
{
    if (factory == null) return Results.Problem("Internal error", statusCode: 500);
    return await ProxyHubAndTransform(factory, $"/api/admin/{path}", req);
});

// ================= Fallback =================
app.MapFallbackToFile("index.html");

app.Run();

// ================= Request/Response Records =================
public record LoginReq(string Username, string Password);
public record SearchReq(string Keyword, string[]? CloudTypes, bool ForceRefresh)
{
    public SearchReq() : this(string.Empty, Array.Empty<string>(), false) { }
};
public record SubscribeReq(string UserId, int MovieId, string? Keyword)
{
    public SubscribeReq() : this(string.Empty, 0, null) { }
};
