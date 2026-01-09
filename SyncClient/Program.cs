using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Serilog;
using SyncClient.Infrastructure;
using SyncClient.Logging;
using SyncClient.Services;

namespace SyncClient;

public static class Program
{
    /// <summary>
    /// 進入點：載入設定並執行同步流程。
    /// </summary>
    public static async Task Main(string[] args)
    {
        var configuration = LoadConfiguration();
        var loggingOptions = configuration.GetSection(AppLoggingOptions.SectionName).Get<AppLoggingOptions>() ?? new();

        Log.Logger = SerilogConfigurator.CreateBootstrapLogger(loggingOptions.ApplicationName);

        try
        {
            var loggerConfiguration = new LoggerConfiguration();
            SerilogConfigurator.Configure(loggerConfiguration, loggingOptions);
            Log.Logger = loggerConfiguration.CreateLogger();

            var settings = LoadSettings(configuration);
            var runner = new SyncRunner(settings, Log.Logger);
            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            Log.Information("同步程序開始，ClientId: {ClientId}, Root: {RootPath}", settings.ClientId, settings.RootPath);

            // 程式入口點
            await runner.RunAsync(cts.Token);

            Log.Information("同步完成");
        }
        catch (OperationCanceledException)
        {
            Log.Warning("同步程序已取消");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "同步程序發生未預期錯誤");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// 載入應用程式設定。
    /// </summary>
    private static IConfigurationRoot LoadConfiguration()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        return config;
    }

    /// <summary>
    /// 讀取同步設定並綁定至模型。
    /// </summary>
    private static SyncSettings LoadSettings(IConfiguration configuration)
    {
        var settings = new SyncSettings();
        configuration.Bind(settings);
        return settings;
    }
}
