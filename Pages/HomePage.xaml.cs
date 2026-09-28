using System.Windows.Controls;
using EarthOnline.Desktop.Data;

namespace EarthOnline.Desktop.Pages;

public partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadSummary();
    }

    /// <summary>从 SQLite 读取概览数据（同时验证 EF Core 链路可用）。</summary>
    private void LoadSummary()
    {
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);

            var profile = db.Profile.FirstOrDefault();
            string name = string.IsNullOrWhiteSpace(profile?.Name) ? "未设置昵称" : profile!.Name;
            string birth = string.IsNullOrWhiteSpace(profile?.BirthDate) ? "未设置生日" : profile!.BirthDate;

            SummaryText.Text = $"{name} · 生日 {birth}";
            TaskCountText.Text = db.Tasks.Count().ToString();
            ItemCountText.Text = db.Items.Count().ToString();
            MemoCountText.Text = db.Memos.Count().ToString();
        }
        catch (Exception ex)
        {
            SummaryText.Text = "数据加载失败：" + ex.Message;
        }
    }
}
