namespace SyncServer.Infrastructure;

/// <summary>
/// 代表檔案儲存設定。
/// </summary>
public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// inbound 根目錄 (依 clientId 建立子資料夾)。
    /// </summary>
    public string InboundRoot { get; set; } = string.Empty;

    /// <summary>
    /// 上傳暫存路徑。
    /// </summary>
    public string TempRoot { get; set; } = string.Empty;
}
