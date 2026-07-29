namespace Zhuiying.Hub.Models;

public class CloudDrive
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Type { get; set; } = "";  // 123/115
    public string? Name { get; set; }
    public string EncryptedCookie { get; set; } = "";
    public string Status { get; set; } = "active";  // active/expired/invalid
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class Transfer
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int DriveId { get; set; }
    public int TmdbId { get; set; }
    public string MediaType { get; set; } = "";  // movie/tv
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public string SourceUrl { get; set; } = "";
    public string? SourceTitle { get; set; }
    public long? FileSize { get; set; }
    public string? Quality { get; set; }
    public string TargetPath { get; set; } = "";
    public string Status { get; set; } = "pending";  // pending/transferring/completed/failed
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class TransferredEpisode
{
    public int Id { get; set; }
    public int TmdbId { get; set; }
    public string MediaType { get; set; } = "";
    public int Season { get; set; }
    public int Episode { get; set; }
    public int DriveId { get; set; }
    public string? FilePath { get; set; }
    public long? FileSize { get; set; }
    public string? Quality { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class StorageConfig
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string ConfigJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
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

public class ShareFileItem
{
    public string FileId { get; set; } = "";
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public string? FileType { get; set; }  // file/folder
    public bool IsFolder => FileType == "folder";
    public List<ShareFileItem> Children { get; set; } = new();
}

public class TransferResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FilePath { get; set; }
    public long FileSize { get; set; }
}
