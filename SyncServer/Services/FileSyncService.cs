using System.Security.Cryptography;
using System.Text;
using SyncServer.Infrastructure;
using SyncServer.Models;

namespace SyncServer.Services;

/// <summary>
/// 處理 manifest 差異比對與檔案上傳的核心服務。
/// </summary>
public class FileSyncService
{
    private readonly PathMapper _pathMapper;

    public FileSyncService(PathMapper pathMapper)
    {
        _pathMapper = pathMapper;
    }

    /// <summary>
    /// 計算 client 與 server 的 manifest 差異。
    /// </summary>
    public ManifestDiffResponse Diff(string clientId, IEnumerable<FileEntry> clientFiles)
    {
        var clientRoot = _pathMapper.GetClientRoot(clientId);
        var serverFiles = EnumerateServerFiles(clientRoot);

        var clientLookup = clientFiles.ToDictionary(f => NormalizePath(f.Path), f => f, StringComparer.OrdinalIgnoreCase);
        var serverLookup = serverFiles.ToDictionary(f => NormalizePath(f.Path), f => f, StringComparer.OrdinalIgnoreCase);

        var upload = new List<string>();
        foreach (var kv in clientLookup)
        {
            if (!serverLookup.TryGetValue(kv.Key, out var serverEntry))
            {
                upload.Add(kv.Key);
                continue;
            }

            if (serverEntry.Size != kv.Value.Size || serverEntry.LastWriteUtc != kv.Value.LastWriteUtc)
            {
                upload.Add(kv.Key);
            }
            else if (!string.IsNullOrEmpty(kv.Value.Sha256) && !string.Equals(kv.Value.Sha256, serverEntry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                upload.Add(kv.Key);
            }
        }

        var delete = serverLookup.Keys.Except(clientLookup.Keys, StringComparer.OrdinalIgnoreCase).ToList();

        return new ManifestDiffResponse
        {
            Upload = upload,
            Delete = delete
        };
    }

    /// <summary>
    /// 儲存單一 chunk 至暫存目錄，可重送覆寫。
    /// </summary>
    public async Task SaveChunkAsync(string clientId, string base64Path, int index, Stream content, CancellationToken ct)
    {
        var relativePath = DecodePath(base64Path);
        var safeTempFile = _pathMapper.GetSafeAbsolutePath(clientId, Path.Combine("temp", relativePath + $".chunk{index}"));

        Directory.CreateDirectory(Path.GetDirectoryName(safeTempFile)!);

        await using var fs = new FileStream(safeTempFile, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fs, ct);
    }

    /// <summary>
    /// 合併 chunk 並原子替換目標檔案。
    /// </summary>
    public async Task CompleteUploadAsync(string clientId, string base64Path, CompleteUploadRequest request, CancellationToken ct)
    {
        var relativePath = DecodePath(base64Path);
        var targetPath = _pathMapper.GetSafeAbsolutePath(clientId, relativePath);
        var chunkDir = _pathMapper.GetSafeAbsolutePath(clientId, Path.Combine("temp", Path.GetDirectoryName(relativePath) ?? string.Empty));
        var chunkFiles = Directory.GetFiles(chunkDir, Path.GetFileName(relativePath) + ".chunk*")
            .OrderBy(f => f)
            .ToList();

        if (request.ChunkCount > 0 && chunkFiles.Count != request.ChunkCount)
        {
            throw new InvalidOperationException("chunk 數量不一致，請重新上傳");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var tempFile = Path.Combine(_pathMapper.GetTempRoot(clientId), Guid.NewGuid() + ".tmp");

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

        File.Move(tempFile, targetPath, true);
        CleanupChunks(chunkFiles);
    }

    /// <summary>
    /// 執行刪除，僅允許客戶根目錄下的檔案。
    /// </summary>
    public void DeleteFiles(string clientId, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var target = _pathMapper.GetSafeAbsolutePath(clientId, DecodePath(path));
            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
    }

    private IEnumerable<FileEntry> EnumerateServerFiles(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            yield return new FileEntry
            {
                Path = NormalizePath(Path.GetRelativePath(root, file)),
                Size = info.Length,
                LastWriteUtc = info.LastWriteTimeUtc,
                Sha256 = null
            };
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static string DecodePath(string base64Path)
    {
        var bytes = Convert.FromBase64String(base64Path);
        return Encoding.UTF8.GetString(bytes);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void CleanupChunks(IEnumerable<string> chunkFiles)
    {
        foreach (var file in chunkFiles)
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
