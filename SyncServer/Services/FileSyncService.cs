using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SyncServer.Infrastructure;
using SyncServer.Models;

namespace SyncServer.Services;

/// <summary>
/// 處理 manifest 差異比對與檔案上傳的核心服務。
/// </summary>
public class FileSyncService
{
    private readonly PathMapper _pathMapper;
    private readonly ILogger<FileSyncService> _logger;

    public FileSyncService(PathMapper pathMapper, ILogger<FileSyncService> logger)
    {
        _pathMapper = pathMapper;
        _logger = logger;
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
                upload.Add(kv.Key); // server 沒有 → 要上傳
                continue;
            }

            if (serverEntry.Size != kv.Value.Size || serverEntry.LastWriteUtc != kv.Value.LastWriteUtc)
            {
                upload.Add(kv.Key); // 大小或時間不同 → 要上傳
            }
            else if (!string.IsNullOrEmpty(kv.Value.Sha256) && !string.Equals(kv.Value.Sha256, serverEntry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                upload.Add(kv.Key); // hash 不同 → 要上傳
            }
        }

        // Server 有、Client 沒有 → Server 端多出來的檔案要刪（鏡像同步）
        var delete = serverLookup.Keys.Except(clientLookup.Keys, StringComparer.OrdinalIgnoreCase).ToList();

        var response = new ManifestDiffResponse
        {
            Upload = upload,
            Delete = delete
        };

        _logger.LogInformation("比較 Manifest 完成，Client: {ClientId}，需上傳 {UploadCount} 筆，需刪除 {DeleteCount} 筆", clientId, response.Upload.Count, response.Delete.Count);

        return response;
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

        _logger.LogInformation("已儲存 chunk，Client: {ClientId}，檔案: {RelativePath}，索引: {ChunkIndex}", clientId, relativePath, index);
    }

    /// <summary>
    /// 合併 chunk 並原子替換目標檔案。
    /// </summary>
    public async Task CompleteUploadAsync(string clientId, string base64Path, CompleteUploadRequest request, CancellationToken ct)
    {
        var relativePath = DecodePath(base64Path);
        var targetPath = _pathMapper.GetSafeAbsolutePath(clientId, relativePath);
        var chunkDir = _pathMapper.GetSafeAbsolutePath(clientId, Path.Combine("temp", Path.GetDirectoryName(relativePath) ?? string.Empty));
        var prefix = Path.GetFileName(relativePath) + ".chunk";

        var chunkFiles = Directory.EnumerateFiles(chunkDir, prefix + "*")
            .Select(path => new
            {
                Path = path,
                Index = ParseChunkIndex(path, prefix)
            })
            .OrderBy(x => x.Index)
            .Select(x => x.Path)
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

        _logger.LogInformation("檔案合併完成，Client: {ClientId}，目標檔案: {RelativePath}，大小: {Size} bytes", clientId, relativePath, request.ExpectedSize);
    }

    static int ParseChunkIndex(string fullPath, string prefix)
    {
        var name = Path.GetFileName(fullPath); // e.g. "a.pdf.chunk12"
        var idxPart = name.Substring(prefix.Length); // "12"
        return int.TryParse(idxPart, out var n) ? n : int.MaxValue;
    }
    
    /// <summary>
    /// 執行刪除，僅允許客戶根目錄下的檔案。
    /// 注意：paths 來自 JSON body，是「相對路徑」，不是 Base64Url。
    /// </summary>
    public void DeleteFiles(string clientId, IEnumerable<string> paths)
    {
        var targetList = paths.ToList();

        foreach (var raw in targetList)
        {
            // 1) 統一路徑格式
            var relative = NormalizePath(raw);

            // 2) 禁止絕對路徑 / UNC
            if (Path.IsPathRooted(relative))
                throw new InvalidOperationException("Invalid path.");

            // 3) 禁止 ../ 穿越（用 segment 檢查比較準）
            var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(s => s == ".."))
                throw new InvalidOperationException("Invalid path.");

            // 4) 終極防線：PathMapper 確保落在 client root 底下
            var target = _pathMapper.GetSafeAbsolutePath(clientId, relative);

            if (File.Exists(target))
                File.Delete(target);
        }

        _logger.LogInformation("刪除檔案完成，Client: {ClientId}，檔案數: {Count}", clientId, targetList.Count);
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

    private static string DecodePath(string base64UrlPath)
    {
        var relative = DecodeBase64UrlToUtf8(base64UrlPath);

        // 統一路徑分隔
        relative = relative.Replace('\\', '/');

        // 防止絕對路徑/UNC
        if (Path.IsPathRooted(relative))
            throw new InvalidOperationException("Invalid path.");

        // 防止 ../ 穿越（用簡單規則 + 交給 GetSafeAbsolutePath 再 double check）
        if (relative.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid path.");

        return relative;
    }

    private static string DecodeBase64UrlToUtf8(string base64Url)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
            case 0: break;
            default: throw new FormatException("Invalid Base64Url string length.");
        }

        var bytes = Convert.FromBase64String(s);
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
