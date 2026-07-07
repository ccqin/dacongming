namespace Zhuiying.MainSite.Models;

public class Movie
{
    public int TmdbId { get; set; }
    public string Title { get; set; } = "";
    public string? OriginalTitle { get; set; }
    public string? Overview { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public double TmdbVoteAverage { get; set; }
    public int TmdbVoteCount { get; set; }
    public string MediaType { get; set; } = "movie";
    public string? ReleaseDate { get; set; }
    public int? Runtime { get; set; }
    public string? OriginalLanguage { get; set; }
    public string? Genres { get; set; }
    public string? ProductionCountries { get; set; }
    public string? Credits { get; set; }
    public string? Similar { get; set; }
    public string? Videos { get; set; }
    public DoubanInfo? Douban { get; set; }
}

public class DoubanInfo
{
    public string DoubanId { get; set; } = "";
    public string Title { get; set; } = "";
    public double Rating { get; set; }
    public int RatingCount { get; set; }
    public string? Summary { get; set; }
    public string? ImageUrl { get; set; }
}

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }
}

public class MovieListResponse
{
    public int Page { get; set; }
    public List<Movie> Results { get; set; } = new();
}
