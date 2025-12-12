namespace SyncClient.Models;

/// <summary>
/// Server 回應的差異清單模型。
/// </summary>
public class ManifestDiffResponse
{
    public List<string> Upload { get; set; } = new();
    public List<string> Delete { get; set; } = new();
}
