namespace SyncServer.Infrastructure;

/// <summary>
/// API Key 對應設定，每個 clientId 需設定專屬 key。
/// </summary>
public class ApiKeyOptions
{
    public const string SectionName = "ApiKeys";

    /// <summary>
    /// clientId 與 API key 對應表。
    /// </summary>
    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
