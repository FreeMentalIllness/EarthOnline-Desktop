using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EarthOnline.Desktop.Data;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Services;

/// <summary>
/// DPAPI 加密：用当前 Windows 用户凭据保护本地敏感串（AI API Key）。
/// 直接 P/Invoke crypt32.dll —— 不引任何 NuGet 包，也不自己造加密算法。
/// 密文只对「同一台机器 + 同一个 Windows 用户」可解，复制到别处是废数据。
/// </summary>
public static class SecretProtector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    private const uint CrypProtectUiForbidden = 0x1;

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn, string? szDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, uint dwFlags, ref DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn, IntPtr szDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, uint dwFlags, ref DataBlob pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    /// <summary>明文 → DPAPI 密文的 Base64。失败抛异常（绝不静默降级为明文存储）。</summary>
    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var bytes = Encoding.UTF8.GetBytes(plain);
        var handle = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, handle, bytes.Length);
            var input = new DataBlob { cbData = bytes.Length, pbData = handle };
            var output = new DataBlob();
            try
            {
                if (!CryptProtectData(ref input, "EarthOnline AI Key", IntPtr.Zero,
                        IntPtr.Zero, IntPtr.Zero, CrypProtectUiForbidden, ref output))
                {
                    throw new InvalidOperationException(
                        "DPAPI 加密失败（错误码 " + Marshal.GetLastWin32Error() + "）");
                }
                var outBytes = new byte[output.cbData];
                Marshal.Copy(output.pbData, outBytes, 0, output.cbData);
                return Convert.ToBase64String(outBytes);
            }
            finally
            {
                if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(handle);
        }
    }

    /// <summary>密文 Base64 → 明文。数据损坏 / 换机换用户时返回 null（不抛）。</summary>
    public static string? Unprotect(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch { return null; }

        var handle = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, handle, bytes.Length);
            var input = new DataBlob { cbData = bytes.Length, pbData = handle };
            var output = new DataBlob();
            try
            {
                if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, IntPtr.Zero, CrypProtectUiForbidden, ref output))
                {
                    return null;
                }
                var outBytes = new byte[output.cbData];
                Marshal.Copy(output.pbData, outBytes, 0, output.cbData);
                return Encoding.UTF8.GetString(outBytes);
            }
            finally
            {
                if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
            }
        }
        catch { return null; }
        finally { Marshal.FreeHGlobal(handle); }
    }
}

/// <summary>AI 配置（字段与安卓 AiConfig / 网页 state.aiConfig 同名，camelCase）。</summary>
public class AiConfig
{
    [JsonPropertyName("baseUrl")] public string BaseUrl { get; set; } = "";
    [JsonPropertyName("apiKey")] public string ApiKey { get; set; } = "";
    /// <summary>模型名必填；不设默认值，避免误导用户以为默认模型可用（与双端同策略）。</summary>
    [JsonPropertyName("model")] public string Model { get; set; } = "";

    public bool IsReady => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>单条对话消息（OpenAI 兼容格式）。</summary>
public class ChatMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}

/// <summary>chat/completions 请求体。</summary>
public class ChatRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = new();
    [JsonPropertyName("stream")] public bool Stream { get; set; }
}

public class ChatChoice
{
    [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    [JsonPropertyName("index")] public int Index { get; set; }
}

public class ChatError
{
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
}

/// <summary>chat/completions 响应体。</summary>
public class ChatResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("choices")] public List<ChatChoice> Choices { get; set; } = new();
    [JsonPropertyName("error")] public ChatError? Error { get; set; }
}

/// <summary>
/// AI 助手服务：配置读写（Key 走 DPAPI 加密）+ OpenAI 兼容 chat/completions 请求
/// + 用户数据上下文摘要。契约与网页 ai.js / 安卓 AiRepository 完全一致。
/// </summary>
public static class AiService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static HttpClient? _http;

    private static HttpClient Http => _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

    /// <summary>读取配置（API Key 解密后返回明文，仅存在于内存）。</summary>
    public static AiConfig Load()
    {
        var s = SettingsStore.Load();
        return new AiConfig
        {
            BaseUrl = s.AiBaseUrl ?? "",
            Model = s.AiModel ?? "",
            ApiKey = SecretProtector.Unprotect(s.AiKeyEnc) ?? ""
        };
    }

    /// <summary>保存配置：地址/模型明文存、Key 用 DPAPI 加密后存。</summary>
    public static void Save(AiConfig cfg)
    {
        var s = SettingsStore.Load();
        s.AiBaseUrl = (cfg.BaseUrl ?? "").Trim();
        s.AiModel = (cfg.Model ?? "").Trim();
        s.AiKeyEnc = SecretProtector.Protect((cfg.ApiKey ?? "").Trim());
        s.Save();
    }

    /// <summary>
    /// 发送一轮对话。system 上下文由调用方决定是否前置注入（与网页端一致：每轮只发一份）。
    /// </summary>
    public static async Task<string> ChatAsync(AiConfig cfg, List<ChatMessage> messages,
        CancellationToken ct = default)
    {
        if (!cfg.IsReady) throw new InvalidOperationException("请先配置 API 地址和密钥");
        if (string.IsNullOrWhiteSpace(cfg.Model)) throw new InvalidOperationException("请先填写模型名称");

        var baseUrl = (cfg.BaseUrl ?? "").Trim().TrimEnd('/');
        var req = new ChatRequest { Model = cfg.Model, Messages = messages, Stream = false };
        var body = new StringContent(JsonSerializer.Serialize(req, JsonOpts), Encoding.UTF8, "application/json");

        using var msg = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/chat/completions");
        msg.Content = body;
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);

        using var resp = await Http.SendAsync(msg, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("HTTP " + (int)resp.StatusCode + "：" + Trim(text));
        }

        try
        {
            var data = JsonSerializer.Deserialize<ChatResponse>(text, JsonOpts);
            var err = data?.Error;
            if (err is not null && !string.IsNullOrWhiteSpace(err.Message))
            {
                throw new InvalidOperationException(Trim(err.Message));
            }
            var content = data?.Choices?.FirstOrDefault()?.Message?.Content;
            return string.IsNullOrWhiteSpace(content) ? "（AI 没有返回内容）" : content;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("返回内容不是合法 JSON：" + Trim(text));
        }
    }

    private static string Trim(string? s)
    {
        s = (s ?? "").Trim();
        return s.Length <= 200 ? s : s[..200] + "…";
    }

    /// <summary>
    /// 用户真实数据摘要（对齐网页 buildAiContextSummary）：角色 / 任务 / 背包 / 成就 / 收藏 / 最近日志。
    /// 取数失败返回空串 —— 没有上下文照样能聊，不该阻断提问。
    /// </summary>
    public static string BuildContextSummary()
    {
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            var profile = db.Profile.AsNoTracking().FirstOrDefault();
            var tasks = db.Tasks.AsNoTracking().ToList();
            var items = db.Items.AsNoTracking().ToList();
            var achs = db.Achievements.AsNoTracking().Where(a => a.Unlocked).ToList();
            var colCount = db.Collections.AsNoTracking().Count();
            var memos = db.Memos.AsNoTracking()
                .OrderBy(m => m.CreatedAt)
                .TakeLast(8).ToList();

            var stats = LifeStats.Compute(profile?.BirthDate);
            var L = new List<string>
            {
                "你是「地球Online」这款人生记录应用内的助手。下面是这位用户的真实数据，请据此回答，不要编造。",
                "",
                "【角色】",
                "- 名称：" + (string.IsNullOrWhiteSpace(profile?.Name) ? "未填写" : profile!.Name)
            };
            if (!string.IsNullOrWhiteSpace(profile?.Signature)) L.Add("- 个性签名：" + profile!.Signature);
            L.Add("- 等级：Lv." + stats.Age + (stats.HasBirth
                ? "（生日 " + profile?.BirthDate + "）"
                : "（未设置生日，等级为 0）"));

            var done = tasks.Count(t => t.Status == "done");
            var pending = tasks.Where(t => t.Status != "done").ToList();
            L.Add("");
            L.Add($"【任务】共 {tasks.Count} 条：已完成 {done} 条，未完成 {pending.Count} 条");

            var todos = pending.Where(t => t.Category == "todo").ToList();
            if (todos.Count > 0)
            {
                L.Add("- 待办 To Do：" + string.Join("；", todos.Take(15)
                    .Select(t => t.Title + (string.IsNullOrWhiteSpace(t.DueDate) ? "" : "（截止 " + t.DueDate + "）"))));
            }
            var mainPending = pending.Where(t => t.Category != "todo").ToList();
            if (mainPending.Count > 0)
            {
                L.Add("- 进行中的任务：" + string.Join("；", mainPending.Take(15)
                    .Select(t => $"{t.Title}（{t.Progress}%）")));
            }

            L.Add("");
            L.Add($"【背包】共 {items.Count} 件");
            if (items.Count > 0)
            {
                L.Add("- " + string.Join("、", items.Take(12).Select(i => i.Name)));
            }

            L.Add("");
            L.Add($"【成就】已解锁 {achs.Count} 条");
            if (achs.Count > 0)
            {
                L.Add("- " + string.Join("、", achs.Take(12).Select(a => a.Title)));
            }

            L.Add("");
            L.Add($"【收藏】共 {colCount} 条");

            if (memos.Count > 0)
            {
                L.Add("");
                L.Add("【最近的世界日志】");
                foreach (var m in memos)
                {
                    L.Add("- " + (m.CreatedAt.Length >= 10 ? m.CreatedAt[..10] : m.CreatedAt)
                          + "：" + (m.Text.Length > 80 ? m.Text[..80] : m.Text));
                }
            }

            return string.Join("\n", L);
        }
        catch
        {
            return "";
        }
    }
}
