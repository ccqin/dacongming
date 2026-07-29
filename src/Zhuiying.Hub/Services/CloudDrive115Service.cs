using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Zhuiying.Hub.Models;

namespace Zhuiying.Hub.Services;

public class CloudDrive115Service : CloudDriveBase
{
    private readonly HttpClient _httpClient;
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/110.0.0.0 Safari/537.36";

    public override string DriveType => "115";

    public CloudDrive115Service(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public override async Task<bool> TestConnectionAsync(string cookie)
    {
        try
        {
            var userInfo = await GetUserInfoAsync(cookie);
            return !string.IsNullOrEmpty(userInfo);
        }
        catch
        {
            return false;
        }
    }

    public override (string shareId, string? sharePwd) ParseShareLink(string url)
    {
        // 解析 115 分享链接
        // 格式: https://115.com/s/xxxxx?password=yyyy
        // 或: https://115cdn.com/s/xxxxx?password=yyyy
        // 或: https://anxia.com/s/xxxxx?password=yyyy
        var match = Regex.Match(url, @"https?://(?:115|115cdn|anxia)\.com/s/(\w+)\?password=(\w+)", 
            RegexOptions.IgnoreCase);
        
        if (!match.Success)
            return ("", null);

        var shareCode = match.Groups[1].Value;
        var receiveCode = match.Groups[2].Value;

        return (shareCode, receiveCode);
    }

    public override async Task<List<ShareFileItem>> GetShareFileListAsync(
        string cookie, string shareId, string? sharePwd)
    {
        var items = new List<ShareFileItem>();
        var offset = 0;
        var limit = 100;

        while (true)
        {
            var url = $"https://webapi.115.com/share/snap?share_code={shareId}&receive_code={sharePwd}&offset={offset}&limit={limit}";
            
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Cookie", cookie);
            request.Headers.Add("User-Agent", UserAgent);

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);

            var root = json.RootElement;
            if (root.TryGetProperty("state", out var stateEl) && !stateEl.GetBoolean())
            {
                var error = root.TryGetProperty("error", out var errorEl) 
                    ? errorEl.GetString() : "unknown";
                throw new Exception($"获取分享文件列表失败: {error}");
            }

            var data = root.GetProperty("data");
            if (!data.TryGetProperty("list", out var list))
                break;

            foreach (var item in list.EnumerateArray())
            {
                var isDir = item.TryGetProperty("cid", out var cidEl) && cidEl.ValueKind != JsonValueKind.Null;
                var fileId = item.TryGetProperty("fid", out var fidEl) 
                    ? fidEl.GetString() 
                    : (item.TryGetProperty("cid", out cidEl) ? cidEl.GetString() : null);

                items.Add(new ShareFileItem
                {
                    FileId = fileId ?? "",
                    FileName = item.TryGetProperty("file_name", out var nameEl) 
                        ? nameEl.GetString() ?? "" 
                        : "",
                    FileSize = item.TryGetProperty("file_size", out var sizeEl) 
                        ? sizeEl.GetInt64() 
                        : 0,
                    FileType = isDir ? "folder" : "file"
                });
            }

            if (list.GetArrayLength() < limit)
                break;
            offset += limit;
        }

        return items;
    }

    public override async Task<string> CreateFolderAsync(
        string cookie, string parentFolderId, string folderName)
    {
        // 115 创建文件夹 API
        var request = new HttpRequestMessage(HttpMethod.Post, "https://webapi.115.com/files/add");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("User-Agent", UserAgent);
        
        var formData = new Dictionary<string, string>
        {
            { "cname", folderName },
            { "pid", parentFolderId }
        };
        request.Content = new FormUrlEncodedContent(formData);

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("state", out var stateEl) && !stateEl.GetBoolean())
        {
            var error = json.RootElement.TryGetProperty("error", out var errorEl) 
                ? errorEl.GetString() : "unknown";
            throw new Exception($"创建文件夹失败: {error}");
        }

        var fileId = json.RootElement.TryGetProperty("file_id", out var fileIdEl) 
            ? fileIdEl.GetString() 
            : throw new Exception("创建文件夹成功但未返回文件ID");

        return fileId;
    }

    public override async Task<TransferResult> TransferFileAsync(
        string cookie, string shareId, string? sharePwd, 
        string fileId, string targetFolderId)
    {
        // 115 的转存是批量操作，需要传 file_id 列表
        // 这里只转存一个文件
        var userId = await GetUserIdAsync(cookie);
        if (string.IsNullOrEmpty(userId))
            return new TransferResult { Success = false, ErrorMessage = "获取用户ID失败" };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://webapi.115.com/share/receive");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("User-Agent", UserAgent);
        
        var formData = new Dictionary<string, string>
        {
            { "user_id", userId },
            { "share_code", shareId },
            { "receive_code", sharePwd ?? "" },
            { "file_id", fileId },
            { "cid", targetFolderId }
        };
        request.Content = new FormUrlEncodedContent(formData);

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        var state = json.RootElement.TryGetProperty("state", out var stateEl) && stateEl.GetBoolean();
        
        if (state)
        {
            return new TransferResult { Success = true };
        }
        else
        {
            var error = json.RootElement.TryGetProperty("error", out var errorEl) 
                ? errorEl.GetString() : "unknown";
            
            // 115 的"文件已接收，无需重复接收"视为成功
            if (error.Contains("无需重复接收"))
                return new TransferResult { Success = true };
            
            return new TransferResult { Success = false, ErrorMessage = $"转存失败: {error}" };
        }
    }

    public override async Task<bool> VerifyFileExistsAsync(
        string cookie, string folderId, string fileName)
    {
        var url = $"https://webapi.115.com/files?cid={folderId}&offset=0&limit=100&show_dir=1";
        
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("User-Agent", UserAgent);

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (!json.RootElement.TryGetProperty("data", out var data))
            return false;

        foreach (var item in data.EnumerateArray())
        {
            var name = item.TryGetProperty("file_name", out var nameEl) 
                ? nameEl.GetString() 
                : (item.TryGetProperty("n", out var nEl) ? nEl.GetString() : null);
            
            if (name == fileName)
                return true;
        }

        return false;
    }

    public override async Task<string?> GetUserInfoAsync(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://my.115.com/?ct=ajax&ac=get_user_aq");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("User-Agent", UserAgent);

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (!json.RootElement.TryGetProperty("state", out var stateEl) || !stateEl.GetBoolean())
            return null;

        var data = json.RootElement.GetProperty("data");
        var uid = data.TryGetProperty("uid", out var uidEl) ? uidEl.GetString() : null;
        
        return uid ?? "unknown";
    }

    private async Task<string?> GetUserIdAsync(string cookie)
    {
        return await GetUserInfoAsync(cookie);
    }
}
