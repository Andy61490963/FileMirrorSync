namespace SyncClient.Infrastructure;

/// <summary>
/// 同步設定模型。
/// </summary>
public class SyncSettings
{
    public string DatasetId { get; set; }
    public string ClientId { get; set; }
    public string ApiKey { get; set; }
    public string ServerBaseUrl { get; set; } 
    public string RootPath { get; set; }
    public string StateFile { get; set; }
    public int ChunkSize { get; set; }
    public int MaxParallelUploads { get; set; } = 2;
    public bool EnableDelete { get; set; } = false;
}
