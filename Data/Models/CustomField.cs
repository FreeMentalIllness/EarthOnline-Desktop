using System.Text.Json.Serialization;

namespace EarthOnline.Desktop.Data.Models;

/// <summary>
/// 资料页自定义字段（对应安卓 data/model/CustomField.kt）。
/// 以 JSON 数组形式存放在 ProfileEntity.CustomFieldsJson。
/// </summary>
public class CustomField
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}
