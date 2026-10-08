using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NekoStrap.Roblox;

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

    /// <summary>Пользователь печатает в поиске (дебаунс делает MainWindow).</summary>
    public event Action<string>? SearchQueryChanged;

    /// <summary>Двойной клик по результату: открыть плейс.</summary>
    public event Action<long, string>? SearchPlayRequested;

    private double _progress = -1;

    public HomePage()
    {
        InitializeComponent();
        ProgressTrack.SizeChanged += (_, _) => UpdateProgressWidth();
        BuildInviteBoxes();
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
        if (e.Key == Key.Escape)
        {
            SearchBox.Text = "";
            e.Handled = true;
            return;
        }
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

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchQueryChanged?.Invoke(SearchBox.Text.Trim());
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

    /// <summary>Только подпись под поиском (список не трогаем).</summary>
    public void SetSearchStatus(string text) => SearchStatus.Text = text;

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

    // ================= Код приглашения =================

    /// <summary>Создать код на свой текущий сервер (лимит входов, срок в сек).</summary>
    public event Action<int, int>? InviteCreateRequested;

    /// <summary>Пользователь ввёл код друга и нажал «Войти».</summary>
    public event Action<string>? InviteJoinRequested;

    /// <summary>Бросить свой код: сервер не трогаем, просто прячем карточку.</summary>
    public event Action? InviteDropRequested;

    /// <summary>Метка (tag = число) + ключ подписи. Порядок = порядок в UI.</summary>
    private static readonly (int Tag, string Key)[] InviteUsesLabels =
    {
        (1, "Inv_Uses1"),
        (3, "Inv_Uses3"),
        (10, "Inv_Uses10"),
        (0, "Inv_UsesInf"),
    };

    private static readonly (int Tag, string Key)[] InviteTtlLabels =
    {
        (3600, "Inv_Ttl1h"),
        (86400, "Inv_Ttl24h"),
        (604800, "Inv_Ttl7d"),
        (2592000, "Inv_Ttl30d"),
    };

    private enum InviteState { NeedGame, Ready, Busy, Created, Error }

    private bool _inviteSession;
    private bool _inviteBusy;
    private string _inviteCode = "";
    private InviteState _inviteCreateState = InviteState.NeedGame;
    private string _inviteCreateCode = "";
    // Правая колонка помнит свой текст независимо: либо ключ статуса,
    // либо (в отдельной переменной) код ошибки — иначе один затирает другой.
    private string _inviteJoinStatusKey = "Inv_JoinHint";
    private string _inviteJoinErrorCode = "";

    private void BuildInviteBoxes()
    {
        FillInviteBox(InviteUsesBox, InviteUsesLabels, 0);
        FillInviteBox(InviteTtlBox, InviteTtlLabels, 1);
    }

    private void FillInviteBox(ComboBox box, (int Tag, string Key)[] items, int selected)
    {
        box.Items.Clear();
        foreach (var (tag, key) in items)
            box.Items.Add(new ComboBoxItem { Content = Lang.Get(key), Tag = tag });
        box.SelectedIndex = Math.Clamp(selected, 0, items.Length - 1);
    }

    /// <summary>Лимит входов: 1/3/10, 0 = без лимита.</summary>
    public int InviteUses =>
        InviteUsesBox.SelectedItem is ComboBoxItem it && it.Tag is int n ? n : 1;

    /// <summary>Срок жизни кода в секундах.</summary>
    public int InviteTtlSec =>
        InviteTtlBox.SelectedItem is ComboBoxItem it && it.Tag is int n ? n : 86400;

    /// <summary>Пересобрать подписи при смене языка; выбор сохраняется.</summary>
    public void RefreshInviteLabels()
    {
        int uses = InviteUsesBox.SelectedIndex, ttl = InviteTtlBox.SelectedIndex;
        FillInviteBox(InviteUsesBox, InviteUsesLabels, uses);
        FillInviteBox(InviteTtlBox, InviteTtlLabels, ttl);
        ApplyCreateStatus();
        ApplyJoinStatus();
    }

    /// <summary>Можно ли создавать код: только в живой сессии.</summary>
    public void SetInviteSession(bool hasSession)
    {
        _inviteSession = hasSession;
        if (!hasSession)
            _inviteCreateState = InviteState.NeedGame;
        else if (_inviteCreateState == InviteState.NeedGame)
            _inviteCreateState = InviteState.Ready;
        ApplyCreateStatus();
    }

    public void SetInviteBusy(bool busy)
    {
        _inviteBusy = busy;
        if (busy) _inviteCreateState = InviteState.Busy;
        else if (_inviteCreateState == InviteState.Busy)
            _inviteCreateState = _inviteSession ? InviteState.Created : InviteState.NeedGame;
        ApplyCreateStatus();
    }

    /// <summary>Код создан (или подтверждён сервером при старте).</summary>
    public void ShowInviteCode(string code, int uses, int maxUses, DateTime expires)
    {
        _inviteCode = code;
        InviteCodeText.Text = string.Join(" ", code);
        InviteCodeMeta.Text = InviteMetaText(uses, maxUses, expires);
        InviteCodeCard.Visibility = Visibility.Visible;
        _inviteCreateState = InviteState.Created;
        ApplyCreateStatus();
    }

    public void HideInviteCode()
    {
        _inviteCode = "";
        InviteCodeCard.Visibility = Visibility.Collapsed;
        _inviteCreateState = _inviteSession ? InviteState.Ready : InviteState.NeedGame;
        ApplyCreateStatus();
    }

    /// <summary>Пусто = кода нет.</summary>
    public string InviteCode => _inviteCode;

    /// <summary>Ошибка на левую колонку: пришла из сети или от валидации.</summary>
    public void SetInviteCreateError(string code)
    {
        _inviteCreateState = InviteState.Error;
        _inviteCreateCode = code;
        _inviteBusy = false;
        ApplyCreateStatus();
    }

    /// <summary>Ошибка на правую колонку (вход по коду).</summary>
    public void SetInviteJoinError(string code)
    {
        _inviteJoinErrorCode = code;
        _inviteJoinStatusKey = "";
        ApplyJoinStatus();
    }

    public void SetInviteJoinStatus(string key)
    {
        _inviteJoinErrorCode = "";
        _inviteJoinStatusKey = key;
        ApplyJoinStatus();
    }

    /// <summary>Кнопка «Войти» живёт только при полном коде.</summary>
    public void SyncInviteJoinButton() =>
        InviteJoinButton.IsEnabled = InviteClient.IsValidCode(InviteJoinBox.Text) && !_inviteBusy;

    public void SetInviteJoinBusy(bool busy)
    {
        _inviteBusy = busy;
        InviteJoinBox.IsEnabled = !busy;
        if (busy) SetInviteJoinStatus("Inv_JoinBusy");
        SyncInviteJoinButton();
    }

    private void ApplyCreateStatus()
    {
        InviteCreateStatus.Text = _inviteCreateState switch
        {
            InviteState.Busy => Lang.Get("Inv_Busy"),
            InviteState.Created => Lang.Get("Inv_Created"),
            InviteState.Ready => Lang.Get("Inv_Ready"),
            InviteState.Error => InviteErrorText(_inviteCreateCode),
            _ => Lang.Get("Inv_NeedGame"),
        };
        InviteCreateButton.IsEnabled = !_inviteBusy && _inviteSession;
        InviteCreateStatus.Foreground = _inviteCreateState == InviteState.Error
            ? (System.Windows.Media.Brush)FindResource("BadBrush")
            : (System.Windows.Media.Brush)FindResource("FgDimmerBrush");
    }

    private void ApplyJoinStatus()
    {
        InviteJoinStatus.Text = _inviteJoinErrorCode.Length > 0
            ? InviteErrorText(_inviteJoinErrorCode)
            : Lang.Get(_inviteJoinStatusKey);
        InviteJoinStatus.Foreground = _inviteJoinErrorCode.Length > 0
            ? (System.Windows.Media.Brush)FindResource("BadBrush")
            : (System.Windows.Media.Brush)FindResource("FgDimmerBrush");
    }

    /// <summary>Мета под кодом: лимит + срок.</summary>
    public static string InviteMetaText(int uses, int maxUses, DateTime expires)
    {
        string until = expires > DateTime.MinValue
            ? expires.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Lang.Culture)
            : "—";
        if (maxUses <= 0) return Lang.Format("Inv_MetaInfFmt", until);
        if (maxUses == 1) return Lang.Format("Inv_MetaOneFmt", until);
        return Lang.Format("Inv_MetaFmt", Math.Min(uses, maxUses), maxUses, until);
    }

    /// <summary>Ключ перевода для кода ошибки сервера/клиента.</summary>
    public static string InviteErrorKey(string code) => code switch
    {
        NekoStrap.Roblox.InviteErrors.Offline => "InvErr_Offline",
        NekoStrap.Roblox.InviteErrors.Timeout => "InvErr_Timeout",
        NekoStrap.Roblox.InviteErrors.Cancelled => "InvErr_Cancelled",
        NekoStrap.Roblox.InviteErrors.Unsupported => "InvErr_Unsupported",
        NekoStrap.Roblox.InviteErrors.Gone => "InvErr_Gone",
        NekoStrap.Roblox.InviteErrors.Expired => "InvErr_Expired",
        NekoStrap.Roblox.InviteErrors.Used => "InvErr_Used",
        NekoStrap.Roblox.InviteErrors.BadCode => "InvErr_BadCode",
        NekoStrap.Roblox.InviteErrors.RateLimit => "InvErr_RateLimit",
        NekoStrap.Roblox.InviteErrors.BadPlace => "InvErr_BadPlace",
        NekoStrap.Roblox.InviteErrors.CodeFull => "InvErr_CodeFull",
        _ => "InvErr_Internal",
    };

    public static string InviteErrorText(string code) => Lang.Get(InviteErrorKey(code));

    // ---------- обработчики ----------

    private void InviteCreate_Click(object sender, RoutedEventArgs e)
    {
        if (_inviteBusy || !_inviteSession) return;
        InviteCreateRequested?.Invoke(InviteUses, InviteTtlSec);
    }

    private void InviteJoin_Click(object sender, RoutedEventArgs e) => SubmitInvite();

    private void InviteJoinBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        SubmitInvite();
        e.Handled = true;
    }

    private void SubmitInvite()
    {
        if (_inviteBusy) return;
        string code = InviteClient.NormalizeCode(InviteJoinBox.Text);
        if (code.Length != InviteClient.CodeLength)
        {
            SetInviteJoinError(InviteErrors.BadCode);
            return;
        }
        InviteJoinRequested?.Invoke(code);
    }

    private void InviteJoinBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // WPF не умеет CharacterCasing — приводим сами: всегда наш алфавит.
        string clean = InviteClient.NormalizeCode(InviteJoinBox.Text);
        if (clean != InviteJoinBox.Text)
        {
            int caret = InviteJoinBox.CaretIndex;
            InviteJoinBox.Text = clean;
            InviteJoinBox.CaretIndex = Math.Min(caret, clean.Length);
        }
        SyncInviteJoinButton();
    }

    private async void InviteCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_inviteCode.Length == 0) return;
        string code = _inviteCode;
        try { Clipboard.SetText(code); } catch { /* буфер занят */ }
        // Короткая отдельная строка вместо тоста: карточка и так на виду.
        string meta = InviteCodeMeta.Text;
        InviteCodeMeta.Text = Lang.Get("Inv_Copied");
        await Task.Delay(1500);
        if (_inviteCode == code) InviteCodeMeta.Text = meta;
    }

    private void InviteDrop_Click(object sender, RoutedEventArgs e)
    {
        HideInviteCode();
        InviteDropRequested?.Invoke();
    }
}
