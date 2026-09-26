using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NekoStrap.Roblox;

namespace NekoStrap.Wpf;

/// <summary>
/// Летающий оверлей чата поверх игры (TopMost, без таскбара).
/// Шапка/подвал перетаскиваются зажатием мыши; кнопка «—» сворачивает
/// в полоску статуса, «×» прячет окно (сам чат при этом не рвётся).
/// Отправляет только в общий канал сервера — ЛС живут в странице чата.
/// </summary>
public partial class ChatOverlayWindow : Window
{
    private const double ExpandedHeight = 480;
    private const double CollapsedHeight = 36;

    /// <summary>Отправить текст в общий чат сервера.</summary>
    public event Action<string>? SendRequested;

    private readonly List<ChatMessage> _messages = new();
    private const int MaxMessages = 200;
    private bool _collapsed;

    internal ChatOverlayWindow()
    {
        InitializeComponent();
        Height = ExpandedHeight;
    }

    // ================= Drag / сворачивание =================

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Зажали ЛКМ — тащим окно, пока не отпустили (стандарт DragMove).
        if (e.ChangedButton != MouseButton.Left) return;
        try
        {
            if (WindowState == WindowState.Normal)
                DragMove();
        }
        catch { /* гонка отпускания кнопки */ }
    }

    private void MinButton_Click(object sender, RoutedEventArgs e)
    {
        _collapsed = !_collapsed;
        Body.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        // В свёрнутом состоянии (только шапка 36px) футеру места нет.
        FooterBar.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        Height = _collapsed ? CollapsedHeight : ExpandedHeight;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    // ================= Данные =================

    public void SetState(ChatState state)
    {
        StateDot.Fill = state switch
        {
            ChatState.Connected => (Brush)FindResource("GoodBrush"),
            ChatState.Connecting => (Brush)FindResource("FgDimBrush"),
            _ => (Brush)FindResource("FgDimmerBrush"),
        };
        HintLabel.Text = state == ChatState.Connected
            ? Lang.Get("Chat_WipShort")
            : Lang.Get(state == ChatState.Connecting ? "Chat_Connecting" : "Chat_Disconnected");
    }

    public void SetUserCount(int count)
    {
        UsersLabel.Text = Lang.Format("Chat_UsersFmt", count);
    }

    public void SetHint(string text) => HintLabel.Text = text;

    public void ResetMessages()
    {
        _messages.Clear();
        MessagesPanel.Children.Clear();
    }

    public void Replay(IReadOnlyList<ChatMessage> messages)
    {
        ResetMessages();
        foreach (var m in messages)
            AppendMessage(m);
    }

    public void AppendMessage(ChatMessage msg)
    {
        _messages.Add(msg);
        while (_messages.Count > MaxMessages)
            _messages.RemoveAt(0);

        var tb = new TextBlock
        {
            Text = FormatMessage(msg),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 1),
            FontSize = 11.5,
        };
        tb.SetResourceReference(FontFamilyProperty, "MonoFont");
        tb.SetResourceReference(ForegroundProperty, msg.Mine ? "FgBrush" : "FgDimBrush");
        MessagesPanel.Children.Add(tb);
        while (MessagesPanel.Children.Count > MaxMessages)
            MessagesPanel.Children.RemoveAt(0);
        MessagesScroll.ScrollToEnd();
    }

    private static string FormatMessage(ChatMessage msg)
    {
        string time = msg.At.ToLocalTime().ToString("HH:mm");
        string who = msg.Mine ? Lang.Get("Chat_You") : msg.Name;
        string dm = msg.To > 0 ? "[" + Lang.Get("Chat_DmShort") + "] " : "";
        return $"{time}  {dm}{who}: {msg.Text}";
    }

    // ================= Отправка =================

    private void SendCurrent()
    {
        string text = InputBox.Text.Trim();
        if (text.Length == 0) return;
        SendRequested?.Invoke(text);
        InputBox.Text = "";
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
}
