using Microsoft.Extensions.Options;

namespace SyncServer.Infrastructure;

/// <summary>
/// 驗證 API Key 與 datasetId / clientId 是否匹配。
/// </summary>
public class ApiKeyValidator
{
    private readonly ApiKeyOptions _options;

    public ApiKeyValidator(IOptions<ApiKeyOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// 確認 datasetId 或 clientId 的 API key 正確。
    /// </summary>
    public bool IsValid(string datasetId, string clientId, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(datasetId) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        if (_options.DatasetKeys.TryGetValue(datasetId, out var datasetExpected))
        {
            return string.Equals(datasetExpected, apiKey, StringComparison.Ordinal);
        }

        return _options.ClientKeys.TryGetValue(clientId, out var expected) &&
               string.Equals(expected, apiKey, StringComparison.Ordinal);
    }
}
