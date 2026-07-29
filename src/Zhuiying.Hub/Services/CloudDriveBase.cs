using Zhuiying.Hub.Models;

namespace Zhuiying.Hub.Services;

public abstract class CloudDriveBase
{
    public abstract string DriveType { get; }

    // 验证 Cookie 有效性
    public abstract Task<bool> TestConnectionAsync(string cookie);

    // 解析分享链接，提取分享 ID 和密码
    public abstract (string shareId, string? sharePwd) ParseShareLink(string url);

    // 获取分享链接中的文件列表
    public abstract Task<List<ShareFileItem>> GetShareFileListAsync(string cookie, string shareId, string? sharePwd);

    // 在网盘中创建文件夹，返回目录 ID
    public abstract Task<string> CreateFolderAsync(string cookie, string parentFolderId, string folderName);

    // 转存单个文件到指定目录
    public abstract Task<TransferResult> TransferFileAsync(
        string cookie,
        string shareId,
        string? sharePwd,
        string fileId,
        string targetFolderId);

    // 验证文件是否已存在于网盘
    public abstract Task<bool> VerifyFileExistsAsync(string cookie, string folderId, string fileName);

    // 获取用户信息（用于测试连接）
    public abstract Task<string?> GetUserInfoAsync(string cookie);
}
