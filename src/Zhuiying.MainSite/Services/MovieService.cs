using System.Net.Http.Json;
using Zhuiying.MainSite.Models;

namespace Zhuiying.MainSite.Services;

public class MovieService
{
    private readonly HttpClient _http;
    private const string ImageBaseUrl = "https://image.tmdb.org/t/p/";

    public MovieService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<Movie>> GetTrendingMoviesAsync(string type = "movie", string? region = null, int page = 1)
    {
        var url = $"/api/movie/trending?type={type}&page={page}";
        if (!string.IsNullOrEmpty(region))
            url += $"&region={region}";

        var response = await _http.GetFromJsonAsync<ApiResponse<List<Movie>>>(url);
        return response?.Success == true ? response.Data ?? new List<Movie>() : new List<Movie>();
    }

    public async Task<List<Movie>> GetLatestMoviesAsync(string type = "movie", int page = 1)
    {
        var url = $"/api/movie/latest?type={type}&page={page}";
        var response = await _http.GetFromJsonAsync<ApiResponse<List<Movie>>>(url);
        return response?.Success == true ? response.Data ?? new List<Movie>() : new List<Movie>();
    }

    public async Task<Movie?> GetMovieDetailsAsync(int id, string type = "movie")
    {
        var url = $"/api/movie/{id}?type={type}";
        var response = await _http.GetFromJsonAsync<ApiResponse<Movie>>(url);
        return response?.Success == true ? response.Data : null;
    }

    public static string GetPosterUrl(string? posterPath, string size = "w500")
    {
        if (string.IsNullOrEmpty(posterPath))
            return "/images/placeholder-poster.svg";
        
        return $"{ImageBaseUrl}{size}{posterPath}";
    }

    public static string GetBackdropUrl(string? backdropPath, string size = "original")
    {
        if (string.IsNullOrEmpty(backdropPath))
            return "/images/placeholder-backdrop.svg";
        
        return $"{ImageBaseUrl}{size}{backdropPath}";
    }
}
