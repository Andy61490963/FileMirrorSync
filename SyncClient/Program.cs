using System.Text.Json;
using Microsoft.Extensions.Configuration;
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
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var settings = new SyncSettings();
        config.Bind(settings);
        return settings;
    }
}
