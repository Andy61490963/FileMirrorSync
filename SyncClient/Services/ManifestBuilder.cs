using System.Security.Cryptography;
using Serilog;
using SyncClient.Models;

namespace SyncClient.Services;

/// <summary>
/// 掃描資料夾並建立 manifest，必要時計算 Hash。
/// </summary>
public class ManifestBuilder
{
    private readonly string _root;
    private readonly Serilog.ILogger _logger;

    public ManifestBuilder(string root, Serilog.ILogger logger)
    {
        _root = root;
        _logger = logger.ForContext<ManifestBuilder>();
    }

    /// <summary>
    /// 建立最新 manifest，並依狀態判斷是否需要重算 Hash。
    /// </summary>
    public List<ClientFileEntry> Build(Dictionary<string, ClientFileEntry> previous)
    {
        var entries = new List<ClientFileEntry>();

        foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(path);
            var relative = Normalize(Path.GetRelativePath(_root, path));
            var entry = new ClientFileEntry
            {
                Path = relative,
                Size = info.Length,
                LastWriteTime = info.LastWriteTime,
                Sha256 = null
            };

            if (previous.TryGetValue(relative, out var prev) && prev.Size == entry.Size && prev.LastWriteTime == entry.LastWriteTime)
            {
                entry.Sha256 = prev.Sha256;
                _logger.Debug("沿用既有 Hash，檔案: {File}", relative);
            }
            else
            {
                entry.Sha256 = ComputeSha256(path);
                _logger.Information("重新計算 Hash，檔案: {File}", relative);
            }

            entries.Add(entry);
        }

        _logger.Information("Manifest 掃描完成，總檔案數: {Count}", entries.Count);
        return entries;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
