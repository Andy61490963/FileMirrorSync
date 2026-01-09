namespace SyncClient.Models;

/// <summary>
/// 上傳 manifest 的請求物件。
/// </summary>
public class ManifestRequest
{
    public string DatasetId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public List<ClientFileEntry> Files { get; set; } = new();
}
