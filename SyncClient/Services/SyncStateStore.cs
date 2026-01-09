using System.Text.Json;
using Serilog;
using SyncClient.Models;

namespace SyncClient.Services;

/// <summary>
/// 以 JSON 檔案保存同步狀態，避免重複計算 Hash。
/// </summary>
public class SyncStateStore
{
    private readonly string _stateFile;
    private readonly Serilog.ILogger _logger;

    public SyncStateStore(string stateFile)
        : this(stateFile, Log.ForContext<SyncStateStore>())
    {
    }

    public SyncStateStore(string stateFile, Serilog.ILogger logger)
    {
        _stateFile = stateFile;
        _logger = logger.ForContext<SyncStateStore>();
    }

    /// <summary>
    /// 載入前一次同步狀態。
    /// </summary>
    public SyncState Load()
    {
        if (!File.Exists(_stateFile))
        {
            _logger.Information("狀態檔不存在，將建立新的檔案：{File}", _stateFile);
            _logger.Information("CurrentDirectory = {Dir}", Environment.CurrentDirectory);
            return new SyncState();
        }

        var json = File.ReadAllText(_stateFile);
        var state = JsonSerializer.Deserialize<SyncState>(json) ?? new SyncState();
        _logger.Information("已載入狀態檔：{File}，筆數：{Count}", _stateFile, state.Files.Count);
        return state;
    }

    /// <summary>
    /// 儲存最新的檔案狀態。
    /// </summary>
    public void Save(SyncState state)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_stateFile, json);
        _logger.Information("狀態檔已更新至 {File}，筆數：{Count}", _stateFile, state.Files.Count);
    }
}
