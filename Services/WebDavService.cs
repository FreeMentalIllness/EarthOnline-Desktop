using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace EarthOnline.Desktop.Services;

/// <summary>
/// 极简 WebDAV 客户端（HttpClient + Basic Auth，不引第三方 WebDAV 库）。
/// 对应安卓 data/network/WebDavService.kt：list(PROPFIND) / upload(PUT) / download(GET) / delete / mkcol。
/// </summary>
public class WebDavService
{
    public sealed record DavConfig(string Url, string User, string Pass);

    private static HttpClient NewClient(DavConfig c)
    {
        var handler = new HttpClientHandler
        {
            // 支持自签名证书（坚果云 / 私有 NAS 常见）
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            AllowAutoRedirect = true
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        if (!string.IsNullOrEmpty(c.User) || !string.IsNullOrEmpty(c.Pass))
        {
            var raw = Encoding.UTF8.GetBytes($"{c.User}:{c.Pass}");
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
        }
        http.DefaultRequestHeaders.UserAgent.ParseAdd("EarthOnline-Desktop/1.0");
        return http;
    }

    private static string Join(string baseUrl, string path)
    {
        var b = baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/";
        var p = path.TrimStart('/');
        return b + p;
    }

    /// <summary>GET 下载文件，返回文本（UTF-8）。404 返回 null。</summary>
    public static async Task<string?> DownloadTextAsync(DavConfig c, string remotePath)
    {
        using var http = NewClient(c);
        var resp = await http.GetAsync(Join(c.Url, remotePath));
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    /// <summary>PUT 上传文本。</summary>
    public static async Task UploadTextAsync(DavConfig c, string remotePath, string text)
    {
        using var http = NewClient(c);
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var resp = await http.PutAsync(Join(c.Url, remotePath), content);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>MKCOL 建目录（已存在返回 405 视为成功，与安卓一致）。</summary>
    public static async Task MkcolAsync(DavConfig c, string remotePath)
    {
        using var http = NewClient(c);
        var req = new HttpRequestMessage(new HttpMethod("MKCOL"), Join(c.Url, remotePath));
        var resp = await http.SendAsync(req);
        if (!resp.IsSuccessStatusCode && (int)resp.StatusCode != 405)
        {
            resp.EnsureSuccessStatusCode();
        }
    }

    /// <summary>DELETE 删除。</summary>
    public static async Task DeleteAsync(DavConfig c, string remotePath)
    {
        using var http = NewClient(c);
        var resp = await http.DeleteAsync(Join(c.Url, remotePath));
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>PROPFIND 列目录（Depth=1），返回 href 列表。用于「浏览云端文件」。</summary>
    public static async Task<List<DavEntry>> ListAsync(DavConfig c, string remotePath)
    {
        using var http = NewClient(c);
        const string body = "<?xml version=\"1.0\"?><propfind xmlns=\"DAV:\"><prop><resourcetype/><getcontentlength/></prop></propfind>";
        var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), Join(c.Url, remotePath))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/xml")
        };
        req.Headers.Add("Depth", "1");
        var resp = await http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var xml = await resp.Content.ReadAsStringAsync();
        return ParseProps(xml);
    }

    public sealed record DavEntry(string Href, bool IsDir, long Size);

    /// <summary>最小 XML 解析：只取 href / collection / getcontentlength（对应安卓 parseProps）。</summary>
    private static List<DavEntry> ParseProps(string xml)
    {
        var list = new List<DavEntry>();
        foreach (var block in RegexMatches(xml, "<(?:D:)?response>(.*?)</(?:D:)?response>"))
        {
            var href = RegexMatches(block, "<(?:D:)?href>(.*?)</(?:D:)?href>").FirstOrDefault();
            if (href is null) continue;
            var isDir = block.Contains("<D:collection") || block.Contains("<collection");
            var sizeStr = RegexMatches(block, "<(?:D:)?getcontentlength>(\\d+)<").FirstOrDefault();
            long.TryParse(sizeStr, out var size);
            list.Add(new DavEntry(href.Trim(), isDir, size));
        }
        return list;
    }

    private static IEnumerable<string> RegexMatches(string input, string pattern)
    {
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(input, pattern,
                     System.Text.RegularExpressions.RegexOptions.Singleline))
        {
            if (m.Success && m.Groups.Count > 1) yield return m.Groups[1].Value;
        }
    }
}
