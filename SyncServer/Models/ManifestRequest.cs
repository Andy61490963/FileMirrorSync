namespace SyncServer.Models;

/// <summary>
/// Client 上送的 manifest，包含 datasetId、clientId 與檔案清單。
/// </summary>
public class ManifestRequest
{
    public string DatasetId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public List<FileEntry> Files { get; set; } = new();
}
