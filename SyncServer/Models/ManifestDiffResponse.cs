namespace SyncServer.Models;

/// <summary>
/// Server 回應的差異結果，列出待上傳與待刪除清單。
/// </summary>
public class ManifestDiffResponse
{
    public List<string> Upload { get; set; } = new();
    public List<string> Delete { get; set; } = new();
}
