using Microsoft.JSInterop;
using System.Text.Json;

namespace Zhuiying.MainSite.Services;

public class CacheService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly TimeSpan _defaultExpiry = TimeSpan.FromHours(2);

    public CacheService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var cacheKey = $"cache_{key}";
            var cached = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", cacheKey);
            
            if (string.IsNullOrEmpty(cached))
                return default;

            var cacheEntry = JsonSerializer.Deserialize<CacheEntry<T>>(cached);
            if (cacheEntry == null)
                return default;

            // 检查是否过期
            if (DateTime.UtcNow > cacheEntry.ExpiresAt)
            {
                await RemoveAsync(key);
                return default;
            }

            return cacheEntry.Data;
        }
        catch
        {
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T data, TimeSpan? expiry = null)
    {
        try
        {
            var cacheKey = $"cache_{key}";
            var entry = new CacheEntry<T>
            {
                Data = data,
                ExpiresAt = DateTime.UtcNow.Add(expiry ?? _defaultExpiry)
            };

            var json = JsonSerializer.Serialize(entry);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", cacheKey, json);
        }
        catch
        {
            // 缓存失败不影响主流程
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            var cacheKey = $"cache_{key}";
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", cacheKey);
        }
        catch
        {
            // 忽略错误
        }
    }

    public async Task ClearAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.clear");
        }
        catch
        {
            // 忽略错误
        }
    }

    public async Task SetRawAsync(string key, string value, TimeSpan? expiry = null)
    {
        try
        {
            Console.WriteLine($"[Cache] SetRawAsync: key={key}, value length={value?.Length ?? 0}");
            var expiryTimestamp = DateTime.UtcNow.Add(expiry ?? _defaultExpiry).Ticks;
            var json = JsonSerializer.Serialize(new { value, expiry = expiryTimestamp });
            Console.WriteLine($"[Cache] SetRawAsync JSON: {json.Substring(0, Math.Min(100, json.Length))}...");
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", key, json);
            Console.WriteLine($"[Cache] SetRawAsync: saved to localStorage");
        }
        catch (Exception ex) { Console.WriteLine($"[Cache] SetRawAsync error: {ex.Message}"); }
    }

    public async Task<string?> GetRawAsync(string key)
    {
        try
        {
            Console.WriteLine($"[Cache] GetRawAsync: key={key}");
            var cached = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", key);
            Console.WriteLine($"[Cache] GetRawAsync: raw value length={cached?.Length ?? 0}");
            if (string.IsNullOrEmpty(cached)) return null;

            var entry = JsonSerializer.Deserialize<RawEntry>(cached);
            if (entry == null) return null;

            Console.WriteLine($"[Cache] GetRawAsync: expiry={entry.Expiry}, now={DateTime.UtcNow.Ticks}, expired={DateTime.UtcNow.Ticks > entry.Expiry}");

            if (DateTime.UtcNow.Ticks > entry.Expiry)
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", key);
                return null;
            }
            Console.WriteLine($"[Cache] GetRawAsync: returning value length={entry.Value?.Length ?? 0}");
            return entry.Value;
        }
        catch (Exception ex) { Console.WriteLine($"[Cache] GetRawAsync error: {ex.Message}"); return null; }
    }

    public async Task RemoveRawAsync(string key)
    {
        try { await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", key); } catch { }
    }

    private class RawEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public string? Value { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("expiry")]
        public long Expiry { get; set; }
    }

    private class CacheEntry<T>
    {
        public T? Data { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
