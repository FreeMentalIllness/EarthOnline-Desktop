using System.IO;
using System.Text.Json;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Services;

/// <summary>
/// 全量备份：把各表导出为单个 JSON，或把 JSON 导回。
///
/// 三端互通约定（关键）：
///  - 导出的**形状与安卓 BackupRepository.Payload 完全一致**（顶层裸字段 version/exportedAt/profile/tasks/...），
///    安卓 kotlinx 与网页端 applyBackupPayload 都能直接吃；不写网页端的 state 信封（桌面端不需要，
///    且安卓解析器靠 ignoreUnknownKeys 忽略多余键，裸格式最通用）。
///  - 导入则**两种格式都吃**：顶层裸字段优先；没有则回落到网页端的 state 信封并做归一化
///    （state.birthDate → profile.birthDate、locations.tags[] → tagsJson、profile.customFields[] → customFieldsJson）。
///  - 导入语义为 REPLACE（按主键合并），不清空既有数据 —— 与安卓 importJson 一致。
/// </summary>
public static class BackupService
{
    /// <summary>
    /// camelCase 是硬要求：安卓 Room 实体与网页 state 全部用 camelCase（version / exportedAt / tasks…），
    /// 桌面端必须逐字一致，否则跨端读不到字段。反序列化大小写不敏感，两种都能吃。
    /// </summary>
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// 备份负载 DTO（安卓 BackupRepository.Payload 的 C# 镜像）。
    /// 所有嵌套实体都已显式标注 [JsonPropertyName]，与安卓 kotlinx camelCase 逐字一致。
    /// </summary>
    public sealed class BackupPayload
    {
        public int Version { get; set; } = 1;
        public string ExportedAt { get; set; } = "";
        public ProfileEntity? Profile { get; set; }
        public List<TaskEntity> Tasks { get; set; } = new();
        public List<MemoEntity> Memos { get; set; } = new();
        public List<ItemEntity> Items { get; set; } = new();
        public List<AchievementEntity> Achievements { get; set; } = new();
        public List<CollectionEntity> Collections { get; set; } = new();
        public List<LocationEntity> Locations { get; set; } = new();
        public List<ActivityEntity> Activities { get; set; } = new();
    }

    // ==================== 导出 ====================

    /// <param name="dbPath">数据库路径，默认 AppPaths.DbFile（测试可注入临时库）。</param>
    public static string ExportJson(string? dbPath = null)
    {
        using var db = new AppDbContext(dbPath ?? AppPaths.DbFile);
        var payload = new BackupPayload
        {
            Version = 1,
            ExportedAt = DateTime.Now.ToString("o"),
            Profile = WithAvatarBytes(db.Profile.FirstOrDefault()),
            Tasks = db.Tasks.AsNoTracking().ToList(),
            Memos = db.Memos.AsNoTracking().ToList(),
            Items = db.Items.AsNoTracking().ToList(),
            Achievements = db.Achievements.AsNoTracking().ToList(),
            Collections = db.Collections.AsNoTracking().ToList(),
            Locations = db.Locations.AsNoTracking().ToList(),
            Activities = db.Activities.AsNoTracking().ToList()
        };
        return JsonSerializer.Serialize(payload, Opts);
    }

    /// <summary>导出：把头像原图内联成字节（换设备后头像不丢，对应安卓 withAvatarBytes）。</summary>
    private static ProfileEntity? WithAvatarBytes(ProfileEntity? p)
    {
        if (p is null) return null;
        if (p.AvatarData is { Length: > 0 }) return p;
        var path = p.AvatarPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return p;
        try
        {
            p.AvatarData = File.ReadAllBytes(path);
        }
        catch
        {
            // 读不到就按原样导出，绝不因头像失败中断备份
        }
        return p;
    }

    /// <summary>从备份 JSON 里读出 exportedAt 的毫秒值（解析失败返回 0，同步比对用）。</summary>
    public static long ReadExportedAt(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exportedAt", out var v) && v.ValueKind == JsonValueKind.String)
            {
                return SyncService.ParseMillis(v.GetString());
            }
        }
        catch
        {
            // 不是有效 JSON / 没有该字段 → 视为 0
        }
        return 0L;
    }

    // ==================== 导入 ====================

    /// <param name="dbPath">数据库路径，默认 AppPaths.DbFile（测试可注入临时库）。</param>
    public static int ImportJson(string text, string? dbPath = null)
    {
        var payload = Parse(text);
        int count = 0;

        using var db = new AppDbContext(dbPath ?? AppPaths.DbFile);

        if (payload.Profile is not null)
        {
            var p = MaterializeAvatar(payload.Profile);
            var exist = db.Profile.Find(p.Id);
            if (exist is null) db.Profile.Add(p);
            else { p.Id = 1; db.Entry(exist).CurrentValues.SetValues(p); }
            count++;
        }

        count += Upsert(db, db.Tasks, payload.Tasks, t => t.Id);
        count += Upsert(db, db.Memos, payload.Memos, m => m.Id);
        count += Upsert(db, db.Items, payload.Items, i => i.Id);
        count += Upsert(db, db.Achievements, payload.Achievements, a => a.Id);
        count += Upsert(db, db.Collections, payload.Collections, c => c.Id);
        count += Upsert(db, db.Locations, payload.Locations, l => l.Id);
        count += Upsert(db, db.Activities, payload.Activities, a => a.Id);

        db.SaveChanges();
        return count;
    }

    private static int Upsert<T>(AppDbContext db, DbSet<T> set, List<T> incoming, Func<T, string> keyOf)
        where T : class
    {
        int n = 0;
        foreach (var item in incoming)
        {
            if (keyOf(item) is not { Length: > 0 }) continue;
            var exist = set.Find(keyOf(item));
            if (exist is null) set.Add(item);
            else db.Entry(exist).CurrentValues.SetValues(item);
            n++;
        }
        return n;
    }

    /// <summary>导入：把内联的头像字节落成文件，路径写回资料行（对应安卓 materializeAvatar）。</summary>
    private static ProfileEntity MaterializeAvatar(ProfileEntity p)
    {
        if (p.AvatarData is not { Length: > 0 }) return p;
        try
        {
            AppPaths.EnsureDirectories();
            var file = Path.Combine(AppPaths.AvatarDir, "avatar_" + DateTime.Now.Ticks + ".jpg");
            File.WriteAllBytes(file, p.AvatarData);
            p.AvatarPath = file;
            p.AvatarData = null;
        }
        catch
        {
            // 落盘失败不影响其它数据
        }
        return p;
    }

    // ==================== 解析（兼容裸格式 / 网页信封格式）====================

    private static BackupPayload Parse(string text)
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        // 顶层裸字段（安卓 / 桌面端写的）
        var p = new BackupPayload();
        if (root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number)
            p.Version = v.GetInt32();
        if (root.TryGetProperty("exportedAt", out var ea) && ea.ValueKind == JsonValueKind.String)
            p.ExportedAt = ea.GetString() ?? "";

        p.Profile = ReadProfile(root);
        p.Tasks = ReadList<TaskEntity>(root, "tasks");
        p.Memos = ReadList<MemoEntity>(root, "memos");
        p.Items = ReadList<ItemEntity>(root, "items");
        p.Achievements = ReadList<AchievementEntity>(root, "achievements");
        p.Collections = ReadList<CollectionEntity>(root, "collections");
        p.Locations = ReadLocations(root, out _);
        p.Activities = ReadList<ActivityEntity>(root, "activities");

        // 回落：网页端的 state 信封（顶层没有 tasks 时才从这里读）
        if (root.TryGetProperty("state", out var st) && st.ValueKind == JsonValueKind.Object)
        {
            if (p.Tasks.Count == 0) p.Tasks = ReadList<TaskEntity>(st, "tasks");
            if (p.Memos.Count == 0) p.Memos = ReadList<MemoEntity>(st, "memos");
            if (p.Items.Count == 0) p.Items = ReadList<ItemEntity>(st, "items");
            if (p.Achievements.Count == 0) p.Achievements = ReadList<AchievementEntity>(st, "achievements");
            if (p.Collections.Count == 0) p.Collections = ReadList<CollectionEntity>(st, "collections");
            if (p.Activities.Count == 0) p.Activities = ReadList<ActivityEntity>(st, "activities");
            if (p.Locations.Count == 0) p.Locations = ReadLocations(st, out _);
            p.Profile ??= ReadProfile(st);
        }

        return p;
    }

    /// <summary>
    /// 仅解析不落库：供「数据兼容性自检」与任何只想读懂备份结构的场景使用。
    /// 解析失败（JSON 不合法 / 不是备份结构）返回 null。
    /// </summary>
    public static BackupPayload? TryParsePayload(string text)
    {
        try
        {
            return Parse(text);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<T> ReadList<T>(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return new List<T>();
        try
        {
            return JsonSerializer.Deserialize<List<T>>(arr.GetRawText(), Opts) ?? new List<T>();
        }
        catch
        {
            return new List<T>();
        }
    }

    private static ProfileEntity? ReadProfile(JsonElement obj)
    {
        // 1) 顶层 profile 对象
        if (obj.TryGetProperty("profile", out var pr) && pr.ValueKind == JsonValueKind.Object)
        {
            var p = JsonSerializer.Deserialize<ProfileEntity>(pr.GetRawText(), Opts);
            if (p is not null)
            {
                p.Id = 1;
                // 网页端 state.birthDate 是顶级字段，profile 里可能没有
                if (string.IsNullOrEmpty(p.BirthDate) &&
                    obj.TryGetProperty("birthDate", out var bd) && bd.ValueKind == JsonValueKind.String)
                {
                    p.BirthDate = bd.GetString() ?? "";
                }
                NormalizeCustomFields(pr, p);
                return p;
            }
        }

        // 2) 没有 profile 对象，但可能有 name/birthDate 等散字段（网页 state 形态）
        if (obj.TryGetProperty("name", out _) || obj.TryGetProperty("birthDate", out _))
        {
            var p = JsonSerializer.Deserialize<ProfileEntity>(obj.GetRawText(), Opts) ?? new ProfileEntity();
            p.Id = 1;
            NormalizeCustomFields(obj, p);
            return p;
        }

        return null;
    }

    /// <summary>网页端 profile.customFields 是数组，安卓 / 桌面是 JSON 字符串。</summary>
    private static void NormalizeCustomFields(JsonElement prof, ProfileEntity p)
    {
        if (!string.IsNullOrEmpty(p.CustomFieldsJson)) return;
        if (prof.TryGetProperty("customFields", out var cf) && cf.ValueKind == JsonValueKind.Array)
        {
            p.CustomFieldsJson = cf.GetRawText();
        }
    }

    /// <summary>网页端 locations[].tags 是数组，安卓 / 桌面是 tagsJson 字符串。</summary>
    private static List<LocationEntity> ReadLocations(JsonElement obj, out int normalized)
    {
        normalized = 0;
        if (!obj.TryGetProperty("locations", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return new List<LocationEntity>();

        var list = new List<LocationEntity>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            try
            {
                var l = JsonSerializer.Deserialize<LocationEntity>(el.GetRawText(), Opts);
                if (l is null) continue;
                if (string.IsNullOrEmpty(l.TagsJson) &&
                    el.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
                {
                    l.TagsJson = tags.GetRawText();
                    normalized++;
                }
                list.Add(l);
            }
            catch
            {
                // 单条坏数据跳过
            }
        }
        return list;
    }
}
