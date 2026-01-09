using Microsoft.Extensions.Options;
using SyncServer.Infrastructure;
using SyncServer.Models;

namespace SyncServer.Services;

/// <summary>
/// 負責計算 manifest 差異並建立 Upload Session。
/// </summary>
public class ManifestDiffService
{
    private readonly PathMapper _pathMapper;
    private readonly UploadSessionService _uploadSessionService;
    private readonly VersionPolicy _versionPolicy;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<ManifestDiffService> _logger;

    public ManifestDiffService(
        PathMapper pathMapper,
        UploadSessionService uploadSessionService,
        VersionPolicy versionPolicy,
        IOptions<StorageOptions> storageOptions,
        ILogger<ManifestDiffService> logger)
    {
        _pathMapper = pathMapper;
        _uploadSessionService = uploadSessionService;
        _versionPolicy = versionPolicy;
        _storageOptions = storageOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// 比對 Client manifest 與 Server 端檔案，產生差異清單。
    /// </summary>
    public ManifestDiffResponse BuildDiff(ManifestRequest request)
    {
        var datasetRoot = _pathMapper.GetDatasetRoot(request.DatasetId);
        var serverFiles = EnumerateServerFiles(datasetRoot)
            .ToDictionary(f => f.Path, f => f, StringComparer.OrdinalIgnoreCase);
        var clientFiles = request.Files
            .Select(file =>
            {
                _pathMapper.ValidateRelativePath(file.Path);
                return new FileEntry
                {
                    Path = NormalizePath(file.Path),
                    Size = file.Size,
                    LastWriteUtc = file.LastWriteUtc,
                    Sha256 = file.Sha256
                };
            })
            .ToDictionary(f => f.Path, f => f, StringComparer.OrdinalIgnoreCase);

        var upload = new List<UploadInstruction>();
        foreach (var clientEntry in clientFiles.Values)
        {
            if (!serverFiles.TryGetValue(clientEntry.Path, out var serverEntry))
            {
                upload.Add(_uploadSessionService.CreateSession(request.DatasetId, request.ClientId, clientEntry.Path));
                continue;
            }

            if (_versionPolicy.ShouldUpload(serverEntry, clientEntry))
            {
                upload.Add(_uploadSessionService.CreateSession(request.DatasetId, request.ClientId, clientEntry.Path));
            }
        }

        var delete = new List<string>();
        if (_storageOptions.DeleteStrategy == DeleteStrategy.LwwDelete)
        {
            delete = serverFiles.Keys.Except(clientFiles.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        }

        _logger.LogInformation(
            "Manifest 比對完成 Dataset={DatasetId} Client={ClientId} Upload={UploadCount} Delete={DeleteCount}",
            request.DatasetId,
            request.ClientId,
            upload.Count,
            delete.Count);

        return new ManifestDiffResponse
        {
            Upload = upload,
            Delete = delete
        };
    }

    /// <summary>
    /// 列舉 Server 端資料集內的檔案資訊。
    /// </summary>
    private IEnumerable<FileEntry> EnumerateServerFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            yield return new FileEntry
            {
                Path = NormalizePath(Path.GetRelativePath(root, file)),
                Size = info.Length,
                LastWriteUtc = info.LastWriteTimeUtc
            };
        }
    }

    /// <summary>
    /// 正規化路徑為 Unix 風格。
    /// </summary>
    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
