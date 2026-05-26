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
            "trending" => "/api/tmdb/trending?language=zh-CN",
            "upcoming" => "/api/tmdb/discover?type=movie&language=zh-CN&sort_by=popularity.desc", // Fallback to discover
            "top_rated" => "/api/tmdb/discover?type=movie&language=zh-CN&sort_by=vote_average.desc",
            "tv" => "/api/tmdb/discover?type=tv&language=zh-CN&sort_by=popularity.desc",
            _ => "/api/tmdb/trending?language=zh-CN"
        };

        Console.WriteLine($"[MainSite] Proxying to Hub: {path}");
        var resp = await hubClient.GetAsync(path);
        if (!resp.IsSuccessStatusCode) 
        {
            Console.WriteLine($"[MainSite] Hub Error: {resp.StatusCode}");
            return Results.StatusCode((int)resp.StatusCode);
        }

        var body = await resp.Content.ReadAsStringAsync();
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
            hubReq.Headers.Add("Authorization", auth);
        }

        var resp = await hubClient.SendAsync(hubReq);
        if (!resp.IsSuccessStatusCode) 
        {
            return Results.StatusCode((int)resp.StatusCode);
        }
        var body = await resp.Content.ReadAsStringAsync();
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
