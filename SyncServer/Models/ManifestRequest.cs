namespace SyncServer.Models;

/// <summary>
/// Client 上送的 manifest，包含 clientId 與檔案清單。
/// </summary>
public class ManifestRequest
{
    public string ClientId { get; set; } = string.Empty;
    public List<FileEntry> Files { get; set; } = new();
}
