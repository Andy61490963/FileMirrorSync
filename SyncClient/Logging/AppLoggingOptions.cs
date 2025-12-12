namespace SyncClient.Logging;

/// <summary>
/// Console 客戶端的 Serilog 設定模型。
/// </summary>
public class AppLoggingOptions
{
    public const string SectionName = "AppLogging";

    /// <summary>
    /// 寫入日誌時的應用識別名稱。
    /// </summary>
    public string ApplicationName { get; set; } = "SyncClient";

    /// <summary>
    /// 最低輸出層級（verbose、debug、information...）。
    /// </summary>
    public string MinimumLevel { get; set; } = "Information";

    /// <summary>
    /// 檔案輸出設定。
    /// </summary>
    public FileLoggingOptions File { get; set; } = new();

    /// <summary>
    /// Seq 伺服器輸出設定。
    /// </summary>
    public SeqLoggingOptions Seq { get; set; } = new();
}

/// <summary>
/// 檔案 Sink 的設定。
/// </summary>
public class FileLoggingOptions
{
    /// <summary>
    /// 是否啟用檔案寫入。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 檔案保留天數。
    /// </summary>
    public int RetainDays { get; set; } = 14;

    /// <summary>
    /// 單檔大小上限（MB）。
    /// </summary>
    public int FileSizeLimitMB { get; set; } = 50;
}

/// <summary>
/// Seq Sink 的設定。
/// </summary>
public class SeqLoggingOptions
{
    /// <summary>
    /// 是否啟用 Seq 傳送。
    /// </summary>
    public bool Enabled { get; set; };

    /// <summary>
    /// Seq 伺服器 URL。
    /// </summary>
    public string ServerUrl { get; set; } = "http://localhost:5341";

    /// <summary>
    /// Seq 本地緩衝檔案相對路徑。
    /// </summary>
    public string BufferRelativePath { get; set; } = "seq-buffer";

    /// <summary>
    /// 傳送週期（秒）。
    /// </summary>
    public int PeriodSeconds { get; set; } = 5;
}
