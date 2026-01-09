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
    private readonly ManifestDiffService _manifestDiffService;
    private readonly UploadSessionService _uploadSessionService;
    private readonly FileMergeService _fileMergeService;
    private readonly DeleteService _deleteService;
    private readonly ILogger<SyncController> _logger;

    public SyncController(
        ApiKeyValidator apiKeyValidator,
        ManifestDiffService manifestDiffService,
        UploadSessionService uploadSessionService,
        FileMergeService fileMergeService,
        DeleteService deleteService,
        ILogger<SyncController> logger)
    {
        _apiKeyValidator = apiKeyValidator;
        _manifestDiffService = manifestDiffService;
        _uploadSessionService = uploadSessionService;
        _fileMergeService = fileMergeService;
        _deleteService = deleteService;
        _logger = logger;
    }

    /// <summary>
    /// 接收 manifest 並回傳差異清單。
    /// </summary>
    [HttpPost("manifest")]
    public ActionResult<ManifestDiffResponse> PostManifest([FromHeader(Name = "X-Api-Key")] string? apiKey, [FromBody] ManifestRequest request)
    {
        if (!_apiKeyValidator.IsValid(request.DatasetId, request.ClientId, apiKey))
        {
            _logger.LogWarning("Manifest 驗證失敗 Dataset={DatasetId} ClientId={ClientId}", request.DatasetId, request.ClientId);
            return Unauthorized();
        }

        var result = _manifestDiffService.BuildDiff(request);
        _logger.LogInformation("回傳 Manifest 差異 Dataset={DatasetId} ClientId={ClientId}", request.DatasetId, request.ClientId);
        return Ok(result);
    }

    /// <summary>
    /// 接收單一 chunk，檔名以 base64 轉碼。
    /// </summary>
    [HttpPut("files/{base64Path}/uploads/{uploadId}/chunks/{index}")]
    public async Task<IActionResult> PutChunk(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromRoute] string base64Path,
        [FromRoute] Guid uploadId,
        [FromRoute] int index,
        [FromQuery] string datasetId,
        [FromQuery] string clientId,
        CancellationToken ct)
    {
        if (!_apiKeyValidator.IsValid(datasetId, clientId, apiKey))
        {
            _logger.LogWarning("Chunk 驗證失敗 Dataset={DatasetId} ClientId={ClientId} Index={Index}", datasetId, clientId, index);
            return Unauthorized();
        }

        var relativePath = PathEncoding.DecodeBase64Url(base64Path);
        var session = _uploadSessionService.GetSession(datasetId, uploadId);
        if (!string.Equals(session.ClientId, clientId, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("UploadId 與 clientId 不一致");
        }

        if (!string.Equals(session.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("UploadId 與路徑不一致");
        }

        await _fileMergeService.SaveChunkAsync(datasetId, uploadId, relativePath, index, Request.Body, ct);
        _logger.LogInformation("接收 chunk 成功 Dataset={DatasetId} ClientId={ClientId} Index={Index}", datasetId, clientId, index);
        return NoContent();
    }

    /// <summary>
    /// 完成檔案上傳，Server 合併所有 chunk。
    /// </summary>
    [HttpPost("files/{base64Path}/uploads/{uploadId}/complete")]
    public async Task<IActionResult> Complete(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromRoute] string base64Path,
        [FromRoute] Guid uploadId,
        [FromBody] CompleteUploadRequest request,
        CancellationToken ct)
    {
        if (!_apiKeyValidator.IsValid(request.DatasetId, request.ClientId, apiKey))
        {
            _logger.LogWarning("完成上傳驗證失敗 Dataset={DatasetId} ClientId={ClientId}", request.DatasetId, request.ClientId);
            return Unauthorized();
        }

        var relativePath = PathEncoding.DecodeBase64Url(base64Path);
        var session = _uploadSessionService.GetSession(request.DatasetId, uploadId);
        if (!string.Equals(session.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("UploadId 與路徑不一致");
        }

        await _fileMergeService.CompleteUploadAsync(uploadId, request, ct);
        _logger.LogInformation("完成檔案合併 Dataset={DatasetId} ClientId={ClientId} Path={Path}", request.DatasetId, request.ClientId, relativePath);
        return NoContent();
    }

    /// <summary>
    /// 刪除 Server 端的檔案，僅限鏡像根目錄內。
    /// </summary>
    [HttpPost("delete")]
    public IActionResult Delete([FromHeader(Name = "X-Api-Key")] string? apiKey, [FromBody] DeleteRequest request)
    {
        if (!_apiKeyValidator.IsValid(request.DatasetId, request.ClientId, apiKey))
        {
            _logger.LogWarning("刪除請求驗證失敗 Dataset={DatasetId} ClientId={ClientId}", request.DatasetId, request.ClientId);
            return Unauthorized();
        }

        _deleteService.ApplyDelete(request);
        _logger.LogInformation("刪除請求完成 Dataset={DatasetId} ClientId={ClientId} Count={Count}", request.DatasetId, request.ClientId, request.Paths.Count);
        return NoContent();
    }
}
