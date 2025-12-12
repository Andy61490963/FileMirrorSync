using Microsoft.AspNetCore.Mvc;
using SyncServer.Infrastructure;
using SyncServer.Models;
using SyncServer.Services;
using Microsoft.Extensions.Logging;

namespace SyncServer.Controllers;

[ApiController]
[Route("api/sync")]
public class SyncController : ControllerBase
{
    private readonly ApiKeyValidator _apiKeyValidator;
    private readonly FileSyncService _fileSyncService;
    private readonly ILogger<SyncController> _logger;

    public SyncController(ApiKeyValidator apiKeyValidator, FileSyncService fileSyncService, ILogger<SyncController> logger)
    {
        _apiKeyValidator = apiKeyValidator;
        _fileSyncService = fileSyncService;
        _logger = logger;
    }

    /// <summary>
    /// 接收 manifest 並回傳差異清單。
    /// </summary>
    [HttpPost("manifest")]
    public ActionResult<ManifestDiffResponse> PostManifest([FromHeader(Name = "X-Api-Key")] string? apiKey, [FromBody] ManifestRequest request)
    {
        if (!_apiKeyValidator.IsValid(request.ClientId, apiKey))
        {
            _logger.LogWarning("Manifest 驗證失敗，ClientId: {ClientId}", request.ClientId);
            return Unauthorized();
        }

        var result = _fileSyncService.Diff(request.ClientId, request.Files);
        _logger.LogInformation("回傳 Manifest 差異，ClientId: {ClientId}", request.ClientId);
        return Ok(result);
    }

    /// <summary>
    /// 接收單一 chunk，檔名以 base64 轉碼。
    /// </summary>
    [HttpPut("files/{base64Path}/chunks/{index}")]
    public async Task<IActionResult> PutChunk([FromHeader(Name = "X-Api-Key")] string? apiKey, [FromRoute] string base64Path, [FromRoute] int index, [FromQuery] string clientId, CancellationToken ct)
    {
        if (!_apiKeyValidator.IsValid(clientId, apiKey))
        {
            _logger.LogWarning("Chunk 驗證失敗，ClientId: {ClientId}, Index: {Index}", clientId, index);
            return Unauthorized();
        }

        await _fileSyncService.SaveChunkAsync(clientId, base64Path, index, Request.Body, ct);
        _logger.LogInformation("接收 chunk 成功，ClientId: {ClientId}, Index: {Index}", clientId, index);
        return NoContent();
    }

    /// <summary>
    /// 完成檔案上傳，Server 合併所有 chunk。
    /// </summary>
    [HttpPost("files/{base64Path}/complete")]
    public async Task<IActionResult> Complete([FromHeader(Name = "X-Api-Key")] string? apiKey, [FromRoute] string base64Path, [FromBody] CompleteUploadRequest request, CancellationToken ct)
    {
        if (!_apiKeyValidator.IsValid(request.ClientId, apiKey))
        {
            _logger.LogWarning("完成上傳驗證失敗，ClientId: {ClientId}", request.ClientId);
            return Unauthorized();
        }

        await _fileSyncService.CompleteUploadAsync(request.ClientId, base64Path, request, ct);
        _logger.LogInformation("完成檔案合併，ClientId: {ClientId}, Path: {Path}", request.ClientId, base64Path);
        return NoContent();
    }

    /// <summary>
    /// 刪除 Server 端的檔案，僅限鏡像根目錄內。
    /// </summary>
    [HttpPost("delete")]
    public IActionResult Delete([FromHeader(Name = "X-Api-Key")] string? apiKey, [FromBody] DeleteRequest request)
    {
        if (!_apiKeyValidator.IsValid(request.ClientId, apiKey))
        {
            _logger.LogWarning("刪除請求驗證失敗，ClientId: {ClientId}", request.ClientId);
            return Unauthorized();
        }

        _fileSyncService.DeleteFiles(request.ClientId, request.Paths);
        _logger.LogInformation("刪除請求完成，ClientId: {ClientId}，數量: {Count}", request.ClientId, request.Paths.Count);
        return NoContent();
    }
}
