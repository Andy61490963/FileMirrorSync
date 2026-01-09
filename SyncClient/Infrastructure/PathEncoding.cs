using System.Text;

namespace SyncClient.Infrastructure;

/// <summary>
/// 提供 Base64Url 編碼工具。
/// </summary>
public static class PathEncoding
{
    /// <summary>
    /// 將路徑轉成 Base64Url 字串。
    /// </summary>
    public static string EncodeBase64Url(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var base64 = Convert.ToBase64String(bytes);
        return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
