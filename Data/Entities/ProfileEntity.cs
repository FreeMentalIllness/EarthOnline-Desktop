using System.Text.Json.Serialization;

namespace EarthOnline.Desktop.Data.Entities;

/// <summary>
/// 全局个人资料（对应安卓 ProfileEntity / 网页 state.profile + 顶级 birthDate）。
/// 全应用仅一行（Id 固定为 1），缺失即视为未初始化。
/// 列名与安卓 Room 一致；JSON 字段名与安卓 kotlinx camelCase 逐字一致（见 [JsonPropertyName]）。
/// </summary>
public class ProfileEntity
{
    [JsonPropertyName("id")] public int Id { get; set; } = 1;
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("avatarKey")] public string AvatarKey { get; set; } = "";
    [JsonPropertyName("avatarPath")] public string? AvatarPath { get; set; }

    /// <summary>头像原图字节（Base64）。安卓导出内联、导入落成文件；只出不进。</summary>
    [JsonPropertyName("avatarData")] public byte[]? AvatarData { get; set; }

    [JsonPropertyName("gender")] public string Gender { get; set; } = "";
    [JsonPropertyName("country")] public string Country { get; set; } = "";
    [JsonPropertyName("province")] public string Province { get; set; } = "";
    [JsonPropertyName("signature")] public string Signature { get; set; } = "";
    [JsonPropertyName("birthDate")] public string BirthDate { get; set; } = "";
    [JsonPropertyName("customFieldsJson")] public string CustomFieldsJson { get; set; } = "";
}
