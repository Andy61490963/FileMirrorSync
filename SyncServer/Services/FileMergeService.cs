using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SyncServer.Infrastructure;
using SyncServer.Models;

namespace SyncServer.Services;

/// <summary>
/// 處理 chunk 合併、Hash 驗證與原子替換。
/// </summary>
public class FileMergeService
{
    private readonly PathMapper _pathMapper;
    private readonly UploadSessionService _uploadSessionService;
    private readonly VersionPolicy _versionPolicy;
    private readonly ILogger<FileMergeService> _logger;
    private readonly SemaphoreSlim _globalGate;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public FileMergeService(
        PathMapper pathMapper,
        UploadSessionService uploadSessionService,
        VersionPolicy versionPolicy,
        IOptions<StorageOptions> storageOptions,
        ILogger<FileMergeService> logger)
    {
        _pathMapper = pathMapper;
        _uploadSessionService = uploadSessionService;
        _versionPolicy = versionPolicy;
        _globalGate = new SemaphoreSlim(Math.Max(1, storageOptions.Value.MaxParallelUploads));
        _logger = logger;
    }

    /// <summary>
    /// 儲存單一 chunk 至暫存目錄。
    /// </summary>
    public async Task SaveChunkAsync(string datasetId, Guid uploadId, string relativePath, int index, Stream content, CancellationToken ct)
    {
        var chunkPath = _uploadSessionService.GetChunkPath(datasetId, uploadId, relativePath, index);
        Directory.CreateDirectory(Path.GetDirectoryName(chunkPath)!);

        await using var fs = new FileStream(chunkPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fs, ct);

        _logger.LogInformation("已儲存 chunk Dataset={DatasetId} UploadId={UploadId} Path={Path} Index={Index}", datasetId, uploadId, relativePath, index);
    }

    /// <summary>
    /// 合併 chunk，並依 LWW 規則決定是否覆蓋。
    /// </summary>
    public async Task CompleteUploadAsync(Guid uploadId, CompleteUploadRequest request, CancellationToken ct)
    {
        await _globalGate.WaitAsync(ct);
        try
        {
            var session = _uploadSessionService.GetSession(request.DatasetId, uploadId);
            if (!string.Equals(session.ClientId, request.ClientId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Upload session 與 clientId 不一致");
            }

            var relativePath = session.RelativePath;
            var lockKey = $"{request.DatasetId}/{relativePath}";
            var gate = _locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

            await gate.WaitAsync(ct);
            try
            {
                var targetPath = _pathMapper.GetSafeAbsolutePath(request.DatasetId, relativePath);
                DateTime? serverLastWriteUtc = File.Exists(targetPath) ? new FileInfo(targetPath).LastWriteTimeUtc : null;

                if (!_versionPolicy.ShouldOverwrite(serverLastWriteUtc, request.LastWriteUtc))
                {
                    _uploadSessionService.CleanupSession(request.DatasetId, uploadId);
                    _logger.LogInformation("忽略較舊版本 Dataset={DatasetId} Path={Path}", request.DatasetId, relativePath);
                    return;
                }

                var chunkFiles = EnumerateChunks(request.DatasetId, uploadId, relativePath).ToList();
                if (request.ChunkCount > 0 && chunkFiles.Count != request.ChunkCount)
                {
                    throw new InvalidOperationException("chunk 數量不一致，請重新上傳");
                }

                var tempFile = Path.Combine(_pathMapper.GetDatasetTempRoot(request.DatasetId), $"{Guid.NewGuid():N}.tmp");
                Directory.CreateDirectory(Path.GetDirectoryName(tempFile)!);

                await using (var output = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    foreach (var chunk in chunkFiles)
                    {
                        await using var input = new FileStream(chunk, FileMode.Open, FileAccess.Read, FileShare.Read);
                        await input.CopyToAsync(output, ct);
                    }
                }

                var info = new FileInfo(tempFile);
                if (info.Length != request.ExpectedSize)
                {
                    File.Delete(tempFile);
                    throw new InvalidOperationException("檔案大小不一致");
                }

                if (!string.IsNullOrWhiteSpace(request.Sha256))
                {
                    var hash = await ComputeSha256Async(tempFile, ct);
                    if (!string.Equals(hash, request.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(tempFile);
                        throw new InvalidOperationException("檔案 Hash 驗證失敗");
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Move(tempFile, targetPath, true);
                File.SetLastWriteTimeUtc(targetPath, request.LastWriteUtc);

                _uploadSessionService.CleanupSession(request.DatasetId, uploadId);
                _logger.LogInformation("檔案合併完成 Dataset={DatasetId} Path={Path} Size={Size}", request.DatasetId, relativePath, request.ExpectedSize);
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            _globalGate.Release();
        }
    }

    /// <summary>
    /// 依 uploadId 與相對路徑列舉 chunk 檔案。
    /// </summary>
    private IEnumerable<string> EnumerateChunks(string datasetId, Guid uploadId, string relativePath)
    {
        var sessionRoot = _pathMapper.GetUploadTempRoot(datasetId, uploadId);
        var prefix = Path.Combine(sessionRoot, relativePath.Replace('\\', '/')) + ".chunk";
        var directory = Path.GetDirectoryName(prefix);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return Enumerable.Empty<string>();
        }

        var filePrefix = Path.GetFileName(prefix);
        return Directory.EnumerateFiles(directory, filePrefix + "*")
            .Select(path => new
            {
                Path = path,
                Index = ParseChunkIndex(path, filePrefix)
            })
            .OrderBy(x => x.Index)
            .Select(x => x.Path);
    }

    /// <summary>
    /// 從 chunk 檔名解析索引值。
    /// </summary>
    private static int ParseChunkIndex(string fullPath, string prefix)
    {
        var name = Path.GetFileName(fullPath);
        var idxPart = name.Substring(prefix.Length);
        return int.TryParse(idxPart, out var n) ? n : int.MaxValue;
    }

    /// <summary>
    /// 計算檔案的 SHA256 雜湊。
    /// </summary>
    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
