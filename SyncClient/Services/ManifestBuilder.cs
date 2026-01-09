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
    /// 建立最新 manifest，僅提供大小與最後修改時間。
    /// </summary>
    public List<ClientFileEntry> Build()
    {
        var entries = new List<ClientFileEntry>();

        // 遍歷目標資料夾下面的所有物件
        foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(path);
            var relative = Normalize(Path.GetRelativePath(_root, path));
            var entry = new ClientFileEntry
            {
                Path = relative,
                Size = info.Length,
                LastWriteUtc = info.LastWriteTimeUtc,
                Sha256 = null
            };

            entries.Add(entry);
        }

        _logger.Information("Manifest 掃描完成，總檔案數: {Count}", entries.Count);
        return entries;
    }

    /// <summary>
    /// 統一路徑分隔符號。
    /// </summary>
    private static string Normalize(string path) => path.Replace('\\', '/');
}
