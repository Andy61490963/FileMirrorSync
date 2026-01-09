namespace SyncServer.Models;

/// <summary>
/// 完成上傳時的驗證資訊。
/// </summary>
public class CompleteUploadRequest
{
    public string DatasetId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public long ExpectedSize { get; set; }
    public string? Sha256 { get; set; }
    public int ChunkCount { get; set; }
    public DateTime LastWriteUtc { get; set; }
}
