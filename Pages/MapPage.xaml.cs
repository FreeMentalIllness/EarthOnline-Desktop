using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EarthOnline.Desktop.Data;
using EarthOnline.Desktop.Data.Entities;
using EarthOnline.Desktop.Dialogs;
using EarthOnline.Desktop.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Web.WebView2.Core;

namespace EarthOnline.Desktop.Pages;

/// <summary>足迹列表行（显示用；tags 从 TagsJson 还原）。</summary>
public class LocationRow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public double Lat { get; init; }
    public double Lng { get; init; }
    public string Date { get; init; } = "";
    public string Note { get; init; } = "";
    public List<string> Tags { get; init; } = new();
    public string CoordText => $"{Lat:F4}, {Lng:F4} · {Date}";
}

/// <summary>
/// 足迹地图页：WebView2 嵌入 Assets/map.html（高德地图，与网页端同一 Key）。
/// 地图不可用时（无网络 / WebView2 运行时缺失）自动降级为纯列表，功能不丢失。
/// </summary>
public partial class MapPage : Page
{
    private List<LocationEntity> _locs = new();
    /// <summary>地图上最近一次点击的坐标，供「+ 添加足迹」预填。</summary>
    private double? _pickLat, _pickLng;
    private bool _mapReady;
    private bool _loaded;
    /// <summary>WebView2 事件是否已挂过（重试加载时避免重复订阅）。</summary>
    private bool _handlersHooked;
    /// <summary>当前存活的地图页实例（页面被 MainWindow 缓存，设置页改地图偏好时靠它即时推送）。</summary>
    private static MapPage? _live;

    public MapPage()
    {
        InitializeComponent();
        _live = this;
        // 夜间样式跟随深色主题：主题亮↔暗切换时按最新偏好重推样式
        ThemeService.ThemeChanged += OnThemeChangedForPrefs;
        Loaded += async (_, _) =>
        {
            _loaded = true;
            _live = this;
            LoadList();
            await InitMapAsync();
        };
    }

    private void OnThemeChangedForPrefs(bool dark)
    {
        try
        {
            var s = SettingsStore.Load();
            var style = EffectiveStyle(s, dark);
            ApplyPrefsLive(style, s.MapZoom, resetView: false);
        }
        catch { /* 偏好读取失败则忽略，下次打开地图页按新主题初始化 */ }
    }

    /// <summary>实际生效的样式：深色主题且开启联动时强制夜间，否则用选定样式。</summary>
    private static string EffectiveStyle(SettingsStore s, bool dark)
        => dark && s.MapStyleFollowDark ? "dark" : NormalizeMapStyle(s.MapStyle);

    // ==================== 数据 ====================

    private void LoadList()
    {
        if (!_loaded || LocList is null) return;
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            _locs = db.Locations.AsNoTracking()
                .OrderByDescending(l => l.Date)
                .ToList();

            var rows = _locs.Select(l => new LocationRow
            {
                Id = l.Id,
                Name = l.Name,
                Lat = l.Lat,
                Lng = l.Lng,
                Date = l.Date,
                Note = string.IsNullOrWhiteSpace(l.Note) ? "" : l.Note!,
                Tags = ParseTags(l.TagsJson)
            }).ToList();

            LocList.ItemsSource = rows;
            if (EmptyText is not null)
                EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("加载足迹失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static List<string> ParseTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch { return new List<string>(); }
    }

    private static string? SerializeTags(List<string> tags)
        => tags.Count == 0 ? null : JsonSerializer.Serialize(tags);

    // ==================== 地图（WebView2） ====================

    /// <summary>初始化 WebView2 并加载本地地图页；任何失败都降级为纯列表。</summary>
    private async Task InitMapAsync()
    {
        if (MapView is null) return;
        try
        {
            await MapView.EnsureCoreWebView2Async(null);
            // 事件只挂一次（重试会再次进入本方法）
            if (!_handlersHooked)
            {
                MapView.CoreWebView2.WebMessageReceived += OnWebMessage;
                MapView.NavigationCompleted += async (_, e) =>
                {
                    if (e.IsSuccess) await PushLocationsAsync();
                    else ShowFallback(true, "地图页面加载失败（请检查网络或 WebView2 运行时）。可切换列表视图管理足迹。");
                };
                _handlersHooked = true;
            }


            // Key 注入必须在导航之前：AddScriptToExecuteOnDocumentCreated 对后续文档生效。
            // 无 Key（未配置且无内置回退）→ 直接降级，不发起任何外部请求。
            var cfg = AmapConfig.Load();
            if (string.IsNullOrWhiteSpace(cfg.Key))
            {
                ShowFallback(true,
                    "未检测到可用的地图服务，已切换为列表视图。" +
                    "右侧足迹列表的新增 / 编辑 / 删除完全可用，不依赖地图。");
                SetHint("未检测到地图服务，使用列表视图");
                return;
            }

            // 地图偏好（样式 / 默认缩放）随 Key 一起在文档创建前注入，地图页 init 时直接采用。
            var prefs = SettingsStore.Load();
            await MapView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.__AMAP_KEY__ = " + JsonSerializer.Serialize(cfg.Key) + ";" +
                "window.__AMAP_SEC__ = " + JsonSerializer.Serialize(cfg.Sec) + ";" +
                "window.__AMAP_STYLE__ = " + JsonSerializer.Serialize(EffectiveStyle(prefs, ThemeService.Dark)) + ";" +
                "window.__AMAP_ZOOM__ = " + NormalizeMapZoom(prefs.MapZoom).ToString(CultureInfo.InvariantCulture) + ";");

            var file = ExtractMapHtml();
            MapView.Source = new Uri(file);
            SetHint("地图加载中…");
            ShowFallback(false);
        }
        catch (Exception ex)
        {
            // WebView2 运行时缺失 / 初始化失败：不影响列表功能
            ShowFallback(true, "地图组件初始化失败：" + ex.Message);
            SetHint("地图不可用，仍可通过列表管理足迹");
        }
    }

    /// <summary>地图不可用 → 显示桌面端降级面板（明确引导「切换列表视图」）；可用 → 隐藏。</summary>
    private void ShowFallback(bool show, string? detail = null)
    {
        if (MapFallback is null) return;
        MapFallback.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show && !string.IsNullOrWhiteSpace(detail) && FallbackDetailText is not null)
        {
            FallbackDetailText.Text = detail;
        }
    }

    /// <summary>降级面板上的「重试」：重新释放并加载地图页。</summary>
    private async void RetryMap_Click(object sender, RoutedEventArgs e)
    {
        SetHint("正在重试加载地图…");
        ShowFallback(false);
        _mapReady = false;
        await InitMapAsync();
    }

    /// <summary>把内嵌的 map.html 释放到数据目录（每次启动覆盖，保证与程序版本一致）。</summary>
    private static string ExtractMapHtml()
    {
        var dir = AppPaths.RootDir;
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "map.html");
        var sri = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/map.html"));
        if (sri?.Stream is null) throw new InvalidOperationException("找不到内嵌的地图页面资源");
        using var fs = new FileStream(target, FileMode.Create, FileAccess.Write);
        sri.Stream.CopyTo(fs);
        return target;
    }

    // ==================== 地图偏好（与设置页共用同一份 SettingsStore） ====================

    /// <summary>地图样式关键字归一：normal / whitesmoke / dark（非法值回落 normal）。</summary>
    public static string NormalizeMapStyle(string? style)
    {
        var v = (style ?? "").Trim().ToLowerInvariant();
        return v is "normal" or "whitesmoke" or "dark" ? v : "normal";
    }

    /// <summary>默认缩放级别（钳在 3~17，未设置回落 4）。</summary>
    public static double NormalizeMapZoom(double zoom)
        => zoom >= 3 && zoom <= 17 ? Math.Round(zoom) : 4;

    /// <summary>
    /// 设置页改地图样式 / 缩放（或点「重置地图视图」）后即时推给已打开的地图页。
    /// 地图页实例由 MainWindow 缓存，因此通常都活着；实例不存在时静默忽略，
    /// 下次进入地图页会按最新偏好重新初始化。
    /// </summary>
    public static void ApplyPrefsLive(string style, double zoom, bool resetView)
    {
        var page = _live;
        if (page?.MapView?.CoreWebView2 is null) return;
        var inv = CultureInfo.InvariantCulture;
        try
        {
            _ = page.MapView.ExecuteScriptAsync(
                $"window.applyPrefs({JsonSerializer.Serialize(NormalizeMapStyle(style))}, " +
                $"{NormalizeMapZoom(zoom).ToString(inv)}, {(resetView ? "true" : "false")})");
        }
        catch { /* 地图页未就绪则忽略，下次打开按新偏好初始化 */ }
    }

    /// <summary>把足迹推送给地图页重画标记。</summary>
    private async Task PushLocationsAsync()
    {
        if (MapView?.CoreWebView2 is null || !_mapReady) return;
        var payload = _locs.Select(l => new
        {
            id = l.Id,
            name = l.Name,
            lat = l.Lat,
            lng = l.Lng,
            note = l.Note ?? ""
        });
        var json = JsonSerializer.Serialize(payload);
        try
        {
            await MapView.ExecuteScriptAsync($"window.render({json})");
        }
        catch { /* 脚本执行失败忽略（地图页可能尚未就绪） */ }
    }

    /// <summary>处理地图页回传事件：ready / fail / click / drag / focus。</summary>
    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : "";

            switch (type)
            {
                case "ready":
                    _mapReady = true;
                    ShowFallback(false);
                    SetHint("双击列表可定位 · 在地图上点击可预填坐标");
                    _ = PushLocationsAsync();
                    break;

                case "fail":
                    _mapReady = false;
                    var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : "";
                    if (reason == "nokey")
                    {
                        ShowFallback(true,
                            "未检测到可用的地图服务，已切换为列表视图。" +
                            "右侧足迹列表的新增 / 编辑 / 删除完全可用，不依赖地图。");
                        SetHint("未检测到地图服务，使用列表视图");
                        break;
                    }
                    ShowFallback(true, reason switch
                    {
                        "offline" => "当前无网络，地图不可用。可切换列表视图：右侧足迹列表的新增 / 编辑 / 删除完全可用，不依赖地图。",
                        "blank" => "地图底图渲染失败，已自动切换为列表视图。右侧足迹列表的新增 / 编辑 / 删除完全可用；可点上方「⟳ 刷新」或「🔄 重试加载地图」再试一次。",
                        _ => "地图加载失败（请检查网络），可切换列表视图管理足迹。"
                    });
                    SetHint("地图不可用，仍可通过列表管理足迹");
                    break;

                case "click":
                    if (root.TryGetProperty("lat", out var la) && root.TryGetProperty("lng", out var ln))
                    {
                        _pickLat = la.GetDouble();
                        _pickLng = ln.GetDouble();
                        SetHint($"已选中 {_pickLat:F4}, {_pickLng:F4} —— 点「+ 添加足迹」记录");
                    }
                    break;

                case "drag":
                    if (root.TryGetProperty("id", out var id) &&
                        root.TryGetProperty("lat", out var dla) &&
                        root.TryGetProperty("lng", out var dln))
                    {
                        UpdateCoords(id.GetString() ?? "", dla.GetDouble(), dln.GetDouble());
                    }
                    break;

                case "focus":
                    if (root.TryGetProperty("id", out var fid)) SelectRowById(fid.GetString());
                    break;
            }
        }
        catch { /* 消息解析失败忽略 */ }
    }

    private void SelectRowById(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (LocList?.ItemsSource is not List<LocationRow> rows) return;
        var row = rows.FirstOrDefault(r => r.Id == id);
        if (row is not null) LocList.SelectedItem = row;
    }

    /// <summary>拖拽标记落库（与网页端 dragend 行为一致）。</summary>
    private void UpdateCoords(string id, double lat, double lng)
    {
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            var row = db.Locations.Find(id);
            if (row is null) return;
            row.Lat = lat;
            row.Lng = lng;
            db.SaveChanges();
            LoadList();
        }
        catch { /* 落库失败忽略，列表保持原状 */ }
    }

    private void SetHint(string text)
    {
        if (HintText is not null) HintText.Text = text;
    }

    // ==================== 交互 ====================

    private void LocList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        if (LocList?.SelectedItem is not LocationRow row) return;
        if (MapView?.CoreWebView2 is null || !_mapReady) return;
        try
        {
            _ = MapView.ExecuteScriptAsync(
                $"window.focusOn({row.Lng.ToString(CultureInfo.InvariantCulture)}, " +
                $"{row.Lat.ToString(CultureInfo.InvariantCulture)}, " +
                $"{JsonSerializer.Serialize(row.Name)}, {JsonSerializer.Serialize(row.Note ?? "")})");
        }
        catch { /* 忽略 */ }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var loc = new LocationEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Date = DateTime.Today.ToString("yyyy-MM-dd"),
            Lat = _pickLat ?? 39.9042,
            Lng = _pickLng ?? 116.4074
        };
        if (!EditDialog(loc, isNew: true)) return;

        using var db = new AppDbContext(AppPaths.DbFile);
        db.Locations.Add(loc);
        db.SaveChanges();
        AchievementNotifier.Check();
        _pickLat = _pickLng = null;
        LoadList();
        _ = PushLocationsAsync();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string id) return;
        try
        {
            using var db = new AppDbContext(AppPaths.DbFile);
            var loc = db.Locations.Find(id);
            if (loc is null) return;
            if (!EditDialog(loc, isNew: false)) return;
            db.SaveChanges();
            LoadList();
            _ = PushLocationsAsync();
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("保存足迹失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string id) return;
        using var db0 = new AppDbContext(AppPaths.DbFile);
        var name = db0.Locations.Find(id)?.Name ?? "";
        if (!Dialogs.SimpleDialogs.Confirm($"确定删除足迹「{name}」？")) return;

        using var db = new AppDbContext(AppPaths.DbFile);
        var row = db.Locations.Find(id);
        if (row is not null) db.Locations.Remove(row);
        db.SaveChanges();
        AchievementNotifier.Check();
        LoadList();
        _ = PushLocationsAsync();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadList();
        _ = PushLocationsAsync();
    }

    /// <summary>手动定位：输入经纬度 → 地图移动到该点并打蓝点（离线可用，无需授权）。</summary>
    private async void ManualLocate_Click(object sender, RoutedEventArgs e)
    {
        var latText = Dialogs.SimpleDialogs.Prompt("手动定位", "纬度 Lat（-90 ~ 90）", "39.9042");
        if (string.IsNullOrWhiteSpace(latText)) return;
        var lngText = Dialogs.SimpleDialogs.Prompt("手动定位", "经度 Lng（-180 ~ 180）", "116.4074");
        if (string.IsNullOrWhiteSpace(lngText)) return;

        if (!double.TryParse(latText, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(lngText, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var lng) ||
            lat is < -90 or > 90 || lng is < -180 or > 180)
        {
            SimpleDialogs.Alert("请输入有效的经纬度（纬度 -90~90，经度 -180~180）", "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MapView?.CoreWebView2 is null || !_mapReady)
        {
            SetHint("地图未就绪，无法定位");
            return;
        }

        var inv = CultureInfo.InvariantCulture;
        try
        {
            await MapView.ExecuteScriptAsync(
                $"window.setUser({lng.ToString(inv)}, {lat.ToString(inv)}); " +
                $"window.focusOn({lng.ToString(inv)}, {lat.ToString(inv)})");
            SetHint($"已定位到 {lat:F4}, {lng:F4}");
        }
        catch { /* 忽略 */ }
    }

    // ==================== 足迹编辑对话框（内联构建，避免额外依赖） ====================

    /// <summary>新建 / 编辑足迹：true = 已确认（结果写入传入实体）。</summary>
    private static bool EditDialog(LocationEntity loc, bool isNew)
    {
        var win = new ThemeDialogWindow(isNew ? "添加足迹" : "编辑足迹", 420);

        var root = new StackPanel { Margin = new Thickness(20) };

        TextBox Field(string label, string initial)
        {
            root.Children.Add(new TextBlock
            {
                Text = label, FontSize = 13, Margin = new Thickness(0, 10, 0, 4),
                Foreground = ThemeService.Brush("TextSecondaryBrush")
            });
            var box = new TextBox { Text = initial, Padding = new Thickness(8, 6, 8, 6), FontSize = 13 };
            root.Children.Add(box);
            return box;
        }

        var nameBox = Field("名称", loc.Name);
        var latBox = Field("纬度（-90 ~ 90）", loc.Lat.ToString(CultureInfo.InvariantCulture));
        var lngBox = Field("经度（-180 ~ 180）", loc.Lng.ToString(CultureInfo.InvariantCulture));
        var dateBox = Field("日期（YYYY-MM-DD）", string.IsNullOrEmpty(loc.Date)
            ? DateTime.Today.ToString("yyyy-MM-dd") : loc.Date);
        var tagBox = Field("标签（逗号分隔，可留空）", string.Join("，", ParseTags(loc.TagsJson)));
        var noteBox = Field("备注（可留空）", loc.Note ?? "");

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var cancel = new Button
        {
            Content = "取消", MinWidth = 84, Height = 34, Padding = new Thickness(14, 0, 14, 0),
            Background = ThemeService.Brush("CardBgBrush"),
            Foreground = ThemeService.Brush("TextPrimaryBrush"),
            BorderBrush = ThemeService.Brush("BorderBrush"),
            Cursor = Cursors.Hand
        };
        var ok = new Button
        {
            Content = "保存", MinWidth = 84, Height = 34, Padding = new Thickness(14, 0, 14, 0),
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = Brushes.White,
            Background = ThemeService.Brush("AccentBrush"),
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand
        };
        cancel.Click += (_, _) => win.DialogResult = false;
        ok.Click += (_, _) => win.DialogResult = true;
        btnRow.Children.Add(cancel);
        btnRow.Children.Add(ok);
        root.Children.Add(btnRow);
        win.SetBody(root);

        if (win.ShowDialog() != true) return false;

        var inv = CultureInfo.InvariantCulture;
        var name = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            SimpleDialogs.Alert("名称不能为空", "地球Online", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (!double.TryParse(latBox.Text, NumberStyles.Float, inv, out var lat) ||
            !double.TryParse(lngBox.Text, NumberStyles.Float, inv, out var lng) ||
            lat is < -90 or > 90 || lng is < -180 or > 180)
        {
            SimpleDialogs.Alert("经纬度不合法（纬度 -90~90，经度 -180~180）", "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        loc.Name = name;
        loc.Lat = lat;
        loc.Lng = lng;
        loc.Date = dateBox.Text.Trim();
        loc.Note = noteBox.Text.Trim();
        var tags = tagBox.Text.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        loc.TagsJson = SerializeTags(tags);
        return true;
    }
}
