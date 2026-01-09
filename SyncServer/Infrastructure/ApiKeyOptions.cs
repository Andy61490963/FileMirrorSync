namespace SyncServer.Infrastructure;

/// <summary>
/// API Key 對應設定，可依 datasetId 或 clientId 綁定。
/// </summary>
public class ApiKeyOptions
{
    public const string SectionName = "ApiKeys";

    /// <summary>
    /// datasetId 與 API key 對應表。
    /// </summary>
    public Dictionary<string, string> DatasetKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// clientId 與 API key 對應表。
    /// </summary>
    public Dictionary<string, string> ClientKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
