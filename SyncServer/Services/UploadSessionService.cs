using System.Text.Json;
using SyncServer.Infrastructure;
using SyncServer.Models;

namespace SyncServer.Services;

/// <summary>
/// 管理 Upload Session 的建立與查詢。
/// </summary>
public class UploadSessionService
{
    private const string SessionMetadataFileName = "session.json";
    private readonly PathMapper _pathMapper;
    private readonly ILogger<UploadSessionService> _logger;

    public UploadSessionService(PathMapper pathMapper, ILogger<UploadSessionService> logger)
    {
        _pathMapper = pathMapper;
        _logger = logger;
    }

    /// <summary>
    /// 建立新的 Upload Session 並回傳 UploadInstruction。
    /// </summary>
    public UploadInstruction CreateSession(string datasetId, string clientId, string relativePath)
    {
        _pathMapper.ValidateRelativePath(relativePath);
        var uploadId = Guid.NewGuid();
        var sessionRoot = _pathMapper.GetUploadTempRoot(datasetId, uploadId);
        var metadata = new UploadSessionMetadata
        {
            DatasetId = datasetId,
            ClientId = clientId,
            RelativePath = relativePath.Replace('\\', '/'),
            CreatedAtUtc = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(sessionRoot, SessionMetadataFileName), json);

        _logger.LogInformation("建立 Upload Session Dataset={DatasetId} Client={ClientId} Path={Path} UploadId={UploadId}", datasetId, clientId, relativePath, uploadId);

        return new UploadInstruction
        {
            Path = metadata.RelativePath,
            UploadId = uploadId
        };
    }

    /// <summary>
    /// 讀取 Upload Session 的 metadata。
    /// </summary>
    public UploadSessionMetadata GetSession(string datasetId, Guid uploadId)
    {
        var sessionRoot = _pathMapper.GetUploadTempRoot(datasetId, uploadId);
        var metadataPath = Path.Combine(sessionRoot, SessionMetadataFileName);
        if (!File.Exists(metadataPath))
        {
            throw new InvalidOperationException("Upload session 不存在");
        }

        var json = File.ReadAllText(metadataPath);
        var metadata = JsonSerializer.Deserialize<UploadSessionMetadata>(json) ?? throw new InvalidOperationException("Upload session 格式錯誤");
        if (!string.Equals(metadata.DatasetId, datasetId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Upload session 與 datasetId 不一致");
        }

        return metadata;
    }

    /// <summary>
    /// 取得 chunk 的暫存路徑。
    /// </summary>
    public string GetChunkPath(string datasetId, Guid uploadId, string relativePath, int index)
    {
        return _pathMapper.GetChunkPath(datasetId, uploadId, relativePath, index);
    }

    /// <summary>
    /// 移除 Upload Session 的暫存資料。
    /// </summary>
    public void CleanupSession(string datasetId, Guid uploadId)
    {
        var sessionRoot = _pathMapper.GetUploadTempRoot(datasetId, uploadId);
        if (Directory.Exists(sessionRoot))
        {
            Directory.Delete(sessionRoot, true);
        }
    }
}
