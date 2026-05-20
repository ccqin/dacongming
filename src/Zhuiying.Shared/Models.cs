namespace Zhuiying.Shared;

/// <summary>
/// 从 TG Bot 推送给主站的消息模型
/// </summary>
public record TgMessageDto(
    long ChatId,
    string Username,
    string Text,
    DateTime ReceivedAt
);

/// <summary>
/// 影视基本信息
/// </summary>
public record MovieDto(
    int Id,
    string Title,
    string? Overview,
    string? PosterPath,
    double VoteAverage,
    string ReleaseDate,
    string MediaType // "movie" | "tv"
);

/// <summary>
/// TMDB 搜索结果
/// </summary>
public record TmdbSearchResult(
    bool Success,
    List<MovieDto> Data,
    int Total,
    string? Query = null
);
