using SyncServer.Models;

namespace SyncServer.Services;

/// <summary>
/// 提供 LWW 規則判斷的策略服務。
/// </summary>
public class VersionPolicy
{
    /// <summary>
    /// 判斷 Client 版本是否應該上傳。
    /// </summary>
    public bool ShouldUpload(FileEntry serverEntry, FileEntry clientEntry)
    {
        if (clientEntry.LastWriteUtc > serverEntry.LastWriteUtc)
        {
            return true;
        }

        if (clientEntry.LastWriteUtc == serverEntry.LastWriteUtc)
        {
            if (clientEntry.Size != serverEntry.Size)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(clientEntry.Sha256) &&
                !string.Equals(clientEntry.Sha256, serverEntry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判斷是否允許以 Client 版本覆蓋 Server 端檔案。
    /// </summary>
    public bool ShouldOverwrite(DateTime? serverLastWriteUtc, DateTime clientLastWriteUtc)
    {
        if (!serverLastWriteUtc.HasValue)
        {
            return true;
        }

        return clientLastWriteUtc > serverLastWriteUtc.Value;
    }
}
