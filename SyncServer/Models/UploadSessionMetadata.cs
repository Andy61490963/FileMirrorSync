namespace SyncServer.Models;

/// <summary>
/// 紀錄 upload session 的基本資訊。
/// </summary>
public class UploadSessionMetadata
{
    public string DatasetId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
