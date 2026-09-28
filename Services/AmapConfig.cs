using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using EarthOnline.Desktop.Data;

namespace EarthOnline.Desktop.Services;

/// <summary>内置回退 Key 的载体（可选文件，已 gitignore，永不入库）。</summary>
public class AmapDefaultFile
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("securityJsCode")] public string SecurityJsCode { get; set; } = "";
}

/// <summary>
/// 高德地图 Key 配置。
/// 优先级：用户自己填的 Key（DPAPI 加密存本机）→ 内置回退（Assets/amap_default.json，
/// 该**文件默认不存在、已在 .gitignore 中**，任何人可自行放入自己的 Key 覆盖）→ 无 Key（地图降级为列表）。
/// 【红线】源码与资源里不允许出现任何硬编码明文 Key。
/// </summary>
public static class AmapConfig
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>当前生效的 Key / 安全密钥，以及是否来自用户自定义。</summary>
    public static (string Key, string Sec, bool IsCustom) Load()
    {
        var s = SettingsStore.Load();
        var key = SecretProtector.Unprotect(s.AmapKeyEnc);
        if (!string.IsNullOrWhiteSpace(key))
        {
            return (key, SecretProtector.Unprotect(s.AmapSecEnc) ?? "", true);
        }
        var def = LoadDefault();
        return (def.Key, def.SecurityJsCode, false);
    }

    /// <summary>保存用户自己的 Key（空串 = 清空，回落到内置 / 无 Key）。</summary>
    public static void Save(string key, string sec)
    {
        var s = SettingsStore.Load();
        s.AmapKeyEnc = SecretProtector.Protect((key ?? "").Trim());
        s.AmapSecEnc = SecretProtector.Protect((sec ?? "").Trim());
        s.Save();
    }

    /// <summary>是否存在可用的内置回退 Key。</summary>
    public static bool HasDefault => !string.IsNullOrWhiteSpace(LoadDefault().Key);

    private static AmapDefaultFile LoadDefault()
    {
        try
        {
            var sri = Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/amap_default.json"));
            if (sri?.Stream is null) return new AmapDefaultFile();
            using var r = new StreamReader(sri.Stream);
            return JsonSerializer.Deserialize<AmapDefaultFile>(r.ReadToEnd(), Opts)
                   ?? new AmapDefaultFile();
        }
        catch
        {
            // 文件不存在 / 格式错：视为无回退（地图降级为列表，不影响其他功能）
            return new AmapDefaultFile();
        }
    }
}
