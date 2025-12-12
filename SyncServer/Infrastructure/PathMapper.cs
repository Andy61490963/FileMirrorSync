using Microsoft.Extensions.Options;

namespace SyncServer.Infrastructure;

/// <summary>
/// 專責處理路徑安全與根目錄映射的工具。
/// </summary>
public class PathMapper
{
    private readonly StorageOptions _options;

    public PathMapper(IOptions<StorageOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// 取得某 clientId 的根目錄，並保證存在。
    /// </summary>
    public string GetClientRoot(string clientId)
    {
        var root = Path.GetFullPath(Path.Combine(_options.InboundRoot, clientId));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// 取得暫存區路徑，專用於 chunk 緩存。
    /// </summary>
    public string GetTempRoot(string clientId)
    {
        var root = Path.GetFullPath(Path.Combine(_options.TempRoot, clientId));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// 驗證相對路徑並回傳絕對路徑，防止路徑穿越或 UNC/絕對路徑。
    /// </summary>
    public string GetSafeAbsolutePath(string clientId, string relativePath)
    {
        if (relativePath.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("路徑不可包含穿越片段");
        }

        var normalized = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException("路徑不可為絕對路徑或 UNC");
        }

        var target = Path.GetFullPath(Path.Combine(GetClientRoot(clientId), normalized));
        var root = GetClientRoot(clientId);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("路徑不在授權範圍內");
        }

        return target;
    }
}
