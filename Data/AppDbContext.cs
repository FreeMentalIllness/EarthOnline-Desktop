using System.Threading;
using System.Threading.Tasks;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Services;
using Microsoft.EntityFrameworkCore;

namespace EarthOnline.Desktop.Data;

/// <summary>
/// 主数据库上下文（对应安卓 AppDatabase version=4 / 网页单一存档键 earth_data）。
///
/// 兼容性约定：
/// 1. 表名、列名与安卓 Room 实体**逐字一致**（唯一例外：TaskEntity.Order 在两端都存为列 sort_order）。
/// 2. Profile 主键固定为 1 且不自增（安卓同约定）。
/// 3. 所有时间字段存字符串（YYYY-MM-DD 或 ISO），与双端一致，避免时区 / 精度差异。
/// </summary>
public class AppDbContext : DbContext
{
    public DbSet<ProfileEntity> Profile => Set<ProfileEntity>();
    public DbSet<TaskEntity> Tasks => Set<TaskEntity>();
    public DbSet<MemoEntity> Memos => Set<MemoEntity>();
    public DbSet<ItemEntity> Items => Set<ItemEntity>();
    public DbSet<AchievementEntity> Achievements => Set<AchievementEntity>();
    public DbSet<CollectionEntity> Collections => Set<CollectionEntity>();
    public DbSet<LocationEntity> Locations => Set<LocationEntity>();
    public DbSet<ActivityEntity> Activities => Set<ActivityEntity>();
    public DbSet<BagCategoryEntity> BagCategories => Set<BagCategoryEntity>();

    /// <summary>数据库文件绝对路径（由 AppPaths 决定）。</summary>
    public string DbPath { get; }

    public AppDbContext(string dbPath)
    {
        DbPath = dbPath;
    }

    public AppDbContext(DbContextOptions<AppDbContext> options, string dbPath) : base(options)
    {
        DbPath = dbPath;
    }

    /// <summary>
    /// 统一写入出口：任何一次真实落库都触发一次「自动备份」防抖调度。
    /// 对应安卓 Room InvalidationTracker 的角色 —— 所有写入都经过 DbContext，
    /// 不需要在每个页面手动插桩，将来新增写入路径也不会漏。
    /// 备份服务本身只写文件、不写库，因此不会自激循环。
    /// </summary>
    public override int SaveChanges()
    {
        var n = base.SaveChanges();
        if (n > 0) AutoBackupService.Signal();
        return n;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var n = await base.SaveChangesAsync(cancellationToken);
        if (n > 0) AutoBackupService.Signal();
        return n;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
        {
            options.UseSqlite($"Data Source={DbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        // ---------- profile ----------
        model.Entity<ProfileEntity>(e =>
        {
            e.ToTable("profile");
            e.HasKey(p => p.Id);
            // 全应用仅一行，id 固定为 1，禁止自增
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Name).HasDefaultValue("");
            e.Property(p => p.AvatarKey).HasDefaultValue("");
            e.Property(p => p.BirthDate).HasDefaultValue("");
            e.Property(p => p.CustomFieldsJson).HasDefaultValue("");
        });

        // ---------- tasks ----------
        model.Entity<TaskEntity>(e =>
        {
            e.ToTable("tasks");
            e.HasKey(t => t.Id);
            // 与安卓 @ColumnInfo(name = "sort_order") 对齐
            e.Property(t => t.Order).HasColumnName("sort_order");
            e.Property(t => t.Category).HasDefaultValue("todo");
            e.Property(t => t.Status).HasDefaultValue("planning");
            e.HasIndex(t => t.ParentId);
            e.HasIndex(t => t.Status);
        });

        // ---------- memos ----------
        model.Entity<MemoEntity>(e =>
        {
            e.ToTable("memos");
            e.HasKey(m => m.Id);
            e.Property(m => m.Type).HasDefaultValue("note");
            e.HasIndex(m => m.CreatedAt);
        });

        // ---------- items ----------
        model.Entity<ItemEntity>(e =>
        {
            e.ToTable("items");
            e.HasKey(i => i.Id);
            e.Property(i => i.Type).HasDefaultValue("physical");
            e.HasIndex(i => i.Category);
        });

        // ---------- achievements ----------
        model.Entity<AchievementEntity>(e =>
        {
            e.ToTable("achievements");
            e.HasKey(a => a.Id);
            e.Property(a => a.Type).HasDefaultValue("manual");
            e.Property(a => a.Unlocked).HasDefaultValue(false);
            e.HasIndex(a => a.Category);
        });

        // ---------- collections ----------
        model.Entity<CollectionEntity>(e =>
        {
            e.ToTable("collections");
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.Category);
        });

        // ---------- locations ----------
        model.Entity<LocationEntity>(e =>
        {
            e.ToTable("locations");
            e.HasKey(l => l.Id);
            e.HasIndex(l => l.Date);
        });

        // ---------- activities ----------
        model.Entity<ActivityEntity>(e =>
        {
            e.ToTable("activities");
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.Time);
        });

        // ---------- bag_categories ----------
        model.Entity<BagCategoryEntity>(e =>
        {
            e.ToTable("bag_categories");
            e.HasKey(b => b.Id);
            // 对应安卓 Index(value = ["scope", "sortOrder"])
            e.HasIndex(b => new { b.Scope, b.SortOrder });
        });
    }

    /// <summary>
    /// 首次使用时建库，并写入 Profile 行（不存在时），保证「缺失即未初始化」语义与安卓一致。
    /// 名称里的 Ready = 建库 + 补齐种子数据；实现必须是 Database.Migrate()（红线），
    /// 绝不能是 EF 的 Database.EnsureCreated() —— 后者不写 __EFMigrationsHistory，后续升级无迁移可依。
    /// </summary>
    public void EnsureReady()
    {
        StampBaselineIfNeeded();   // 老格式库（有表无迁移历史）补基线，避免升级时迁移重放
        Database.Migrate();
        if (!Profile.Any())
        {
            Profile.Add(new ProfileEntity { Id = 1 });
            SaveChanges();
        }
    }

    /// <summary>
    /// 兼容保护：若库里已经有以下任意一张业务表，却没有 __EFMigrationsHistory（历史版本或外部工具建库），
    /// 直接跑 Migrate 会因「表已存在」失败。这里把 InitialCreate 标记为已应用，让迁移链从当前版本继续。
    /// </summary>
    private void StampBaselineIfNeeded()
    {
        try
        {
            using var conn = Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            var tables = new List<string>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read()) tables.Add(reader.GetString(0));
            }
            if (tables.Contains("__EFMigrationsHistory")) return;
            if (!tables.Contains("profile") && !tables.Contains("tasks")) return;   // 空库：正常走迁移

            using var ins = conn.CreateCommand();
            ins.CommandText =
                "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (" +
                "MigrationId TEXT NOT NULL PRIMARY KEY, ProductVersion TEXT NOT NULL);" +
                "INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES " +
                "('20260927161513_InitialCreate', '8.0.11');";
            ins.ExecuteNonQuery();
        }
        catch
        {
            // 打基线失败不拦启动：正常库根本走不到这里，异常时交给 Migrate 自己报错
        }
    }
}
