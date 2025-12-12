using System.Security.Cryptography;
using SyncClient.Models;

namespace SyncClient.Services;

/// <summary>
/// 掃描資料夾並建立 manifest，必要時計算 Hash。
/// </summary>
public class ManifestBuilder
{
    private readonly string _root;

    public ManifestBuilder(string root)
    {
        _root = root;
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
                LastWriteUtc = info.LastWriteTimeUtc,
                Sha256 = null
            };

            if (previous.TryGetValue(relative, out var prev) && prev.Size == entry.Size && prev.LastWriteUtc == entry.LastWriteUtc)
            {
                entry.Sha256 = prev.Sha256;
            }
            else
            {
                entry.Sha256 = ComputeSha256(path);
            }

            entries.Add(entry);
        }

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
