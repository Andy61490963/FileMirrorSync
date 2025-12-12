namespace SyncServer.Models;

/// <summary>
/// 表示單一檔案的資訊，用於 manifest 與差異比對。
/// </summary>
public class FileEntry
{
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public string? Sha256 { get; set; }
}
