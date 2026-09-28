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

    /// <summary>读自定义字段（坏 JSON 忽略）。</summary>
    public static List<CustomField> ReadCustomFields(ProfileEntity? p)
    {
        if (string.IsNullOrWhiteSpace(p?.CustomFieldsJson)) return new List<CustomField>();
        try
        {
            return JsonSerializer.Deserialize<List<CustomField>>(
                       p.CustomFieldsJson,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new List<CustomField>();
        }
        catch
        {
            return new List<CustomField>();
        }
    }

    public static string WriteCustomFields(List<CustomField> list) =>
        JsonSerializer.Serialize(list);
}
