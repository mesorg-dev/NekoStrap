using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NekoStrap.Roblox;

namespace NekoStrap.Wpf.Pages;

/// <summary>Строка итогов по плейсам.</summary>
public sealed class GameRow
{
    public required string Name { get; init; }
    public required string Played { get; init; }
    public required string Last { get; init; }
    public required string PlaceText { get; init; }
    public required long PlaceId { get; init; }
}

/// <summary>Строка лога заходов.</summary>
public sealed class SessionRow
{
    public required string Game { get; init; }
    public required string PlaceText { get; init; }
    public required string Job { get; init; }
    public required string When { get; init; }
    public required string Duration { get; init; }
    public required string Ip { get; init; }
    public required RecentSession Session { get; init; }
}

/// <summary>История игр и лог заходов с перезаходом на тот же сервер.</summary>
public partial class HistoryPage : UserControl
{
    public event EventHandler? PlayClicked;
    public event EventHandler? DeleteClicked;
    public event EventHandler? ClearClicked;
    public event EventHandler? SortChanged;
    public event EventHandler? FavoriteAddClicked;

    public event EventHandler? SessionRejoinClicked;
    public event EventHandler? SessionJoinClicked;
    public event EventHandler? SessionCopyPlaceClicked;
    public event EventHandler? SessionCopyLinkClicked;
    public event EventHandler? SessionOpenSiteClicked;
    public event EventHandler? SessionDeleteClicked;
    public event EventHandler? SessionClearClicked;

    public bool SortByRecent { get; private set; } = true;

    private readonly ObservableCollection<GameRow> _games = new();
    private readonly ObservableCollection<SessionRow> _sessions = new();

    private readonly Style _on;
    private readonly Style _off;

    public HistoryPage()
    {
        InitializeComponent();
        HistoryList.ItemsSource = _games;
        SessionList.ItemsSource = _sessions;
        _on = (Style)FindResource("PrimaryButton");
        _off = (Style)FindResource("OutlineButton");
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e) => PlayClicked?.Invoke(this, EventArgs.Empty);
    private void DeleteButton_Click(object sender, RoutedEventArgs e) => DeleteClicked?.Invoke(this, EventArgs.Empty);
    private void ClearButton_Click(object sender, RoutedEventArgs e) => ClearClicked?.Invoke(this, EventArgs.Empty);
    private void FavoriteButton_Click(object sender, RoutedEventArgs e) => FavoriteAddClicked?.Invoke(this, EventArgs.Empty);
    private void RejoinButton_Click(object sender, RoutedEventArgs e) => SessionRejoinClicked?.Invoke(this, EventArgs.Empty);
    private void JoinButton_Click(object sender, RoutedEventArgs e) => SessionJoinClicked?.Invoke(this, EventArgs.Empty);
    private void CopyPlaceButton_Click(object sender, RoutedEventArgs e) => SessionCopyPlaceClicked?.Invoke(this, EventArgs.Empty);
    private void CopyLinkButton_Click(object sender, RoutedEventArgs e) => SessionCopyLinkClicked?.Invoke(this, EventArgs.Empty);
    private void SiteButton_Click(object sender, RoutedEventArgs e) => SessionOpenSiteClicked?.Invoke(this, EventArgs.Empty);
    private void DeleteSessionButton_Click(object sender, RoutedEventArgs e) => SessionDeleteClicked?.Invoke(this, EventArgs.Empty);
    private void ClearSessionsButton_Click(object sender, RoutedEventArgs e) => SessionClearClicked?.Invoke(this, EventArgs.Empty);

    private void SessionList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        SessionRejoinClicked?.Invoke(this, EventArgs.Empty);
    }

    private void SortRecentButton_Click(object sender, RoutedEventArgs e) => SetSort(true);
    private void SortTopButton_Click(object sender, RoutedEventArgs e) => SetSort(false);

    private void SetSort(bool recent)
    {
        if (SortByRecent == recent) return;
        SortByRecent = recent;
        SortRecentButton.Style = recent ? _on : _off;
        SortTopButton.Style = recent ? _off : _on;
        SortChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetGames(IEnumerable<(long placeId, string name, long seconds, DateTime last)> games)
    {
        _games.Clear();
        foreach (var (placeId, name, seconds, last) in games)
        {
            _games.Add(new GameRow
            {
                Name = name.Length > 0 ? name : Lang.Format("Common_PlaceFmt", placeId),
                Played = PlaytimeStore.Format(seconds,
                    Lang.Get("Time_LessMinute"), Lang.Get("Time_Min"), Lang.Get("Time_Hour")),
                Last = last == default ? "—" : last.ToLocalTime().ToString("d MMM yyyy, HH:mm", Lang.Culture),
                PlaceText = placeId > 0 ? placeId.ToString() : "—",
                PlaceId = placeId
            });
        }
    }

    public void SetSessions(IEnumerable<RecentSession> sessions)
    {
        _sessions.Clear();
        foreach (var s in sessions)
        {
            _sessions.Add(new SessionRow
            {
                Game = s.DisplayName,
                PlaceText = s.PlaceId > 0 ? s.PlaceId.ToString() : "—",
                Job = s.ShortJob,
                When = s.JoinedAt == default ? "—" : RecentSessionStore.FormatWhen(s.JoinedAt,
                    Lang.Get("Time_Now"), Lang.Get("Time_MinAgoFmt"), Lang.Get("Time_HourAgoFmt"),
                    Lang.Get("Time_DayAgoFmt"), Lang.Culture),
                Duration = s.JoinedAt == default ? "—"
                    : (s.LeftAt == null ? Lang.Get("Hist_PlayingNow") : RecentSessionStore.FormatDuration(s.Duration,
                        Lang.Get("Time_LessMinute"), Lang.Get("Time_Min"), Lang.Get("Time_Hour"))),
                Ip = s.ServerIp.Length > 0 ? s.ServerIp : "—",
                Session = s
            });
        }
    }

    public long SelectedPlaceId()
    {
        return HistoryList.SelectedItem is GameRow r ? r.PlaceId : 0;
    }

    /// <summary>Выбранная игра: id + отображаемое имя (для избранного).</summary>
    public (long placeId, string name) SelectedGame()
    {
        if (HistoryList.SelectedItem is GameRow r)
            return (r.PlaceId, r.Name);
        return (0, "");
    }

    public RecentSession? SelectedSession()
    {
        return SessionList.SelectedItem is SessionRow r ? r.Session : null;
    }
}
