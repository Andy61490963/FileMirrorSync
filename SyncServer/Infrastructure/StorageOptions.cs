using SyncServer.Models;

namespace SyncServer.Infrastructure;

/// <summary>
/// 代表檔案儲存設定。
/// </summary>
public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// inbound 根目錄 (依 datasetId 建立子資料夾)。
    /// </summary>
    public string InboundRoot { get; set; } = string.Empty;

    /// <summary>
    /// 上傳暫存路徑。
    /// </summary>
    public string TempRoot { get; set; } = string.Empty;

    /// <summary>
    /// 刪除策略，控制鏡像刪除行為。
    /// </summary>
    public DeleteStrategy DeleteStrategy { get; set; } = DeleteStrategy.DeleteDisabled;

    /// <summary>
    /// Server 端允許的最大同時上傳數。
    /// </summary>
    public int MaxParallelUploads { get; set; } = 4;
}
