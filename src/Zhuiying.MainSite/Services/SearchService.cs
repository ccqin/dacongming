using System.Net.Http.Json;
using Zhuiying.MainSite.Models;

namespace Zhuiying.MainSite.Services;

public class SearchService
{
    private readonly HttpClient _http;
    private readonly CacheService _cache;

    public SearchService(HttpClient http, CacheService cache)
    {
        _http = http;
        _cache = cache;
    }

    public async Task<List<Movie>> SearchMoviesAsync(string keyword, string type = "movie", int page = 1)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return new List<Movie>();

        var cacheKey = $"search_{keyword}_{type}_{page}";
        var cached = await _cache.GetAsync<List<Movie>>(cacheKey);
        if (cached != null)
            return cached;

        var url = $"/api/search/movie?keyword={Uri.EscapeDataString(keyword)}&type={type}&page={page}";
        var response = await _http.GetFromJsonAsync<ApiResponse<List<Movie>>>(url);
        var result = response?.Success == true ? response.Data ?? new List<Movie>() : new List<Movie>();
        
        if (result.Any())
            await _cache.SetAsync(cacheKey, result);
        
        return result;
    }

    public async Task<List<Movie>> SearchTvShowsAsync(string keyword, int page = 1)
    {
        return await SearchMoviesAsync(keyword, "tv", page);
    }
}
