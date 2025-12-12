namespace SyncClient.Models;

/// <summary>
/// 對 Server 端要求刪除檔案的模型。
/// </summary>
public class DeleteRequest
{
    public string ClientId { get; set; } = string.Empty;
    public List<string> Paths { get; set; } = new();
}
