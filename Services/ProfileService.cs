using System.Linq;
using System.Text.Json;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Data.Models;

namespace EarthOnline.Desktop.Services;

/// <summary>
/// 资料读写 + 性别 key↔文案 映射。
/// 性别在数据层存 key（与安卓/网页 GENDER_KEYS 一致）：'' | male | female | walmart | helicopter | potato。
/// </summary>
public static class ProfileService
{
    /// <summary>性别选项（与网页 profile.js GENDER_OPTIONS 完全一致）。</summary>
    public static readonly (string Key, string Label)[] GenderOptions =
    {
        ("", "保密"),
        ("male", "男"),
        ("female", "女"),
        ("walmart", "🛍️ 沃尔玛购物袋"),
        ("helicopter", "🚁 直升机"),
        ("potato", "🥔 土豆"),
    };

    /// <summary>性别 key → 显示文案（未知 key 原样返回，避免吞数据）。</summary>
    public static string GenderLabel(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        foreach (var (k, label) in GenderOptions)
        {
            if (k == key) return label;
        }
        return key;
    }

    /// <summary>头像 emoji 预设（avatarKey 与安卓一致）。</summary>
    public static readonly (string Key, string Emoji)[] AvatarPresets =
    {
        ("", "🌍"),
        ("rocket", "🚀"),
        ("game", "🎮"),
        ("cat", "🐱"),
        ("leaf", "🍃"),
        ("music", "🎵"),
        ("star", "⭐"),
    };

    public static string AvatarEmoji(string? avatarKey)
    {
        foreach (var (k, emoji) in AvatarPresets)
        {
            if (k == avatarKey) return emoji;
        }
        return "🌍";
    }

    public static ProfileEntity? Load(AppDbContext db) => db.Profile.FirstOrDefault();

    /// <summary>
    /// 自定义字段共享序列化选项：严格 camelCase（对齐 Web/Android 契约）。
    /// CustomField 已显式标注 [JsonPropertyName("id"/"label"/"value")]，因此反序列化只接受 camelCase 键。
    /// </summary>
    private static readonly JsonSerializerOptions CustomFieldOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false
    };

    /// <summary>读自定义字段（坏 JSON 忽略）。</summary>
    public static List<CustomField> ReadCustomFields(ProfileEntity? p)
    {
        if (string.IsNullOrWhiteSpace(p?.CustomFieldsJson)) return new List<CustomField>();
        try
        {
            // 默认按严格 camelCase 契约解析（与 Web/Android 完全一致）。
            var strict = JsonSerializer.Deserialize<List<CustomField>>(p.CustomFieldsJson, CustomFieldOpts);
            if (strict is { Count: > 0 } && strict.Any(f => !string.IsNullOrEmpty(f.Id)))
                return strict;
            // 兜底：兼容旧版 Windows 以 PascalCase 落库的自定义字段，避免升级后字段丢失。
            var legacy = JsonSerializer.Deserialize<List<CustomField>>(
                p.CustomFieldsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return legacy ?? new List<CustomField>();
        }
        catch
        {
            return new List<CustomField>();
        }
    }

    /// <summary>写自定义字段：统一以 camelCase 落库，与 Web/Android 契约对齐。</summary>
    public static string WriteCustomFields(List<CustomField> list) =>
        JsonSerializer.Serialize(list, CustomFieldOpts);
}
