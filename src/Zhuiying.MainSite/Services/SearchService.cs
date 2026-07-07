using System.Net.Http.Json;
using Zhuiying.MainSite.Models;

namespace Zhuiying.MainSite.Services;

public class SearchService
{
    private readonly HttpClient _http;

    public SearchService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<Movie>> SearchMoviesAsync(string keyword, string type = "movie", int page = 1)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return new List<Movie>();

        var url = $"/api/search/movie?keyword={Uri.EscapeDataString(keyword)}&type={type}&page={page}";
        var response = await _http.GetFromJsonAsync<ApiResponse<List<Movie>>>(url);
        
        return response?.Success == true ? response.Data ?? new List<Movie>() : new List<Movie>();
    }

    public async Task<List<Movie>> SearchTvShowsAsync(string keyword, int page = 1)
    {
        return await SearchMoviesAsync(keyword, "tv", page);
    }
}
