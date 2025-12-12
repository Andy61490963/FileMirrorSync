namespace SyncClient.Models;

/// <summary>
/// Client 端檔案資訊，用於 manifest 與狀態保存。
/// </summary>
public class ClientFileEntry
{
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public string? Sha256 { get; set; }
}
