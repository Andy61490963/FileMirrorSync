using System.Text;

namespace SyncServer.Infrastructure;

/// <summary>
/// 提供 Base64Url 編碼/解碼工具。
/// </summary>
public static class PathEncoding
{
    /// <summary>
    /// 將 Base64Url 字串解碼為 UTF-8 路徑。
    /// </summary>
    public static string DecodeBase64Url(string base64Url)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2:
                s += "==";
                break;
            case 3:
                s += "=";
                break;
            case 0:
                break;
            default:
                throw new FormatException("Invalid Base64Url string length.");
        }

        var bytes = Convert.FromBase64String(s);
        return Encoding.UTF8.GetString(bytes);
    }
}
