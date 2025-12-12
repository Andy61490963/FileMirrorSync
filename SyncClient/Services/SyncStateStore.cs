using System.Text.Json;
using SyncClient.Models;

namespace SyncClient.Services;

/// <summary>
/// 以 JSON 檔案保存同步狀態，避免重複計算 Hash。
/// </summary>
public class SyncStateStore
{
    private readonly string _stateFile;

    public SyncStateStore(string stateFile)
    {
        _stateFile = stateFile;
    }

    /// <summary>
    /// 載入前一次同步的檔案資訊。
    /// </summary>
    public Dictionary<string, ClientFileEntry> Load()
    {
        if (!File.Exists(_stateFile))
        {
            return new Dictionary<string, ClientFileEntry>(StringComparer.OrdinalIgnoreCase);
        }

        var json = File.ReadAllText(_stateFile);
        return JsonSerializer.Deserialize<Dictionary<string, ClientFileEntry>>(json) ?? new Dictionary<string, ClientFileEntry>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 儲存最新的檔案狀態。
    /// </summary>
    public void Save(Dictionary<string, ClientFileEntry> state)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_stateFile, json);
    }
}
