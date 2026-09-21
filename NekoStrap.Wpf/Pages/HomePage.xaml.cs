using System.Windows;
using System.Windows.Controls;

namespace NekoStrap.Wpf.Pages;

/// <summary>
/// Главная: статус, «Играть», прогресс, статы, последняя игра, сервер.
/// Публичный API повторяет WinForms-версию (события + сеттеры), чтобы
/// бэкенд MainWindow переехал без изменений логики.
/// </summary>
public partial class HomePage : UserControl
{
    public event EventHandler? PlayClicked;
    public event EventHandler? PlayAgainClicked;

    private double _progress = -1;

    public HomePage()
    {
        InitializeComponent();
        ProgressTrack.SizeChanged += (_, _) => UpdateProgressWidth();
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        PlayClicked?.Invoke(this, EventArgs.Empty);
    }

    private void AgainButton_Click(object sender, RoutedEventArgs e)
    {
        PlayAgainClicked?.Invoke(this, EventArgs.Empty);
    }

    public void SetStatus(string text, bool ready)
    {
        StatusLabel.Text = text;
        // Единственный цветной акцент — по смыслу: зелёный = готов, серый = ожидание.
        StatusDot.Foreground = ready
            ? (System.Windows.Media.Brush)FindResource("GoodBrush")
            : (System.Windows.Media.Brush)FindResource("FgDimmerBrush");
    }

    public void SetAccount(string text)
    {
        AccountLabel.Text = text;
    }

    public void SetVersionInfo(string text)
    {
        VersionLabel.Text = text;
    }

    public void SetBusy(bool busy)
    {
        PlayButton.IsEnabled = !busy;
        PlayButton.Content = busy ? "Работаю..." : "Играть";
    }

    /// <summary>Прогресс 0..1; null — скрыть полосу.</summary>
    public void SetProgress(double? frac)
    {
        _progress = frac == null ? -1 : Math.Clamp(frac.Value, 0, 1);
        ProgressTrack.Visibility = frac == null ? Visibility.Collapsed : Visibility.Visible;
        UpdateProgressWidth();
    }

    private void UpdateProgressWidth()
    {
        if (_progress < 0 || ProgressTrack.ActualWidth <= 0)
        {
            ProgressFill.Width = 0;
            return;
        }
        ProgressFill.Width = Math.Max(8, ProgressTrack.ActualWidth * _progress);
    }

    public void SetCounts(int mods, int flags, string hours)
    {
        ModsValue.Text = mods.ToString();
        FlagsValue.Text = flags.ToString();
        HoursValue.Text = hours;
    }

    public void SetLastGame(string text, long placeId)
    {
        LastGameLabel.Text = text.Length > 0 ? text : "—";
        AgainButton.Visibility = placeId > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetServer(string ip, string geo, string ping)
    {
        ServerIp.Text = ip;
        ServerGeo.Text = geo;
        ServerPing.Text = ping;
    }

    public void ClearServer()
    {
        SetServer("—", "—", "—");
    }
}
