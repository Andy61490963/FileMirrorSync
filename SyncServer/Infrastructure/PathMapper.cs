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
    /// 取得某 datasetId 的根目錄，並保證存在。
    /// </summary>
    public string GetDatasetRoot(string datasetId)
    {
        var root = Path.GetFullPath(Path.Combine(_options.InboundRoot, datasetId));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// 取得暫存區路徑，專用於 chunk 緩存。
    /// </summary>
    public string GetDatasetTempRoot(string datasetId)
    {
        var root = Path.GetFullPath(Path.Combine(_options.TempRoot, datasetId));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// 驗證相對路徑並回傳絕對路徑，防止路徑穿越或 UNC/絕對路徑。
    /// </summary>
    public string GetSafeAbsolutePath(string datasetId, string relativePath)
    {
        ValidateRelativePath(relativePath);

        var normalized = relativePath.Replace('\\', '/');
        var target = Path.GetFullPath(Path.Combine(GetDatasetRoot(datasetId), normalized));
        var root = GetDatasetRoot(datasetId);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("路徑不在授權範圍內");
        }

        return target;
    }

    /// <summary>
    /// 取得特定 uploadId 的暫存根目錄。
    /// </summary>
    public string GetUploadTempRoot(string datasetId, Guid uploadId)
    {
        var root = Path.GetFullPath(Path.Combine(GetDatasetTempRoot(datasetId), uploadId.ToString("N")));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// 取得 chunk 檔案的安全絕對路徑。
    /// </summary>
    public string GetChunkPath(string datasetId, Guid uploadId, string relativePath, int index)
    {
        ValidateRelativePath(relativePath);
        var normalized = relativePath.Replace('\\', '/');
        var path = Path.Combine(GetUploadTempRoot(datasetId, uploadId), normalized + $".chunk{index}");
        return Path.GetFullPath(path);
    }

    /// <summary>
    /// 驗證相對路徑是否合法。
    /// </summary>
    public void ValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("路徑不可為空");
        }

        var normalized = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException("路徑不可為絕對路徑或 UNC");
        }

        if (normalized.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new InvalidOperationException("路徑含有非法字元");
        }

        var invalidFileChars = Path.GetInvalidFileNameChars();
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
        {
            throw new InvalidOperationException("路徑不可包含穿越片段");
        }

        if (segments.Any(segment => segment.IndexOfAny(invalidFileChars) >= 0))
        {
            throw new InvalidOperationException("路徑含有非法檔名字元");
        }
    }
}
