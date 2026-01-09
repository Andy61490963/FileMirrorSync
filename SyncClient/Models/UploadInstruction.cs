namespace SyncClient.Models;

/// <summary>
/// 描述待上傳檔案與對應的 UploadId。
/// </summary>
public class UploadInstruction
{
    public string Path { get; set; } = string.Empty;
    public Guid UploadId { get; set; }
}
