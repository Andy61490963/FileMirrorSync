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
        // 讀回「上次同步時每個檔案的資訊」（Path → Size/LastWriteTime/Sha256）
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
        IEnumerable<string> uploadList,
        List<ClientFileEntry> files,
        CancellationToken ct)
    {
        // 逐一處理 Server 要求上傳的檔案（相對路徑）
        foreach (var relative in uploadList)
        {
            // 從本次 manifest 中，找出該檔案的完整描述（Size / Sha256）
            // 用於後續 complete 階段的完整性驗證
            var entry = files.First(f =>
                string.Equals(f.Path, relative, StringComparison.OrdinalIgnoreCase));

            // 將檔案路徑轉成 URL-safe 的 Base64（避免特殊字元破壞 URL）
            var base64Path = ToBase64Url(relative);

            // 組合實體檔案的完整路徑
            var filePath = Path.Combine(_settings.RootPath, relative);

            // 以串流方式開啟檔案：
            // - 不一次載入整個檔案（支援大檔）
            // - FileShare.Read 避免其他程式寫入，確保內容穩定
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            // 計算此檔案總共會被切成多少個 chunk
            // Server 會在 complete 階段驗證是否有漏傳 chunk
            var totalChunks = (int)Math.Ceiling(
                (double)stream.Length / _settings.ChunkSize);

            // 建立可重複使用的 buffer，避免每個 chunk 都 new byte[]
            var buffer = new byte[_settings.ChunkSize];

            // Chunk 序號（0-based，與 Server API 契約一致）
            var index = 0;
            int read;

            // 逐 chunk 讀取並上傳
            while ((read = await stream.ReadAsync(
                buffer, 0, buffer.Length, ct)) > 0)
            {
                // 只包裝實際讀到的資料長度（最後一塊可能 < ChunkSize）
                using var content = new ByteArrayContent(buffer, 0, read);

                // Chunk 上傳 API：
                // - base64Path：檔案識別
                // - index：chunk 序號
                // - clientId：區分不同 Client
                var url = $"api/sync/files/{base64Path}/chunks/{index}?clientId={_settings.ClientId}";
                var resp = await _httpClient.PutAsync(url, content, ct);

                // 若上傳失敗，先記錄完整錯誤資訊，再丟出例外中斷同步
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);

                    _logger.Error(
                        "Upload failed. Status={StatusCode} Url={Url} Body={Body}",
                        (int)resp.StatusCode,
                        resp.RequestMessage?.RequestUri?.ToString(),
                        body
                    );

                    // 丟出例外，讓外層流程決定是否重試或終止
                    resp.EnsureSuccessStatusCode();
                }

                // 單一 chunk 上傳成功的紀錄（人類可讀的 1-based 序號）
                _logger.Information(
                    "上傳 chunk 成功，檔案: {File}，序號: {Index}/{Total}",
                    relative, index + 1, totalChunks);

                // 移動到下一個 chunk
                index++;
            }

            // 所有 chunk 上傳完成後，呼叫 complete API
            // 由 Server 驗證檔案是否完整且內容正確
            var completeRequest = new CompleteUploadRequest
            {
                ClientId = _settings.ClientId,

                // 預期檔案大小，用於防止截斷或多寫
                ExpectedSize = entry.Size,

                // 檔案內容雜湊，用於最終內容一致性驗證
                Sha256 = entry.Sha256,

                // 預期 chunk 數量，用於檢查是否有漏傳
                ChunkCount = totalChunks
            };

            var resp1 = await _httpClient.PostAsJsonAsync(
                $"api/sync/files/{base64Path}/complete",
                completeRequest,
                ct);

            // Complete 階段失敗，同樣視為同步失敗
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

            // 此檔案已完整上傳並通過 Server 驗證
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
