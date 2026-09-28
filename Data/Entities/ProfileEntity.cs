namespace EarthOnline.Desktop.Data.Entities;

/// <summary>
/// 全局个人资料（对应安卓 ProfileEntity / 网页 state.profile + 顶级 birthDate）。
/// 全应用仅一行（Id 固定为 1），缺失即视为未初始化。
/// 列名与安卓 Room 完全一致，便于双端共用同一份 SQLite / 备份 JSON。
/// </summary>
public class ProfileEntity
{
    public int Id { get; set; } = 1;

    public string Name { get; set; } = "";

    /// <summary>
    /// 预设头像 key："" / "default" / "earth" / "rocket" / "game" / "cat" / "leaf" / "music" / "star"
    /// </summary>
    public string AvatarKey { get; set; } = "";

    /// <summary>
    /// 上传头像的原图文件路径。空 = 未上传（回落到 AvatarKey 的预设头像）。
    /// </summary>
    public string? AvatarPath { get; set; }

    /// <summary>【兼容字段】v1.0.0 之前头像直接存字节；只出不进，备份导出时按需回填。</summary>
    public byte[]? AvatarData { get; set; }

    /// <summary>"" / "male" / "female" / "walmart"</summary>
    public string Gender { get; set; } = "";

    public string Country { get; set; } = "";
    public string Province { get; set; } = "";
    public string Signature { get; set; } = "";

    /// <summary>YYYY-MM-DD，空串 = 未设置（等级 / 年龄由它计算）</summary>
    public string BirthDate { get; set; } = "";

    /// <summary>List&lt;CustomField&gt; 的 JSON</summary>
    public string CustomFieldsJson { get; set; } = "";
}
