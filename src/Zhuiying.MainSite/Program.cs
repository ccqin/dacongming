using Zhuiying.Shared;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Configuration
// Use environment variable, fallback to default
var hubUrl = Environment.GetEnvironmentVariable("ZhuiyingHubUrl") ?? "http://zhuiying-hub:5002";
builder.Services.AddHttpClient("Hub", c => { c.BaseAddress = new Uri(hubUrl); });

builder.Services.AddCors(c => c.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// Middleware
app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();

var hubClient = app.Services.CreateScope().ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Hub");

// ================= API ROUTES =================

// 1. Movies (Proxy to Hub - Aligned with API.md)
app.MapGet("/api/movies", async (string? category = null) =>
{
    try 
    {
        // Align with API.md provided by Meiguoxiaola
        // API.md endpoints: /api/tmdb/trending, /api/tmdb/discover, /api/tmdb/search
        var path = category switch
        {
            "trending" => "/api/movie/trending?language=zh-CN",
            "upcoming" => "/api/movie/latest?language=zh-CN",
            "top_rated" => "/api/movie/latest?language=zh-CN", // Hub 暂未开放 top_rated，先用 latest 替代
            "tv" => "/api/movie/trending?language=zh-CN",
            _ => "/api/movie/trending?language=zh-CN"
        };

        Console.WriteLine($"[MainSite] Proxying to Hub: {path}");
        var resp = await hubClient.GetAsync(path);
        if (!resp.IsSuccessStatusCode) 
        {
            Console.WriteLine($"[MainSite] Hub Error: {resp.StatusCode}");
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
