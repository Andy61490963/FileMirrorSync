using SyncServer.Infrastructure;
using SyncServer.Services;

var builder = WebApplication.CreateBuilder(args);

// 載入設定並註冊 DI
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection(ApiKeyOptions.SectionName));
builder.Services.AddSingleton<ApiKeyValidator>();
builder.Services.AddSingleton<PathMapper>();
builder.Services.AddSingleton<FileSyncService>();
builder.Services.AddControllers();

var app = builder.Build();

// 強制 HTTPS 導向
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
