using Microsoft.Extensions.Options;

namespace SyncServer.Infrastructure;

/// <summary>
/// 驗證 API Key 與 clientId 是否匹配。
/// </summary>
public class ApiKeyValidator
{
    private readonly ApiKeyOptions _options;

    public ApiKeyValidator(IOptions<ApiKeyOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// 確認 clientId 的 API key 正確。
    /// </summary>
    public bool IsValid(string clientId, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        return _options.Keys.TryGetValue(clientId, out var expected) &&
               string.Equals(expected, apiKey, StringComparison.Ordinal);
    }
}
