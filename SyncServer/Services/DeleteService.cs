using Microsoft.Extensions.Options;
using SyncServer.Infrastructure;
using SyncServer.Models;

namespace SyncServer.Services;

/// <summary>
/// 處理 Server 端檔案刪除行為。
/// </summary>
public class DeleteService
{
    private readonly PathMapper _pathMapper;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<DeleteService> _logger;

    public DeleteService(PathMapper pathMapper, IOptions<StorageOptions> storageOptions, ILogger<DeleteService> logger)
    {
        _pathMapper = pathMapper;
        _storageOptions = storageOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// 依設定執行刪除策略。
    /// </summary>
    public void ApplyDelete(DeleteRequest request)
    {
        if (_storageOptions.DeleteStrategy == DeleteStrategy.DeleteDisabled)
        {
            _logger.LogInformation("刪除策略已停用，忽略刪除請求 Dataset={DatasetId} Client={ClientId}", request.DatasetId, request.ClientId);
            return;
        }

        if (_storageOptions.DeleteStrategy == DeleteStrategy.LwwDelete)
        {
            if (!request.DeletedAtUtc.HasValue)
            {
                throw new InvalidOperationException("LWW 刪除必須提供 DeletedAtUtc");
            }

            ExecuteLwwDelete(request.DatasetId, request.Paths, request.DeletedAtUtc.Value);
        }
    }

    /// <summary>
    /// 依 LWW 規則執行刪除。
    /// </summary>
    private void ExecuteLwwDelete(string datasetId, IEnumerable<string> paths, DateTime deletedAtUtc)
    {
        foreach (var raw in paths)
        {
            var relative = raw.Replace('\\', '/');
            _pathMapper.ValidateRelativePath(relative);
            var target = _pathMapper.GetSafeAbsolutePath(datasetId, relative);
            if (!File.Exists(target))
            {
                continue;
            }

            var info = new FileInfo(target);
            if (deletedAtUtc > info.LastWriteTimeUtc)
            {
                File.Delete(target);
                _logger.LogInformation("已刪除檔案 Dataset={DatasetId} Path={Path}", datasetId, relative);
            }
            else
            {
                _logger.LogInformation("略過刪除（Server 版本較新） Dataset={DatasetId} Path={Path}", datasetId, relative);
            }
        }
    }
}
