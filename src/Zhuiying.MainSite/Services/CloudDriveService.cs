using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Zhuiying.MainSite.Services;

public class CloudDriveService
{
    private readonly HttpClient _http;
    private readonly CacheService _cache;
    private const string TokenKey = "auth_token";

    public CloudDriveService(HttpClient http, CacheService cache)
    {
        _http = http;
        _cache = cache;
    }

    private async Task ApplyTokenAsync()
    {
        var token = await _cache.GetRawAsync(TokenKey);
        if (!string.IsNullOrEmpty(token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    public async Task<List<CloudDriveItem>> GetDrivesAsync()
    {
        await ApplyTokenAsync();
        var response = await _http.GetAsync("/api/cloud-drives");
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<CloudDriveItem>>>();
            return result?.Data ?? new List<CloudDriveItem>();
        }
        return new List<CloudDriveItem>();
    }

    public async Task<(bool success, string? error)> AddDriveAsync(string type, string? name, string cookie)
    {
        await ApplyTokenAsync();
        var response = await _http.PostAsJsonAsync("/api/cloud-drives", new { type, name, cookie });
        if (response.IsSuccessStatusCode)
        {
            return (true, null);
        }
        var error = await response.Content.ReadAsStringAsync();
        return (false, error);
    }

    public async Task<bool> DeleteDriveAsync(int id)
    {
        await ApplyTokenAsync();
        var response = await _http.DeleteAsync($"/api/cloud-drives/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<(bool success, string? userInfo)> TestDriveAsync(int id)
    {
        await ApplyTokenAsync();
        var response = await _http.PostAsync($"/api/cloud-drives/{id}/test", null);
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            var success = result.GetProperty("success").GetBoolean();
            var userInfo = result.TryGetProperty("userInfo", out var uiEl) && uiEl.ValueKind == JsonValueKind.String 
                ? uiEl.GetString() : null;
            return (success, userInfo);
        }
        return (false, null);
    }

    public async Task<(bool success, string? error, List<int>? transferIds)> CreateTransferAsync(
        int driveId, int tmdbId, string mediaType, string sourceUrl, 
        string? sourceTitle, int? season, int? episode, string? targetPath)
    {
        await ApplyTokenAsync();
        var response = await _http.PostAsJsonAsync("/api/transfers", new
        {
            driveId, tmdbId, mediaType, sourceUrl, sourceTitle, season, episode, targetPath
        });
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            var data = result.GetProperty("data");
            var transferIds = data.GetProperty("transferIds").EnumerateArray()
                .Select(e => e.GetInt32()).ToList();
            return (true, null, transferIds);
        }
        var error = await response.Content.ReadAsStringAsync();
        return (false, error, null);
    }

    public async Task<List<TransferItem>> GetTransfersAsync(string? status = null, int page = 1, int pageSize = 20)
    {
        await ApplyTokenAsync();
        var url = $"/api/transfers?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(status))
            url += $"&status={status}";
        
        var response = await _http.GetAsync(url);
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<TransferItem>>>();
            return result?.Data ?? new List<TransferItem>();
        }
        return new List<TransferItem>();
    }

    public async Task<bool> RetryTransferAsync(int id)
    {
        await ApplyTokenAsync();
        var response = await _http.PostAsync($"/api/transfers/{id}/retry", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<StoragePathConfig?> GetStorageConfigAsync()
    {
        await ApplyTokenAsync();
        var response = await _http.GetAsync("/api/storage-config");
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<StoragePathConfig>>();
            return result?.Data;
        }
        return null;
    }

    public async Task<bool> UpdateStorageConfigAsync(StoragePathConfig config)
    {
        await ApplyTokenAsync();
        var response = await _http.PutAsJsonAsync("/api/storage-config", config);
        return response.IsSuccessStatusCode;
    }
}

public class CloudDriveItem
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public string? Name { get; set; }
    public string Status { get; set; } = "active";
    public string? ExpiresAt { get; set; }
    public string CreatedAt { get; set; } = "";
}

public class TransferItem
{
    public int Id { get; set; }
    public int DriveId { get; set; }
    public int TmdbId { get; set; }
    public string MediaType { get; set; } = "";
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public string SourceUrl { get; set; } = "";
    public string? SourceTitle { get; set; }
    public long? FileSize { get; set; }
    public string TargetPath { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? CompletedAt { get; set; }
    public string? DriveType { get; set; }
}

public class StoragePathConfig
{
    public string MoviePath { get; set; } = "";
    public string TvPath { get; set; } = "";
    public string[] TemplateVariables { get; set; } = new[]
    {
        "{title}", "{year}", "{tmdb_id}", "{season}", "{quality}", "{subtitle}", "{genre}", "{filename}"
    };
}

