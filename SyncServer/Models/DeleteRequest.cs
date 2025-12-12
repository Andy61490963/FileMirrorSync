namespace SyncServer.Models;

/// <summary>
/// 刪除請求，包含 clientId 與要刪除的檔案路徑。
/// </summary>
public class DeleteRequest
{
    public string ClientId { get; set; } = string.Empty;
    public List<string> Paths { get; set; } = new();
}
