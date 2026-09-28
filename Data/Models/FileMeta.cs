using System.Text.Json.Serialization;

namespace EarthOnline.Desktop.Data.Models;

/// <summary>
/// 收藏文件附件元信息（对应安卓 CollectionFileMeta，JSON 内嵌于 CollectionEntity.FileMetaJson）。
/// 二进制不入库：桌面复制到 AppPaths.FilesDir，路径存 FileUri。
/// </summary>
public class FileMeta
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("mime")] public string Mime { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}
