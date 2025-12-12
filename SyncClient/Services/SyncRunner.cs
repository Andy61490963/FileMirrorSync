using System.Net.Http.Json;
using System.Text;
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

    public SyncRunner(SyncSettings settings)
    {
        _settings = settings;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(settings.ServerBaseUrl)
        };
        _httpClient.DefaultRequestHeaders.Add("X-Api-Key", settings.ApiKey);
        _stateStore = new SyncStateStore(settings.StateFile);
        _manifestBuilder = new ManifestBuilder(settings.RootPath);
    }

    /// <summary>
    /// 依序執行 manifest 上傳、差異上傳與刪除同步。
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var previous = _stateStore.Load();
        var files = _manifestBuilder.Build(previous);

        var manifest = new ManifestRequest
        {
            ClientId = _settings.ClientId,
            Files = files
        };

        var diff = await PostManifestAsync(manifest, ct);
        await UploadFilesAsync(diff.Upload, files, ct);
        await DeleteFilesAsync(diff.Delete, ct);

        var newState = files.ToDictionary(f => f.Path, f => f, StringComparer.OrdinalIgnoreCase);
        _stateStore.Save(newState);
    }

    /// <summary>
    /// 呼叫 Server 取得差異清單。
    /// </summary>
    private async Task<ManifestDiffResponse> PostManifestAsync(ManifestRequest request, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync("api/sync/manifest", request, ct);
        response.EnsureSuccessStatusCode();
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
            var base64Path = Convert.ToBase64String(Encoding.UTF8.GetBytes(relative));
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
                var response = await _httpClient.PutAsync(url, content, ct);
                response.EnsureSuccessStatusCode();
                index++;
            }

            var completeRequest = new CompleteUploadRequest
            {
                ClientId = _settings.ClientId,
                ExpectedSize = entry.Size,
                Sha256 = entry.Sha256,
                ChunkCount = totalChunks
            };
            var completeResponse = await _httpClient.PostAsJsonAsync($"api/sync/files/{base64Path}/complete", completeRequest, ct);
            completeResponse.EnsureSuccessStatusCode();
        }
    }

    /// <summary>
    /// 發送刪除請求，使 Server 鏡像同步。
    /// </summary>
    private async Task DeleteFilesAsync(IEnumerable<string> deleteList, CancellationToken ct)
    {
        if (!deleteList.Any())
        {
            return;
        }

        var request = new DeleteRequest
        {
            ClientId = _settings.ClientId,
            Paths = deleteList.ToList()
        };

        var response = await _httpClient.PostAsJsonAsync("api/sync/delete", request, ct);
        response.EnsureSuccessStatusCode();
    }
}
