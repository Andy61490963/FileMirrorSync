using System.Text.Json;
using SyncClient.Infrastructure;
using SyncClient.Services;

namespace SyncClient;

public static class Program
{
    /// <summary>
    /// 進入點：載入設定並執行同步流程。
    /// </summary>
    public static async Task Main(string[] args)
    {
        var settings = LoadSettings();
        var runner = new SyncRunner(settings);
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await runner.RunAsync(cts.Token);
        Console.WriteLine("同步完成");
    }

    private static SyncSettings LoadSettings()
    {
        const string file = "appsettings.json";
        if (!File.Exists(file))
        {
            return new SyncSettings();
        }

        var json = File.ReadAllText(file);
        return JsonSerializer.Deserialize<SyncSettings>(json) ?? new SyncSettings();
    }
}
