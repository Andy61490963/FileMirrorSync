namespace SyncServer.Models;

/// <summary>
/// 定義 Server 端的刪除策略。
/// </summary>
public enum DeleteStrategy
{
    /// <summary>
    /// 不啟用鏡像刪除。
    /// </summary>
    DeleteDisabled = 0,

    /// <summary>
    /// 使用 LWW 規則刪除，需比對 DeletedAtUtc。
    /// </summary>
    LwwDelete = 1
}
