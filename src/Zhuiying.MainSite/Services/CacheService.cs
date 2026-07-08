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

    private class CacheEntry<T>
    {
        public T? Data { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
