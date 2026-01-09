namespace SyncClient.Models;

/// <summary>
/// 同步狀態保存模型。
/// </summary>
public class SyncState
{
    public DateTime? LastSyncUtc { get; set; }
    public Dictionary<string, ClientFileEntry> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
