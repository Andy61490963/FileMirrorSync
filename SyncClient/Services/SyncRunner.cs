using System.Net.Http.Json;
using System.Text;
using Serilog;
using SyncClient.Infrastructure;
using SyncClient.Models;

namespace SyncClient.Services;

/// <summary>
/// 控制完整同步流程的指揮者。
/// </summary>
public class SyncRunner
{
    private readonly SyncSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly SyncStateStore _stateStore;
    private readonly ManifestBuilder _manifestBuilder;
    private readonly Serilog.ILogger _logger;

    public SyncRunner(SyncSettings settings, Serilog.ILogger logger)
    {
        _settings = settings;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(settings.ServerBaseUrl)
        };
        _httpClient.DefaultRequestHeaders.Add("X-Api-Key", settings.ApiKey);
        _logger = logger.ForContext<SyncRunner>();
        _stateStore = new SyncStateStore(settings.StateFile, _logger);
        _manifestBuilder = new ManifestBuilder(settings.RootPath, _logger);
    }

    /// <summary>
    /// 依序執行 manifest 上傳、差異上傳與刪除同步。
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var previous = _stateStore.Load();
        var files = _manifestBuilder.Build(previous);

        _logger.Information("Manifest 建立完成，共 {Count} 筆項目，開始比對差異", files.Count);

        var manifest = new ManifestRequest
        {
            ClientId = _settings.ClientId,
            Files = files
        };

        var diff = await PostManifestAsync(manifest, ct);
        _logger.Information("伺服器回傳差異，需上傳 {Upload} 筆、需刪除 {Delete} 筆", diff.Upload.Count, diff.Delete.Count);
        await UploadFilesAsync(diff.Upload, files, ct);
        await DeleteFilesAsync(diff.Delete, ct);

        var newState = files.ToDictionary(f => f.Path, f => f, StringComparer.OrdinalIgnoreCase);
        _stateStore.Save(newState);
        _logger.Information("狀態檔已更新，檔案數: {Count}", newState.Count);
    }

    /// <summary>
    /// 呼叫 Server 取得差異清單。
    /// </summary>
    private async Task<ManifestDiffResponse> PostManifestAsync(ManifestRequest request, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync("api/sync/manifest", request, ct);
        response.EnsureSuccessStatusCode();
        _logger.Information("Manifest 已送出，等待差異回應");
        return (await response.Content.ReadFromJsonAsync<ManifestDiffResponse>(cancellationToken: ct))!;
    }

    /// <summary>
    /// 依差異清單逐檔以 chunk 上傳。
    /// </summary>
    private async Task UploadFilesAsync(IEnumerable<string> uploadList, List<ClientFileEntry> files, CancellationToken ct)
    {
        foreach (var relative in uploadList)
        {
            var entry = files.First(f => string.Equals(f.Path, relative, StringComparison.OrdinalIgnoreCase));
            var base64Path = ToBase64Url(relative);
            var filePath = Path.Combine(_settings.RootPath, relative);

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var totalChunks = (int)Math.Ceiling((double)stream.Length / _settings.ChunkSize);
            var buffer = new byte[_settings.ChunkSize];
            var index = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                using var content = new ByteArrayContent(buffer, 0, read);
                var url = $"api/sync/files/{base64Path}/chunks/{index}?clientId={_settings.ClientId}";
                var resp = await _httpClient.PutAsync(url, content, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);

                    _logger.Error(
                        "Upload failed. Status={StatusCode} Url={Url} Body={Body}",
                        (int)resp.StatusCode,
                        resp.RequestMessage?.RequestUri?.ToString(),
                        body
                    );

                    resp.EnsureSuccessStatusCode();
                }
                
                _logger.Information("上傳 chunk 成功，檔案: {File}，序號: {Index}/{Total}", relative, index + 1, totalChunks);
                index++;
            }

            var completeRequest = new CompleteUploadRequest
            {
                ClientId = _settings.ClientId,
                ExpectedSize = entry.Size,
                Sha256 = entry.Sha256,
                ChunkCount = totalChunks
            };
            var resp1 = await _httpClient.PostAsJsonAsync($"api/sync/files/{base64Path}/complete", completeRequest, ct);
            if (!resp1.IsSuccessStatusCode)
            {
                var body = await resp1.Content.ReadAsStringAsync(ct);

                _logger.Error(
                    "Upload failed. Status={StatusCode} Url={Url} Body={Body}",
                    (int)resp1.StatusCode,
                    resp1.RequestMessage?.RequestUri?.ToString(),
                    body
                );

                resp1.EnsureSuccessStatusCode();
            }
            _logger.Information("檔案上傳完成並驗證，檔案: {File}", relative);
        }
    }

    private static string ToBase64Url(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var base64 = Convert.ToBase64String(bytes);

        // Base64Url: '+' -> '-', '/' -> '_', 去掉 '=' padding
        return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
    
    /// <summary>
    /// 發送刪除請求，使 Server 鏡像同步。
    /// </summary>
    private async Task DeleteFilesAsync(IEnumerable<string> deleteList, CancellationToken ct)
    {
        if (!deleteList.Any())
        {
            _logger.Information("無檔案需要刪除");
            return;
        }

        var request = new DeleteRequest
        {
            ClientId = _settings.ClientId,
            Paths = deleteList.ToList()
        };

        var response = await _httpClient.PostAsJsonAsync("api/sync/delete", request, ct);
        response.EnsureSuccessStatusCode();
        _logger.Information("刪除請求已完成，共刪除 {Count} 筆", request.Paths.Count);
    }
}
