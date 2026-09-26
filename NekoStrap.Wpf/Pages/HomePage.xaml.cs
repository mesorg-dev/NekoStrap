using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NekoStrap.Wpf.Pages;

/// <summary>Строка результата поиска игр (шаблон списка на главной).</summary>
public sealed class SearchRow
{
    public required long PlaceId { get; init; }
    public required string Name { get; init; }
    public required string Meta { get; init; }

    /// <summary>URL иконки; пусто — плитку не показываем.</summary>
    public string Icon { get; init; } = "";

    public bool HasIcon => Icon.Length > 0;
}

/// <summary>
/// Главная: статус, «Играть», прогресс, статы, последняя игра, сервер.
/// Публичный API повторяет WinForms-версию (события + сеттеры), чтобы
/// бэкенд MainWindow переехал без изменений логики.
/// </summary>
public partial class HomePage : UserControl
{
    public event EventHandler? PlayClicked;
    public event EventHandler? PlayAgainClicked;
    public event EventHandler? CleanLaunchClicked;
    public event Action<long>? FavoritePlayClicked;
    public event Action<long>? FavoriteRemoveClicked;

    /// <summary>Пользователь запросил поиск (по кнопке или Enter).</summary>
    public event Action<string>? SearchQueryEntered;

    /// <summary>Двойной клик по результату: открыть плейс.</summary>
    public event Action<long, string>? SearchPlayRequested;

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

    private void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        CleanLaunchClicked?.Invoke(this, EventArgs.Empty);
    }

    // ---------- Поиск игр ----------

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        SubmitSearch();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        SubmitSearch();
        e.Handled = true;
    }

    private void SubmitSearch()
    {
        string q = SearchBox.Text.Trim();
        if (q.Length == 0) return;
        SearchQueryEntered?.Invoke(q);
    }

    private void SearchList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchList.SelectedItem is SearchRow row)
            SearchPlayRequested?.Invoke(row.PlaceId, row.Name);
    }

    /// <summary>Ожидание ответа API: список прячем, статус меняем.</summary>
    public void SetSearching()
    {
        SearchStatus.Text = Lang.Get("Search_Busy");
        SearchList.Visibility = Visibility.Collapsed;
    }

    /// <summary>Итог поиска: строки и подпись (пусто / ошибка / сколько нашлось).</summary>
    public void ShowSearchResults(List<SearchRow> rows, string status)
    {
        SearchStatus.Text = status;
        SearchList.ItemsSource = rows;
        SearchList.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
        PlayButton.Content = busy ? Lang.Get("Home_Busy") : Lang.Get("Btn_Play");
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
        var vis = placeId > 0 ? Visibility.Visible : Visibility.Collapsed;
        AgainButton.Visibility = vis;
        CleanButton.Visibility = vis;
    }

    /// <summary>Кнопки избранного (клик = играть, правый клик = убрать).</summary>
    public void SetFavorites(IEnumerable<(long placeId, string name)> favorites)
    {
        FavoritesPanel.Children.Clear();
        var list = favorites.ToList();
        FavoritesEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var btnStyle = (Style)FindResource("OutlineButton");
        var itemStyle = (Style)FindResource("DarkMenuItem");
        foreach (var (placeId, name) in list)
        {
            var btn = new Button
            {
                Content = "★ " + (name.Length > 0 ? name : "Place " + placeId),
                ToolTip = "PlaceId " + placeId,
                Style = btnStyle,
                MinWidth = 160,
                Height = 34,
                Margin = new Thickness(0, 6, 10, 0),
                Tag = placeId
            };
            btn.Click += (_, _) => FavoritePlayClicked?.Invoke(placeId);
            var menu = new ContextMenu
            {
                Background = (System.Windows.Media.Brush)FindResource("BgPanelBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("LineBrush"),
                BorderThickness = new Thickness(1)
            };
            var remove = new MenuItem
            {
                Header = Lang.Get("Fav_RemoveCtx"),
                Style = itemStyle,
                Tag = placeId
            };
            remove.Click += (_, _) => FavoriteRemoveClicked?.Invoke(placeId);
            menu.Items.Add(remove);
            btn.ContextMenu = menu;
            FavoritesPanel.Children.Add(btn);
        }
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
