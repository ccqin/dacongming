using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Zhuiying.Hub.Models;

namespace Zhuiying.Hub.Services;

public class CloudDrive123Service : CloudDriveBase
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://www.123pan.com";

    public override string DriveType => "123";

    public CloudDrive123Service(HttpClient httpClient)
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
        // 解析 123pan 分享链接
        // 格式: https://www.123pan.com/s/xxxxx 或 https://www.123pan.com/s/xxxxx?pwd=yyyy
        var match = Regex.Match(url, @"/s/([A-Za-z0-9]+)");
        if (!match.Success)
            return ("", null);

        var shareId = match.Groups[1].Value;
        
        // 提取密码
        var pwdMatch = Regex.Match(url, @"pwd=([A-Za-z0-9]+)");
        var sharePwd = pwdMatch.Success ? pwdMatch.Groups[1].Value : null;
        
        // 也检查中文格式
        if (sharePwd == null)
        {
            pwdMatch = Regex.Match(url, @"提取码[:：]\s*([A-Za-z0-9]+)");
            sharePwd = pwdMatch.Success ? pwdMatch.Groups[1].Value : null;
        }

        return (shareId, sharePwd);
    }

    public override async Task<List<ShareFileItem>> GetShareFileListAsync(
        string cookie, string shareId, string? sharePwd)
    {
        var items = new List<ShareFileItem>();
        var page = 1;

        while (true)
        {
            var requestBody = new
            {
                ShareKey = shareId,
                SharePwd = sharePwd ?? "",
                parentFileId = 0,
                limit = 100,
                Page = page
            };

            var request = new HttpRequestMessage(HttpMethod.Post, 
                $"{BaseUrl}/b/api/restful/goapi/v1/share/fs/list");
            request.Headers.Add("Cookie", cookie);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);

            if (json.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 0)
                throw new Exception($"获取分享文件列表失败: {json.RootElement.GetProperty("message").GetString()}");

            var data = json.RootElement.GetProperty("data");
            if (!data.TryGetProperty("InfoList", out var infoList))
                break;

            foreach (var item in infoList.EnumerateArray())
            {
                items.Add(new ShareFileItem
                {
                    FileId = item.GetProperty("FileId").GetString() ?? "",
                    FileName = item.GetProperty("FileName").GetString() ?? "",
                    FileSize = item.TryGetProperty("Size", out var sizeEl) ? sizeEl.GetInt64() : 0,
                    FileType = item.GetProperty("Type").GetInt32() == 1 ? "folder" : "file"
                });
            }

            if (infoList.GetArrayLength() < 100)
                break;
            page++;
        }

        return items;
    }

    public override async Task<string> CreateFolderAsync(
        string cookie, string parentFolderId, string folderName)
    {
        var requestBody = new
        {
            name = folderName,
            parentID = parentFolderId,
            duplicate = 1
        };

        var request = new HttpRequestMessage(HttpMethod.Post, 
            $"{BaseUrl}/b/api/restful/goapi/v1/file/mkdir");
        request.Headers.Add("Cookie", cookie);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 0)
            throw new Exception($"创建文件夹失败: {json.RootElement.GetProperty("message").GetString()}");

        var fileId = json.RootElement.GetProperty("data")
            .GetProperty("Info")
            .GetProperty("FileId")
            .GetString();

        return fileId ?? throw new Exception("创建文件夹成功但未返回文件ID");
    }

    public override async Task<TransferResult> TransferFileAsync(
        string cookie, string shareId, string? sharePwd, 
        string fileId, string targetFolderId)
    {
        // 123pan 的转存是批量操作，这里只转存一个文件
        var fileList = new[]
        {
            new
            {
                fileID = fileId,
                size = 0,
                etag = "",
                type = 0,
                parentFileID = targetFolderId,
                fileName = "",
                driveID = 0
            }
        };

        var requestBody = new
        {
            fileList,
            shareKey = shareId,
            sharePwd = sharePwd ?? "",
            currentLevel = 0
        };

        var request = new HttpRequestMessage(HttpMethod.Post, 
            $"{BaseUrl}/b/api/restful/goapi/v1/file/copy/save");
        request.Headers.Add("Cookie", cookie);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        var message = json.RootElement.TryGetProperty("message", out var msgEl) 
            ? msgEl.GetString() : "unknown";

        if (message == "ok")
        {
            return new TransferResult { Success = true };
        }
        else
        {
            return new TransferResult 
            { 
                Success = false, 
                ErrorMessage = $"转存失败: {message}" 
            };
        }
    }

    public override async Task<bool> VerifyFileExistsAsync(
        string cookie, string folderId, string fileName)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, 
            $"{BaseUrl}/b/api/file/list/new?parentFileId={folderId}&limit=100&SearchData={Uri.EscapeDataString(fileName)}");
        request.Headers.Add("Cookie", cookie);

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 0)
            return false;

        var data = json.RootElement.GetProperty("data");
        if (!data.TryGetProperty("fileList", out var fileList))
            return false;

        foreach (var item in fileList.EnumerateArray())
        {
            var name = item.TryGetProperty("filename", out var nameEl) 
                ? nameEl.GetString() : null;
            if (name == fileName)
                return true;
        }

        return false;
    }

    public override async Task<string?> GetUserInfoAsync(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, 
            $"{BaseUrl}/b/api/user/info");
        request.Headers.Add("Cookie", cookie);

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 0)
            return null;

        var data = json.RootElement.GetProperty("data");
        var username = data.TryGetProperty("username", out var usernameEl) 
            ? usernameEl.GetString() : null;
        var mobile = data.TryGetProperty("mobile", out var mobileEl) 
            ? mobileEl.GetString() : null;

        return username ?? mobile ?? "unknown";
    }
}
