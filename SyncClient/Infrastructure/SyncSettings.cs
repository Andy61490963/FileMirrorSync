namespace SyncClient.Infrastructure;

/// <summary>
/// 同步設定模型。
/// </summary>
public class SyncSettings
{
    public string ClientId { get; set; } = "demo";
    public string ApiKey { get; set; } = "demo-secret-key";
    public string ServerBaseUrl { get; set; } = "https://localhost";
    public string RootPath { get; set; } = "/data/source";
    public string StateFile { get; set; } = "sync-state.json";
    public int ChunkSize { get; set; } = 8 * 1024 * 1024;
}
