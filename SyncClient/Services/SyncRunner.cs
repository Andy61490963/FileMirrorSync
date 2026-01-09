using System.Net.Http.Json;
using System.Security.Cryptography;
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
        // 讀回「上次同步狀態」（避免重複計算）
        _ = _stateStore.Load();
        var files = _manifestBuilder.Build();

        _logger.Information("Manifest 建立完成，共 {Count} 筆項目，開始比對差異", files.Count);

        var manifest = new ManifestRequest
        {
            DatasetId = _settings.DatasetId,
            ClientId = _settings.ClientId,
            Files = files
        };

        var diff = await PostManifestAsync(manifest, ct);
        _logger.Information("伺服器回傳差異，需上傳 {Upload} 筆、需刪除 {Delete} 筆", diff.Upload.Count, diff.Delete.Count);
        await UploadFilesAsync(diff.Upload, files, ct);
        await DeleteFilesAsync(diff.Delete, ct);

        var newState = new SyncState
        {
            LastSyncUtc = DateTime.UtcNow,
            Files = files.ToDictionary(f => f.Path, f => f, StringComparer.OrdinalIgnoreCase)
        };
        _stateStore.Save(newState);
        _logger.Information("狀態檔已更新，檔案數: {Count}", newState.Files.Count);
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
    /// 
    /// 依照 Server 回傳的差異清單，逐一以「chunk 上傳」方式同步檔案。
    /// 
    /// 流程說明：
    /// 1. 針對每一個需要上傳的檔案：
    ///    - 開啟檔案串流（不一次讀進記憶體，避免大檔 OOM）
    ///    - 依固定 ChunkSize 切割檔案
    ///    - 逐 chunk 上傳至 Server
    /// 2. 所有 chunk 上傳完成後：
    ///    - 呼叫 complete API
    ///    - 由 Server 驗證檔案完整性（Size / Sha256 / ChunkCount）
    /// 
    /// 注意：
    /// - 支援 CancellationToken，可安全中斷上傳流程
    /// - 若任一 chunk 或 complete 失敗，會直接拋例外終止同步
    /// </summary>
    private async Task UploadFilesAsync(
        IEnumerable<UploadInstruction> uploadList,
        List<ClientFileEntry> files,
        CancellationToken ct)
    {
        var lookup = files.ToDictionary(f => f.Path, f => f, StringComparer.OrdinalIgnoreCase);
        var maxParallel = Math.Max(1, _settings.MaxParallelUploads);
        using var throttler = new SemaphoreSlim(maxParallel, maxParallel);
        var tasks = uploadList.Select(async instruction =>
        {
            await throttler.WaitAsync(ct);
            try
            {
                if (!lookup.TryGetValue(instruction.Path, out var entry))
                {
                    _logger.Warning("找不到檔案項目，略過上傳 Path={Path}", instruction.Path);
                    return;
                }

                await UploadSingleFileAsync(entry, instruction, ct);
            }
            finally
            {
                throttler.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);
    }
    
    /// <summary>
    /// 發送刪除請求，使 Server 鏡像同步。
    /// </summary>
    private async Task DeleteFilesAsync(IEnumerable<string> deleteList, CancellationToken ct)
    {
        if (!_settings.EnableDelete)
        {
            _logger.Information("刪除同步已停用，忽略 delete 清單");
            return;
        }

        if (!deleteList.Any())
        {
            _logger.Information("無檔案需要刪除");
            return;
        }

        var request = new DeleteRequest
        {
            DatasetId = _settings.DatasetId,
            ClientId = _settings.ClientId,
            Paths = deleteList.ToList(),
            DeletedAtUtc = DateTime.UtcNow
        };

        var response = await _httpClient.PostAsJsonAsync("api/sync/delete", request, ct);
        response.EnsureSuccessStatusCode();
        _logger.Information("刪除請求已完成，共刪除 {Count} 筆", request.Paths.Count);
    }

    /// <summary>
    /// 上傳單一檔案並在 Complete 階段驗證。
    /// </summary>
    private async Task UploadSingleFileAsync(ClientFileEntry entry, UploadInstruction instruction, CancellationToken ct)
    {
        var relative = entry.Path;
        var base64Path = PathEncoding.EncodeBase64Url(relative);
        var filePath = Path.Combine(_settings.RootPath, relative);

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var totalChunks = (int)Math.Ceiling((double)stream.Length / _settings.ChunkSize);
        var buffer = new byte[_settings.ChunkSize];
        var index = 0;
        int read;

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            hasher.AppendData(buffer, 0, read);

            using var content = new ByteArrayContent(buffer, 0, read);
            var url =
                $"api/sync/files/{base64Path}/uploads/{instruction.UploadId}/chunks/{index}?datasetId={_settings.DatasetId}&clientId={_settings.ClientId}";
            var resp = await _httpClient.PutAsync(url, content, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.Error(
                    "Upload failed. Status={StatusCode} Url={Url} Body={Body}",
                    (int)resp.StatusCode,
                    resp.RequestMessage?.RequestUri?.ToString(),
                    body);
                resp.EnsureSuccessStatusCode();
            }

            _logger.Information("上傳 chunk 成功，檔案: {File}，序號: {Index}/{Total}", relative, index + 1, totalChunks);
            index++;
        }

        var hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        var completeRequest = new CompleteUploadRequest
        {
            DatasetId = _settings.DatasetId,
            ClientId = _settings.ClientId,
            ExpectedSize = entry.Size,
            Sha256 = hash,
            ChunkCount = totalChunks,
            LastWriteUtc = entry.LastWriteUtc
        };

        var completeResponse = await _httpClient.PostAsJsonAsync(
            $"api/sync/files/{base64Path}/uploads/{instruction.UploadId}/complete",
            completeRequest,
            ct);

        if (!completeResponse.IsSuccessStatusCode)
        {
            var body = await completeResponse.Content.ReadAsStringAsync(ct);
            _logger.Error(
                "Upload failed. Status={StatusCode} Url={Url} Body={Body}",
                (int)completeResponse.StatusCode,
                completeResponse.RequestMessage?.RequestUri?.ToString(),
                body);
            completeResponse.EnsureSuccessStatusCode();
        }

        _logger.Information("檔案上傳完成並驗證，檔案: {File}", relative);
    }
}
