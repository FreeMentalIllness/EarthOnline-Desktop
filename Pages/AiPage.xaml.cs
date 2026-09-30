using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EarthOnline.Desktop.Services;
using EarthOnline.Desktop.Dialogs;

namespace EarthOnline.Desktop.Pages;

/// <summary>聊天气泡（视图模型：对齐 + 底色由角色决定）。</summary>
public class ChatBubble
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public string RoleLabel => Role == "user" ? "你" : "🤖 AI";
    public HorizontalAlignment Align => Role == "user"
        ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    /// <summary>气泡底色按角色取主题画笔：深色模式下换成对应的深色调，保证文字对比度。</summary>
    public Brush BubbleBg => Role == "user"
        ? ThemeService.Brush("AiBubbleUserBrush")   // 琥珀系（用户）
        : ThemeService.Brush("AiBubbleAiBrush");    // 中性（AI）
}

/// <summary>
/// AI 助手页：接口配置（Key 走 DPAPI 加密存储）+ 对话。
/// 请求契约与网页 ai.js / 安卓 AiRepository 完全一致：POST {baseUrl}/chat/completions，
/// Bearer 鉴权，body { model, messages, stream:false }。
/// </summary>
public partial class AiPage : Page
{
    private readonly ObservableCollection<ChatBubble> _msgs = new();
    private AiConfig _cfg = new();
    private bool _busy;

    public AiPage()
    {
        InitializeComponent();
        ConvoList.ItemsSource = _msgs;
        Loaded += (_, _) => LoadConfig();
    }

    // ==================== 配置 ====================

    private void LoadConfig()
    {
        _cfg = AiService.Load();
        BaseUrlBox.Text = _cfg.BaseUrl;
        ModelBox.Text = _cfg.Model;
        KeyBox.Password = _cfg.ApiKey;   // 已解密的明文只在内存中，界面仍以圆点显示
        RefreshState();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // 与网页端同策略：模型为必填项，留空不允许保存（避免"以为配好了其实是空的"）
        if (string.IsNullOrWhiteSpace(ModelBox.Text))
        {
            SimpleDialogs.Alert("请先填写模型名称", "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            AiService.Save(new AiConfig
            {
                BaseUrl = BaseUrlBox.Text.Trim(),
                ApiKey = KeyBox.Password.Trim(),
                Model = ModelBox.Text.Trim()
            });
        }
        catch (Exception ex)
        {
            SimpleDialogs.Alert("保存失败：" + ex.Message, "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _cfg = AiService.Load();
        RefreshState();
        SimpleDialogs.Alert("AI 配置已保存（密钥经 Windows DPAPI 加密后存于本机）", "地球Online",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshState()
    {
        var ready = _cfg.IsReady && !string.IsNullOrWhiteSpace(_cfg.Model);
        WarnText.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        ConfigStatusText.Text = ready
            ? "已配置：" + _cfg.Model + "（密钥已加密保存，不会写入任何代码或日志）"
            : "地址与密钥缺一不可，模型名也必须填写。";
    }

    // ==================== 对话 ====================

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            Send();
        }
    }

    private void Send_Click(object sender, RoutedEventArgs e) => Send();

    private async void Send()
    {
        if (_busy) return;
        var q = InputBox.Text.Trim();
        if (q.Length == 0)
        {
            SimpleDialogs.Alert("先输入点什么吧", "地球Online", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!_cfg.IsReady)
        {
            SimpleDialogs.Alert("请先配置 API 地址和密钥", "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(_cfg.Model))
        {
            SimpleDialogs.Alert("请先填写模型名称", "地球Online",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _msgs.Add(new ChatBubble { Role = "user", Content = q });
        var placeholder = new ChatBubble { Role = "assistant", Content = "🤖 思考中…" };
        _msgs.Add(placeholder);
        InputBox.Clear();
        ScrollToEnd();
        SetBusy(true);

        // 上下文摘要作为 system 消息置于最前：每轮实时生成、只发一份，
        // 不塞进 _msgs —— 否则会随轮次重复堆叠，白白烧 token（网页端同逻辑）。
        var send = new List<ChatMessage>();
        var summary = AiService.BuildContextSummary();
        if (summary.Length > 0) send.Add(new ChatMessage { Role = "system", Content = summary });
        foreach (var m in _msgs.Where(m => !ReferenceEquals(m, placeholder)))
        {
            send.Add(new ChatMessage { Role = m.Role, Content = m.Content });
        }

        try
        {
            var reply = await AiService.ChatAsync(_cfg, send);
            placeholder.Content = reply;
        }
        catch (Exception ex)
        {
            placeholder.Content = "请求失败：" + ex.Message;
        }
        finally
        {
            SetBusy(false);
            RefreshBubble(placeholder);
            ScrollToEnd();
        }
    }

    /// <summary>原地替换后手动刷新（ChatBubble 未实现通知接口，重新赋一次 ItemsSource 即可）。</summary>
    private void RefreshBubble(ChatBubble b)
    {
        var idx = _msgs.IndexOf(b);
        if (idx < 0) return;
        _msgs.RemoveAt(idx);
        _msgs.Insert(idx, b);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SendBtn.Content = busy ? "思考中…" : "发送";
        SendBtn.IsEnabled = !busy;
    }

    private void ScrollToEnd()
    {
        if (ConvoScroll is not null) ConvoScroll.ScrollToEnd();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_msgs.Count > 0 &&
            SimpleDialogs.Alert("清空当前对话？（不会影响已保存的接口配置）", "地球Online",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        _msgs.Clear();
    }
}
