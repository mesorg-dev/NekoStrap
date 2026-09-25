using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NekoStrap.Roblox;

namespace NekoStrap.Wpf.Pages;

/// <summary>
/// Страница чата в лаунчере: общий чат роблокс-сервера + ЛС по списку игроков.
/// Виден только тот, кто на том же сервере и тоже использует NekoStrap.
/// Код по конвенции проекта: страница не лезет в сеть сама — шлёт события,
/// всё коммутирует MainWindow.
/// </summary>
public partial class ChatPage : UserControl
{
    /// <summary>Отправить в общий чат сервера.</summary>
    public event Action<string>? ServerMessageSend;

    /// <summary>Отправить ЛС игроку uid.</summary>
    public event Action<long, string>? DmMessageSend;

    /// <summary>Открыть летающий оверлей чата.</summary>
    public event Action? OpenOverlayClicked;

    private const int MaxMessages = 300;

    private long _selfUid;
    private long _dmTarget; // 0 = общий чат
    private bool _dmEnabled = true;
    private readonly List<ChatUser> _users = new();

    public ChatPage()
    {
        InitializeComponent();
    }

    // ================= Состояние =================

    /// <summary>Точка состояния + подпись (поток UI).</summary>
    public void SetState(ChatState state)
    {
        StateLabel.Text = state switch
        {
            ChatState.Connected => Lang.Get("Chat_Connected"),
            ChatState.Connecting => Lang.Get("Chat_Connecting"),
            _ => Lang.Get("Chat_Disconnected"),
        };
        StateDot.Fill = state switch
        {
            ChatState.Connected => (Brush)FindResource("GoodBrush"),
            ChatState.Connecting => (Brush)FindResource("FgDimBrush"),
            _ => (Brush)FindResource("FgDimmerBrush"),
        };
        if (state != ChatState.Connected)
        {
            _dmTarget = 0;
            RefreshModeButtons();
        }
    }

    /// <summary>Человекопонятная строка статуса (своя, вне enum).</summary>
    public void SetStatus(string text, bool ok)
    {
        StateLabel.Text = text;
        StateDot.Fill = (Brush)FindResource(ok ? "GoodBrush" : "FgDimBrush");
    }

    public void SetHint(string text) => HintLabel.Text = text;

    public void SetDmEnabled(bool enabled)
    {
        _dmEnabled = enabled;
        DmButton.IsEnabled = enabled;
        if (!enabled && _dmTarget != 0)
        {
            _dmTarget = 0;
            RefreshModeButtons();
        }
    }

    public void SetUsers(IEnumerable<ChatUser> users, long selfUid)
    {
        _selfUid = selfUid;
        _users.Clear();
        _users.AddRange(users);
        UsersList.Items.Clear();
        foreach (var u in _users.OrderBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            string label = u.Uid == selfUid ? u.Name + " (" + Lang.Get("Chat_You") + ")" : u.Name;
            var item = new ListBoxItem
            {
                Content = label,
                Tag = u.Uid,
                Padding = new Thickness(10, 5, 10, 5),
                ToolTip = Lang.Format("Chat_DmHintFmt", u.Name),
            };
            item.SetResourceReference(ForegroundProperty, u.Uid == selfUid ? "FgBrush" : "FgDimBrush");
            UsersList.Items.Add(item);
        }
        if (_dmTarget != 0 && !_users.Any(u => u.Uid == _dmTarget))
        {
            _dmTarget = 0;
            RefreshModeButtons();
        }
    }

    // ================= Сообщения =================

    public void ResetForRoom()
    {
        MessagesPanel.Children.Clear();
    }

    public void AppendMessage(ChatMessage msg)
    {
        var tb = new TextBlock
        {
            Text = FormatMessage(msg),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 1),
            FontSize = 12,
        };
        tb.SetResourceReference(FontFamilyProperty, "MonoFont");
        tb.SetResourceReference(ForegroundProperty, msg.Mine ? "FgBrush" : "FgDimBrush");
        MessagesPanel.Children.Add(tb);
        while (MessagesPanel.Children.Count > MaxMessages)
            MessagesPanel.Children.RemoveAt(0);
        MessagesScroll.ScrollToEnd();
    }

    private string FormatMessage(ChatMessage msg)
    {
        string time = msg.At.ToLocalTime().ToString("HH:mm");
        string who = msg.Mine ? Lang.Get("Chat_You") : msg.Name;
        if (msg.To > 0)
        {
            // Исходящее ЛС: адресат по uid из списка; входящее — автор слева.
            string peer = msg.Mine
                ? _users.FirstOrDefault(u => u.Uid == msg.To)?.Name ?? "?"
                : msg.Name;
            return msg.Mine
                ? $"{time}  {Lang.Format("Chat_DmToFmt", peer)}{who}: {msg.Text}"
                : $"{time}  {Lang.Get("Chat_DmFrom")}{peer}: {msg.Text}";
        }
        return $"{time}  {who}: {msg.Text}";
    }

    // ================= Отправка =================

    public string InputText
    {
        get => InputBox.Text;
        set => InputBox.Text = value;
    }

    public void ClearInput() => InputBox.Text = "";

    public long DmTarget => _dmTarget;

    private void SendCurrent()
    {
        string text = InputBox.Text.Trim();
        if (text.Length == 0) return;
        if (_dmTarget > 0)
        {
            if (!_dmEnabled) return;
            DmMessageSend?.Invoke(_dmTarget, text);
        }
        else
        {
            ServerMessageSend?.Invoke(text);
        }
        InputBox.Text = "";
        InputBox.Focus();
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => SendCurrent();

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SendCurrent();
        }
    }

    // ================= Режимы / оверлей =================

    private void AllButton_Click(object sender, RoutedEventArgs e)
    {
        _dmTarget = 0;
        RefreshModeButtons();
        InputBox.Focus();
    }

    private void DmButton_Click(object sender, RoutedEventArgs e) => PickDmTarget();

    private void UsersList_DoubleClick(object sender, MouseButtonEventArgs e) => PickDmTarget();

    private void PickDmTarget()
    {
        if (!_dmEnabled) return;
        if (UsersList.SelectedItem is not ListBoxItem item || item.Tag is not long uid || uid <= 0)
            return;
        if (uid == _selfUid) return;
        _dmTarget = uid;
        RefreshModeButtons();
        InputBox.Focus();
    }

    private void RefreshModeButtons()
    {
        bool dmMode = _dmTarget > 0;
        var on = (Style)FindResource("PrimaryButton");
        var off = (Style)FindResource("OutlineButton");
        AllButton.Style = dmMode ? off : on;
        DmButton.Style = dmMode ? on : off;
        AllButton.Content = Lang.Get("Chat_All");
        DmButton.Content = dmMode
            ? Lang.Format("Chat_DmWithFmt",
                _users.FirstOrDefault(u => u.Uid == _dmTarget)?.Name ?? "?")
            : Lang.Get("Chat_Dm");
        HintLabel.Text = dmMode
            ? Lang.Format("Chat_DmOpenFmt",
                _users.FirstOrDefault(u => u.Uid == _dmTarget)?.Name ?? "?")
            : Lang.Get("Chat_NotInGame");
    }

    private void OverlayButton_Click(object sender, RoutedEventArgs e)
    {
        OpenOverlayClicked?.Invoke();
    }

    /// <summary>Смена языка: пересобрать подписи режимов.</summary>
    public void RefreshLabels()
    {
        RefreshModeButtons();
        SetDmEnabled(_dmEnabled);
    }
}
