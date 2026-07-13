using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zhuiying.MainSite.Models;

namespace Zhuiying.MainSite.Services;

public class AuthService
{
    private readonly HttpClient _http;
    private readonly CacheService _cache;
    private const string TokenKey = "auth_token";
    private const string UserKey = "auth_user";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AuthService(HttpClient http, CacheService cache)
    {
        _http = http;
        _cache = cache;
    }

    public async Task<ApiResponse<AuthResult>> LoginAsync(string username, string password)
    {
        Console.WriteLine($"[Auth] LoginAsync called with username={username}");
        var response = await _http.PostAsJsonAsync("/api/auth/login", new { username, password });
        Console.WriteLine($"[Auth] Login response status: {(int)response.StatusCode}");

        var content = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"[Auth] Login response content: {content.Substring(0, Math.Min(200, content.Length))}");

        var result = JsonSerializer.Deserialize<ApiResponse<AuthResult>>(content, JsonOptions);
        Console.WriteLine($"[Auth] Login result success={result?.Success}, hasData={result?.Data != null}, token={result?.Data?.Token?.Substring(0, Math.Min(20, result.Data?.Token?.Length ?? 0))}");

        if (result?.Success == true && result.Data != null)
        {
            Console.WriteLine($"[Auth] Login successful, token length={result.Data.Token?.Length ?? 0}");
            await _cache.SetRawAsync(TokenKey, result.Data.Token ?? "", TimeSpan.FromDays(7));
            await _cache.SetRawAsync(UserKey, JsonSerializer.Serialize(result.Data, JsonOptions), TimeSpan.FromDays(7));
            ApplyToken(result.Data.Token ?? "");
            Console.WriteLine($"[Auth] Token saved to cache");
        }
        return result ?? new ApiResponse<AuthResult> { Success = false, Error = "登录失败" };
    }

    public async Task<ApiResponse<AuthResult>> RegisterAsync(string username, string password, string? email = null)
    {
        var response = await _http.PostAsJsonAsync("/api/auth/register", new { username, password, email });
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ApiResponse<AuthResult>>(content, JsonOptions);
        if (result?.Success == true && result.Data != null)
        {
            await _cache.SetRawAsync(TokenKey, result.Data.Token ?? "", TimeSpan.FromDays(7));
            await _cache.SetRawAsync(UserKey, JsonSerializer.Serialize(result.Data, JsonOptions), TimeSpan.FromDays(7));
            ApplyToken(result.Data.Token ?? "");
        }
        return result ?? new ApiResponse<AuthResult> { Success = false, Error = "注册失败" };
    }

    public async Task LogoutAsync()
    {
        await _cache.RemoveRawAsync(TokenKey);
        await _cache.RemoveRawAsync(UserKey);
        _http.DefaultRequestHeaders.Authorization = null;
    }

    public async Task<AuthResult?> GetCurrentUserAsync()
    {
        var token = await _cache.GetRawAsync(TokenKey);
        Console.WriteLine($"[Auth] Token from cache: {(string.IsNullOrEmpty(token) ? "null" : token.Substring(0, Math.Min(20, token.Length)))}...");

        if (string.IsNullOrEmpty(token)) return null;

        // 尝试从缓存获取用户信息
        var cachedUser = await _cache.GetRawAsync(UserKey);
        Console.WriteLine($"[Auth] Cached user: {(string.IsNullOrEmpty(cachedUser) ? "null" : "exists")}");

        if (!string.IsNullOrEmpty(cachedUser))
        {
            try { return JsonSerializer.Deserialize<AuthResult>(cachedUser, JsonOptions); } catch { }
        }

        // 从 API 获取
        Console.WriteLine($"[Auth] Calling /api/auth/me...");
        ApplyToken(token);
        var response = await _http.GetAsync("/api/auth/me");
        Console.WriteLine($"[Auth] API response: {(int)response.StatusCode}");

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ApiResponse<AuthResult>>(content, JsonOptions);
            Console.WriteLine($"[Auth] API result success: {result?.Success}");
            return result?.Data;
        }
        return null;
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        var token = await _cache.GetRawAsync(TokenKey);
        return !string.IsNullOrEmpty(token);
    }

    private void ApplyToken(string token)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}

public class AuthResult
{
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string? Role { get; set; }
    public string? Token { get; set; }
}
