using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Zhuiying.MainSite.Models;

namespace Zhuiying.MainSite.Services;

public class FavoritesService
{
    private readonly HttpClient _http;
    private readonly CacheService _cache;
    private const string TokenKey = "auth_token";

    public FavoritesService(HttpClient http, CacheService cache)
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

    public async Task<bool> AddFavoriteAsync(int tmdbId, string mediaType, string? title = null, string? posterPath = null)
    {
        await ApplyTokenAsync();
        var response = await _http.PostAsJsonAsync("/api/favorites", new { tmdbId, mediaType, title, posterPath });
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveFavoriteAsync(int tmdbId, string mediaType)
    {
        await ApplyTokenAsync();
        var response = await _http.DeleteAsync($"/api/favorites/{tmdbId}/{mediaType}");
        return response.IsSuccessStatusCode;
    }

    public async Task<List<FavoriteItem>> GetFavoritesAsync()
    {
        await ApplyTokenAsync();
        var response = await _http.GetAsync("/api/favorites");
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<FavoriteItem>>>();
            return result?.Data ?? new List<FavoriteItem>();
        }
        return new List<FavoriteItem>();
    }

    public async Task<List<FavoriteLink>> GetFavoriteLinksAsync(int favoriteId)
    {
        await ApplyTokenAsync();
        var response = await _http.GetAsync($"/api/favorites/{favoriteId}/links");
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<FavoriteLink>>>();
            return result?.Data ?? new List<FavoriteLink>();
        }
        return new List<FavoriteLink>();
    }

    public async Task<bool> SearchLinksAsync(int favoriteId)
    {
        await ApplyTokenAsync();
        var response = await _http.PostAsync($"/api/favorites/{favoriteId}/search", null);
        return response.IsSuccessStatusCode;
    }


    public async Task<List<FavoriteLink>> GetLinksByTmdbIdAsync(int tmdbId, string mediaType)
    {
        await ApplyTokenAsync();
        var response = await _http.GetAsync($"/api/search/links/{tmdbId}/{mediaType}");
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<FavoriteLink>>>();
            return result?.Data ?? new List<FavoriteLink>();
        }
        return new List<FavoriteLink>();
    }

    public async Task<bool> SearchLinksByTmdbIdAsync(int tmdbId, string mediaType)
    {
        await ApplyTokenAsync();
        var response = await _http.PostAsync($"/api/search/links/{tmdbId}/{mediaType}", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> IsFavoriteAsync(int tmdbId, string mediaType)
    {
        var favorites = await GetFavoritesAsync();
        return favorites.Any(f => f.TmdbId == tmdbId && f.MediaType == mediaType);
    }
}

public class FavoriteItem
{
    public int Id { get; set; }
    public int TmdbId { get; set; }
    public string MediaType { get; set; } = "movie";
    public string Title { get; set; } = "";
    public string PosterPath { get; set; } = "";
    public string AddedAt { get; set; } = "";
    public int LinkCount { get; set; }
}

public class FavoriteLink
{
    public int Id { get; set; }
    public string Source { get; set; } = "";
    public string CloudType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Password { get; set; }
    public string FoundAt { get; set; } = "";
}
