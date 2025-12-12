using Serilog;
using SyncServer.Infrastructure;
using SyncServer.Logging;
using SyncServer.Services;

Log.Logger = SerilogConfigurator.CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, _, configuration) =>
    {
        var options = context.Configuration.GetSection(AppLoggingOptions.SectionName).Get<AppLoggingOptions>() ?? new();
        SerilogConfigurator.Configure(configuration, options);
    });

    // 載入設定並註冊 DI
    builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
    builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection(ApiKeyOptions.SectionName));
    builder.Services.Configure<AppLoggingOptions>(builder.Configuration.GetSection(AppLoggingOptions.SectionName));
    builder.Services.AddSingleton<ApiKeyValidator>();
    builder.Services.AddSingleton<PathMapper>();
    builder.Services.AddSingleton<FileSyncService>();
    builder.Services.AddControllers();

    var app = builder.Build();

    app.UseHttpsRedirection();
    app.UseAuthorization();
    app.MapControllers();

    Log.Information("SyncServer 啟動完成，開始接受請求");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "SyncServer 啟動失敗");
}
finally
{
    Log.CloseAndFlush();
}
