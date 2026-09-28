using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EarthOnline.Desktop.Data;

/// <summary>
/// 仅供 `dotnet ef migrations` 等设计时命令使用（运行时不调用）。
/// 没有它，EF 无法实例化带 dbPath 参数的 AppDbContext。
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        AppPaths.EnsureDirectories();
        return new AppDbContext(AppPaths.DbFile);
    }
}
