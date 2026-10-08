using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NekoStrap.Display;
using NekoStrap.Media;
using NekoStrap.Roblox;
using NekoStrap.Utils;
using NekoStrap.Wpf.Pages;
using WinForms = System.Windows.Forms;

namespace NekoStrap.Wpf;

/// <summary>
/// Главное окно: кастомный хром через WindowChrome (нативные drag/ресайз/снап),
/// сайдбар-навигация, контент-хост. Страницы живут постоянно и переключаются
/// мгновенно — никаких анимаций целыми окнами.
/// </summary>
public partial class MainWindow : Window
{
    private LauncherConfig _config = new();
    private PlaytimeStore _playtime = PlaytimeStore.Load();
    private RecentSessionStore _recent = RecentSessionStore.Load();
    private bool _busy;
    private bool _closed;
    private bool _reallyExit;
    private bool _hasWallpaper;
    private GifPlayer? _gifPlayer;

    private readonly SessionWatcher _watcher = new();
    private readonly DiscordManager _discord = new();

    // Кэш иконок плейсов для Discord-присутствия (гость ходит по серверам
    // одного места — тянуть картинку заново каждый раз незачем).
    private readonly Dictionary<long, string> _placeIconCache = new();
    private readonly Media.MusicManager _music = new();
    private readonly ChatClient _chat = new();
    private Process? _playerProcess;
    private ProcessOptimizer? _optimizer;
    private WinForms.NotifyIcon? _trayIcon;
    private ColorTuneWindow? _tuneWindow;
    private ChatOverlayWindow? _chatOverlay;
    private PerfOverlayWindow? _perfOverlay;
    private readonly GamePerf _perf = new();
    private DispatcherTimer? _perfTimer;
    private readonly List<ChatMessage> _chatLog = new();
    private string _chatRoom = "";
    private bool _tuningOpen;
    private DispatcherTimer? _glassTimer;
    private AppRelease? _pendingRelease;
    private readonly DispatcherTimer _serverTimer;
    private DispatcherTimer? _colorTimer;
    private int _sessionSeq;
    private string _lastServerIp = "";
    private string _lastGeoText = "—";
    private string _lastPingText = "—";
    private string _lastGameName = "";

    // --- Счётчик времени ---
    private DateTime _sessionStartedAt;
    private long _sessionPlaceId;
    private string _sessionName = "";

    // Когда стартовал клиент (для диагностики крашей).
    private DateTime _playerStartedAt;

    private readonly HomePage _homePage = new();
    private readonly ModsPage _modsPage = new();
    private readonly FastFlagsPage _flagsPage = new();
    private readonly VersionsPage _versionsPage = new();
    private readonly HistoryPage _historyPage = new();
    private readonly ChatPage _chatPage = new();
    private readonly SettingsPage _settingsPage = new();
    private readonly AboutPage _aboutPage = new();
    private readonly Dictionary<RadioButton, UserControl> _pages = new();

    public MainWindow()
    {
        InitializeComponent();
        Glass.Wallpaper = WallpaperImage;

        _pages[NavHome] = _homePage;
        _pages[NavMods] = _modsPage;
        _pages[NavFlags] = _flagsPage;
        _pages[NavVersions] = _versionsPage;
        _pages[NavHistory] = _historyPage;
        _pages[NavChat] = _chatPage;
        _pages[NavSettings] = _settingsPage;
        _pages[NavAbout] = _aboutPage;

        _homePage.PlayClicked += async (_, _) => await PlayFlowAsync();
        _homePage.PlayAgainClicked += (_, _) => PlayAgain();
        _homePage.CleanLaunchClicked += (_, _) => StartCleanLaunch();
        _homePage.FavoritePlayClicked += (placeId) => LaunchPlaceById(placeId);
        _homePage.FavoriteRemoveClicked += (placeId) => RemoveFavorite(placeId);
        _homePage.SearchQueryEntered += async (q) =>
        {
            CancelSearchDebounce();
            await SearchGamesAsync(q);
        };
        _homePage.SearchQueryChanged += QueueSearch;
        _homePage.SearchPlayRequested += (placeId, _) => LaunchPlaceById(placeId);
        _homePage.InviteCreateRequested += async (uses, ttl) => await CreateInviteAsync(uses, ttl);
        _homePage.InviteJoinRequested += async code => await JoinByInviteAsync(code);
        _homePage.InviteDropRequested += DropInvite;

        _modsPage.AddModClicked += (_, _) => AddMods();
        _modsPage.RefreshClicked += (_, _) => RefreshMods();
        _modsPage.ToggleModClicked += (_, _) => ToggleSelectedMods();
        _modsPage.DeleteModClicked += (_, _) => DeleteSelectedMods();
        _modsPage.FleasionDownloadClicked += async (_, _) => await DownloadFleasionAsync();
        _modsPage.FleasionCheck.Checked += (_, _) =>
        {
            _config.FleasionEnabled = true;
            SaveQuiet();
            RefreshFleasionStatus();
        };
        _modsPage.FleasionCheck.Unchecked += (_, _) =>
        {
            _config.FleasionEnabled = false;
            SaveQuiet();
            RefreshFleasionStatus();
        };
        _modsPage.ProfileSelected += name => SwitchModProfile(name);
        _modsPage.ProfileCreateClicked += (_, _) => CreateModProfile(copyActive: false);
        _modsPage.ProfileDuplicateClicked += (_, _) => CreateModProfile(copyActive: true);
        _modsPage.ProfileDeleteClicked += (_, _) => DeleteModProfile();
        _modsPage.ProfileFolderClicked += (_, _) => OpenModProfileFolder();
        _modsPage.QuickPickRequested += id => PickQuickAsset(id);
        _modsPage.QuickResetRequested += id => ResetQuickAsset(id);
        _modsPage.QuickTargetEdited += (id, path) => EditQuickTarget(id, path);
        _modsPage.ProfileBindRequested += (_, _) => BindProfileToGame(mod: true);
        _modsPage.ProfileUnbindRequested += (_, _) => UnbindProfileFromGame(mod: true);
        _modsPage.ProfileExportRequested += (_, _) => ExportModProfile();
        _modsPage.ProfileImportRequested += (_, _) => ImportModProfile();

        _flagsPage.AddFlagClicked += (_, _) => _flagsPage.AddNewFlag();
        _flagsPage.ProfileApplyClicked += (name) => ApplyFlagProfile(name);
        _flagsPage.ProfileSaveClicked += (_, _) => SaveFlagProfile();
        _flagsPage.ProfileDeleteClicked += (_, _) => DeleteFlagProfile();
        _flagsPage.ProfileBindRequested += (_, _) => BindProfileToGame(mod: false);
        _flagsPage.ProfileUnbindRequested += (_, _) => UnbindProfileFromGame(mod: false);
        _flagsPage.DeleteAllClicked += (_, _) => DeleteAllFlags();
        _flagsPage.SaveClicked += (_, _) => SaveFlags();
        _flagsPage.ImportClicked += (_, _) => ImportFlags();
        _flagsPage.PasteClicked += (_, _) => ImportFlagsFromClipboard();
        _flagsPage.ExportClicked += (_, _) => ExportFlags();
        _flagsPage.Changed += (_, _) => MarkFlagsDirty();
        _flagsPage.ValidationError += (name, value, error) => Toast(error, ToastKind.Error);

        _versionsPage.ActivateClicked += (_, _) => ActivateSelectedVersion();
        _versionsPage.DeleteClicked += (_, _) => DeleteSelectedVersion();
        _versionsPage.StudioInstallClicked += async (_, _) => await InstallStudioAsync();
        _versionsPage.StudioLaunchClicked += (_, _) => LaunchStudio();
        _versionsPage.CheckFilesClicked += async (_, _) => await CheckClientIntegrityAsync();
        _versionsPage.RepairClicked += async (_, _) => await RepairClientAsync();

        _historyPage.PlayClicked += (_, _) => PlayHistorySelected();
        _historyPage.FavoriteAddClicked += (_, _) => AddFavoriteFromHistory();
        _historyPage.DeleteClicked += (_, _) => DeleteHistorySelected();
        _historyPage.ClearClicked += (_, _) => ClearHistory();
        _historyPage.SortChanged += (_, _) => RefreshHistory();
        _historyPage.SessionRejoinClicked += (_, _) => RejoinSessionSelected();
        _historyPage.SessionJoinClicked += (_, _) => JoinSessionPlace();
        _historyPage.SessionCopyPlaceClicked += (_, _) => CopySessionPlaceId();
        _historyPage.SessionCopyLinkClicked += (_, _) => CopySessionLink();
        _historyPage.SessionOpenSiteClicked += (_, _) => OpenSessionSite();
        _historyPage.SessionDeleteClicked += (_, _) => DeleteSessionSelected();
        _historyPage.SessionClearClicked += (_, _) => ClearSessions();

        _aboutPage.CheckUpdatesClicked += async (_, _) => await CheckForAppUpdatesAsync(manual: true);
        _aboutPage.DownloadUpdateClicked += async (_, _) => await DownloadAndInstallAsync();

        _settingsPage.SaveClicked += (_, _) =>
        {
            SaveConfigFromUi();
            WarnInvalidSettings();
            SetStatus(Lang.Get("Set_Saved"), true);
        };
        _settingsPage.SoundPickClicked += (key) => PickCustomSound(key);
        _settingsPage.SoundResetClicked += (key) => ResetCustomSound(key);
        _settingsPage.SoundVolumeChanged += (key, v) =>
        {
            ClickSound.SetSoundVolume(key, v / 100f);
            _config.SoundVolumes[key] = v / 100f;
            SaveQuiet();
        };
        _settingsPage.AppearanceChanged += () => ApplyAppearanceFromUi(save: true);
        _settingsPage.AppearanceResetClicked += () => ResetAppearance();
        _settingsPage.LanguagePicked += (choice) => ApplyLanguage(choice, save: true);
        _settingsPage.CdnApplyClicked += (_, _) => CdnApply();
        _settingsPage.CdnRollbackClicked += (_, _) => CdnRollback();
        _settingsPage.TelemetryBlockChanged += (on) => ApplyTelemetryBlock(on);
        _settingsPage.PerfOverlayChanged += (on) =>
        {
            _config.PerfOverlay = on;
            SaveQuiet();
            if (on) ShowPerfOverlay(); else HidePerfOverlay();
        };
        _settingsPage.PerfMetricsChanged += () =>
        {
            _config.PerfShowPing = _settingsPage.PerfPingCheck.IsChecked == true;
            _config.PerfShowCpu = _settingsPage.PerfCpuCheck.IsChecked == true;
            _config.PerfShowRam = _settingsPage.PerfRamCheck.IsChecked == true;
            _config.PerfShowGpu = _settingsPage.PerfGpuCheck.IsChecked == true;
            _config.PerfShowServer = _settingsPage.PerfServerCheck.IsChecked == true;
            _config.PerfShowTime = _settingsPage.PerfTimeCheck.IsChecked == true;
            SaveQuiet();
            ApplyPerfRows();
        };
        _settingsPage.PerfGlassChanged += (glass) =>
        {
            _config.PerfOverlayGlass = glass;
            SaveQuiet();
            _perfOverlay?.ApplyStyle(glass, _config.PerfOverlayOpacity);
        };
        _settingsPage.PerfOpacityChanged += (v) =>
        {
            _config.PerfOverlayOpacity = v;
            SaveQuiet();
            _perfOverlay?.ApplyStyle(_config.PerfOverlayGlass, v);
        };
        _settingsPage.PerfShowClicked += () =>
        {
            if (_perfOverlay != null && _perfOverlay.IsVisible) HidePerfOverlay();
            else ShowPerfOverlay();
        };
        _settingsPage.WallpaperPickClicked += (_, _) => PickWallpaper();
        _settingsPage.WallpaperResetClicked += (_, _) => ResetWallpaper();
        _settingsPage.WallpaperBlurChanged += (v) =>
        {
            _config.WallpaperBlur = v;
            ApplyWallpaperBlur();
            SaveQuiet();
        };
        _settingsPage.WallpaperDimChanged += (v) =>
        {
            _config.WallpaperDim = v;
            UpdateWallpaperDim();
            SaveQuiet();
        };
        _settingsPage.GlassCheck.Checked += (_, _) =>
        {
            _config.GlassEnabled = true;
            SaveQuiet();
            ApplyGlassToUi();
        };
        _settingsPage.GlassCheck.Unchecked += (_, _) =>
        {
            _config.GlassEnabled = false;
            SaveQuiet();
            ApplyGlassToUi();
        };
        _settingsPage.GlassOpacityChanged += (v) =>
        {
            _config.GlassOpacity = v;
            SaveQuiet();
            ApplyGlassToUi();
        };
        _settingsPage.GlassBlurChanged += (v) =>
        {
            _config.GlassBlur = v;
            SaveQuiet();
            ApplyGlassToUi();
        };
        _settingsPage.GlassDimChanged += (v) =>
        {
            _config.GlassDim = v;
            SaveQuiet();
            ApplyGlassToUi();
        };
        _settingsPage.GlassPureChanged += (pure) =>
        {
            _config.GlassPure = pure;
            SaveQuiet();
            ApplyGlassToUi();
        };

        SubscribeLiveToggles();
        WireChat();

        // Сессии из лога → карта сервера + Discord + чат.
        _watcher.SessionChanged += (_, s) =>
            _ = Dispatcher.BeginInvoke(new Action(() => OnSessionChanged(s)));

        _serverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _serverTimer.Tick += (_, _) => RefreshServerCard();

        LoadConfig();
        ApplyLanguage(_config.Language, save: false);
        Loaded += (_, _) =>
        {
            RestoreWindowBounds();
            ApplyWallpaper();
            RefreshHomeMeta();
            RestoreVanillaRun(); // если лаунчер убили посреди чистого запуска
            ApplyDiscord();
            UpdateColorFx();
            _music.LoadState();
            CheckForUpdatesQuiet();
            CheckForAppUpdatesQuiet();
            ScheduleBackgroundRobloxUpdate();
            ApplyConfigToUi();
            SyncChat(); // глобальный чат (или сервер, если игра уже идёт)
            RefreshChatHint();
            _ = VerifyStoredInviteAsync(); // свой код ещё жив на сервере?
            RefreshFavorites();
            ApplySoundsFromConfig();
            RefreshAppearanceSettings();
            UiTheme.ApplyText(_config);
            RefreshWallpaperSettings();
            RefreshGlassSettings();
            FooterText.Text = $"v{AppInfo.Version}  •  {Lang.Get("Footer_Ready")}";
            UpdateMaxIcon();
            NavHome.IsChecked = true;
        };
        StateChanged += (_, _) =>
        {
            UpdateMaxIcon();
            if (WindowState == WindowState.Minimized && _config.MinimizeToTray)
                HideToTray();
        };
        GlobalProgress.SizeChanged += (_, _) => UpdateGlobalProgressWidth();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closed = true;
            try { _bgUpdateCts?.Cancel(); } catch { /* ignore */ }
            try { _chatOverlay?.Close(); } catch { /* ignore */ }
            try { CapturePerfPos(); SaveQuiet(); } catch { /* ignore */ }
            try { _perfOverlay?.Close(); } catch { /* ignore */ }
            try { _perfTimer?.Stop(); } catch { /* ignore */ }
            try { _perf.Dispose(); } catch { /* ignore */ }
            try { _chat.Dispose(); } catch { /* ignore */ }
            try { _watcher.Dispose(); } catch { /* ignore */ }
            try { _discord.Dispose(); } catch { /* ignore */ }
            try { _music.Dispose(); } catch { /* ignore */ }
            try { ColorFx.Shutdown(); } catch { /* ignore */ }
            try { _playerProcess?.Dispose(); } catch { /* ignore */ }
            try { _gifPlayer?.Dispose(); } catch { /* ignore */ }
            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
            }
            catch { /* ignore */ }
            _trayIcon = null;
        };
    }

    // ---------- Навигация ----------

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton btn) return;
        if (!_pages.TryGetValue(btn, out var page)) return;
        if (!ReferenceEquals(PageHost.Content, page))
            PageHost.Content = page;
        RefreshPageTitle();
        // Стекло дотягиваем отложенно, после layout свежей страницы:
        // тогда у всех панелей уже есть размеры и семплы встают сразу.
        _ = Dispatcher.BeginInvoke(new Action(() => Glass.NotifySettingsChanged()),
            DispatcherPriority.Render);
        // Обновление данных при входе в раздел (дешёвые локальные чтения).
        RefreshSectionData(btn);
    }

    // ---------- Горячие клавиши ----------

    /// <summary>Ctrl+1..8 — разделы сайдбара, F5 — перезагрузка раздела, Ctrl+K — палитра команд.</summary>
    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.Control)
        {
            if (e.Key == Key.K)
            {
                TogglePalette();
                e.Handled = true;
                return;
            }
            int idx = e.Key switch
            {
                >= Key.D1 and <= Key.D8 => e.Key - Key.D1,
                >= Key.NumPad1 and <= Key.NumPad8 => e.Key - Key.NumPad1,
                _ => -1,
            };
            if (idx >= 0)
            {
                var order = new[]
                {
                    NavHome, NavMods, NavFlags, NavVersions,
                    NavHistory, NavChat, NavSettings, NavAbout,
                };
                if (idx < order.Length)
                {
                    order[idx].IsChecked = true;
                    e.Handled = true;
                }
            }
            return;
        }
        if (e.Key == Key.F5 && mods == ModifierKeys.None)
        {
            var current = _pages.Keys.FirstOrDefault(b =>
                ReferenceEquals(_pages[b], PageHost.Content));
            if (current != null)
            {
                RefreshSectionData(current);
                SetStatus(Lang.Get("Common_Refreshed"), true);
            }
            e.Handled = true;
        }
    }

    /// <summary>Свежие данные раздела: используем и при переходе, и по F5.</summary>
    private void RefreshSectionData(RadioButton btn)
    {
        try
        {
            if (btn == NavHome)
            {
                RefreshHomeMeta();
            }
            else if (btn == NavMods)
            {
                RefreshMods();
                RefreshFleasionStatus();
            }
            else if (btn == NavFlags)
            {
                // Не перечитываем стор, если в таблице есть несохранённое:
                // уход со страницы и возврат больше не топят правки.
                if (!_flagsDirty)
                    _flagsPage.SetFlags(FastFlagStore.Load(EffectiveVersion()));
                RefreshFlagProfiles();
            }
            else if (btn == NavVersions)
            {
                RefreshVersions();
            }
            else if (btn == NavHistory)
            {
                RefreshHistory();
            }
            else if (btn == NavChat)
            {
                RefreshChatHint();
            }
            else if (btn == NavSettings)
            {
                RefreshCdnStatus();
            }
        }
        catch (Exception ex)
        {
            SetStatus(Lang.Get("Upd_SectFail") + " " + ex.Message, false);
        }
    }

    // ================= Command Palette (Ctrl+K) =================

    internal sealed class PaletteItem
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string Keywords { get; init; }
        public required Action Action { get; init; }
    }

    private readonly List<PaletteItem> _paletteItems = new();
    private bool _paletteOpen;

    private void BuildPaletteItems()
    {
        _paletteItems.Clear();

        // Description — локализованная строка (видна пользователю).
        // Keywords — синонимы для поиска, на обоих языках сразу: они не
        // выводятся в UI, поэтому остаются литералами в коде.
        void Add(string titleKey, string descKey, string keywords, Action action,
            params object?[] args) =>
            _paletteItems.Add(new PaletteItem
            {
                Title = Lang.Get(titleKey),
                Description = Lang.Format(descKey, args),
                Keywords = keywords,
                Action = action,
            });

        // Sections
        Add("Nav_Home", "Pal_OpenFmt", "home main start главная", () => NavHome.IsChecked = true, Lang.Get("Nav_Home"));
        Add("Nav_Mods", "Pal_OpenFmt", "mods files profiles моды", () => NavMods.IsChecked = true, Lang.Get("Nav_Mods"));
        Add("Nav_Flags", "Pal_OpenFmt", "flags fastflags fflags флаги", () => NavFlags.IsChecked = true, Lang.Get("Nav_Flags"));
        Add("Nav_Versions", "Pal_OpenFmt", "versions client rollback studio версии", () => NavVersions.IsChecked = true, Lang.Get("Nav_Versions"));
        Add("Nav_History", "Pal_OpenFmt", "history games sessions playtime история", () => NavHistory.IsChecked = true, Lang.Get("Nav_History"));
        Add("Nav_Chat", "Pal_OpenFmt", "chat overlay dm messages чат", () => NavChat.IsChecked = true, Lang.Get("Nav_Chat"));
        Add("Nav_Settings", "Pal_OpenFmt", "settings config options настройки", () => NavSettings.IsChecked = true, Lang.Get("Nav_Settings"));
        Add("Nav_About", "Pal_OpenFmt", "about version info license", () => NavAbout.IsChecked = true, Lang.Get("Nav_About"));

        // Common actions
        #pragma warning disable CS4014
        Add("Btn_Play", "Pal_Play_D", "play launch start game играть", () => PlayFlowAsync());
        Add("Btn_Refresh", "Pal_Refresh_D", "refresh reload update обновить", () =>
        {
            var current = _pages.Keys.FirstOrDefault(b => ReferenceEquals(_pages[b], PageHost.Content));
            if (current != null) RefreshSectionData(current);
        });
        Add("Pal_CheckNow", "Pal_Updates_D", "update check upgrade обновление", () => CheckForAppUpdatesAsync(true));
        #pragma warning restore CS4014
        Add("Btn_SaveSettings", "Pal_SaveSettings_D", "save settings apply сохранить", () =>
        {
            SaveConfigFromUi();
            WarnInvalidSettings();
            Toast(Lang.Get("Set_Saved"), ToastKind.Info);
        });

        // Settings shortcuts
        Add("Set_Discord_L", "Set_Discord_D", "discord rpc presence rich", () =>
            _settingsPage.DiscordRpcCheck.IsChecked = !(_settingsPage.DiscordRpcCheck.IsChecked ?? false));
        Add("Set_Fps_L", "Set_Fps_D", "fps cap limit framerate", () =>
            _settingsPage.FpsCheck.IsChecked = !(_settingsPage.FpsCheck.IsChecked ?? false));
        Add("Set_MinTray_L", "Set_MinTray_D", "tray minimize background трей", () =>
            _settingsPage.MinimizeToTrayCheck.IsChecked = !(_settingsPage.MinimizeToTrayCheck.IsChecked ?? false));
        Add("Set_AutoUpdate_L", "Set_AutoUpdate_D", "auto update automatic автообновление", () =>
            _settingsPage.AutoUpdateCheck.IsChecked = !(_settingsPage.AutoUpdateCheck.IsChecked ?? false));
        Add("Set_PerfTitle", "Set_PerfDesc", "performance overlay stats ping gpu cpu оверлей", () =>
            _settingsPage.PerfOverlayCheck.IsChecked = !(_settingsPage.PerfOverlayCheck.IsChecked ?? false));
        Add("Pal_RobloxFolder", "Pal_RobloxFolder_D", "folder roblox path directory папка", () =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", RobloxPaths.BaseDir); } catch { }
        });
        Add("Pal_ConfigFolder", "Pal_ConfigFolder_D", "config folder settings json конфиг", () =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", RobloxPaths.ConfigPath); } catch { }
        });
        Add("Btn_ResetTheme", "Pal_ResetTheme_D", "reset theme appearance colors fonts оформление", () =>
        {
            ResetAppearance();
            Toast(Lang.Get("Theme_ResetDone"), ToastKind.Info);
        });

        // FastFlags shortcuts
        Add("Btn_Save", "Pal_SaveFlags_D", "save flags fastflags fflags применить", () => SaveFlags());
        Add("Btn_Import", "Pal_ImportFlags_D", "import flags json импорт", () => ImportFlags());
        Add("Btn_Export", "Pal_ExportFlags_D", "export flags json экспорт", () => ExportFlags());
    }

    private void TogglePalette()
    {
        if (_paletteOpen)
        {
            ClosePalette();
        }
        else
        {
            OpenPalette();
        }
    }

    private void OpenPalette()
    {
        if (_paletteOpen) return;
        if (_paletteItems.Count == 0) BuildPaletteItems();
        _paletteOpen = true;
        PaletteHost.Visibility = Visibility.Visible;
        PaletteSearchBox.Text = "";
        PaletteSearchBox.Focus();
        UpdatePaletteResults("");
    }

    private void ClosePalette()
    {
        if (!_paletteOpen) return;
        _paletteOpen = false;
        PaletteHost.Visibility = Visibility.Collapsed;
        PaletteSearchBox.Text = "";
        Keyboard.ClearFocus();
    }

    private void PaletteSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePaletteResults(PaletteSearchBox.Text.Trim());
    }

    private void PaletteSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClosePalette();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ExecuteFirstPaletteResult();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            MovePaletteSelection(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            MovePaletteSelection(-1);
            e.Handled = true;
        }
    }

    private void UpdatePaletteResults(string query)
    {
        PaletteResultsPanel.Children.Clear();
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = _paletteItems
            .Select(item => new { Item = item, Score = CalcScore(item, words) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(12);

        foreach (var match in matches)
        {
            var btn = new Button
            {
                Style = (Style)FindResource("OutlineButton"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 2, 0, 2),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = match.Item.Title, FontWeight = FontWeights.SemiBold, FontSize = 13 },
                        new TextBlock { Text = "  —  " + match.Item.Description, FontSize = 11, Foreground = (Brush)FindResource("FgDimmerBrush"), Margin = new Thickness(8,0,0,0), VerticalAlignment = VerticalAlignment.Center }
                    }
                },
                Tag = match.Item
            };
            btn.Click += (_, _) =>
            {
                match.Item.Action();
                ClosePalette();
            };
            PaletteResultsPanel.Children.Add(btn);
        }

        if (PaletteResultsPanel.Children.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = Lang.Get("Palette_Empty"),
                Style = (Style)FindResource("Mono"),
                FontStyle = FontStyles.Italic,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0)
            };
            PaletteResultsPanel.Children.Add(empty);
        }
    }

    internal static int CalcScore(PaletteItem item, string[] words)
    {
        if (words.Length == 0) return 1;
        int score = 0;
        string haystack = (item.Title + " " + item.Description + " " + item.Keywords).ToLowerInvariant();
        foreach (var w in words)
        {
            string lw = w.ToLowerInvariant();
            if (item.Title.ToLowerInvariant().StartsWith(lw)) score += 10;
            else if (item.Title.ToLowerInvariant().Contains(lw)) score += 5;
            else if (item.Keywords.ToLowerInvariant().Contains(lw)) score += 3;
            else if (haystack.Contains(lw)) score += 1;
            else return 0;
        }
        return score;
    }

    private void ExecuteFirstPaletteResult()
    {
        if (PaletteResultsPanel.Children.Count > 0 && PaletteResultsPanel.Children[0] is Button btn && btn.Tag is PaletteItem item)
        {
            item.Action();
            ClosePalette();
        }
    }

    private void MovePaletteSelection(int delta)
    {
        var buttons = PaletteResultsPanel.Children.OfType<Button>().ToList();
        if (buttons.Count == 0) return;
        int idx = buttons.FindIndex(b => b.IsFocused);
        if (idx < 0) idx = 0;
        idx = Math.Clamp(idx + delta, 0, buttons.Count - 1);
        buttons[idx].Focus();
    }

    private void MinButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MaxButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    // ---------- Тосты, глобальный статус и прогресс ----------

    public enum ToastKind { Info, Warn, Error }

    /// <summary>
    /// Тост поверх контента: не блокирует окно и сам исчезает. Модалки
    /// остались только там, где нужно подтверждение необратимого действия.
    /// </summary>
    private void Toast(string text, ToastKind kind = ToastKind.Info, int ms = 4500)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => Toast(text, kind, ms)));
            return;
        }
        try
        {
            string prefix = kind switch
            {
                ToastKind.Error => "✕  ",
                ToastKind.Warn => "⚠  ",
                _ => "•  "
            };
            var border = new System.Windows.Controls.Border
            {
                Background = TryFindResource("CardBrush") as System.Windows.Media.Brush
                             ?? System.Windows.Media.Brushes.Black,
                BorderBrush = TryFindResource("LineBrush") as System.Windows.Media.Brush
                              ?? System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8),
                MaxWidth = 400,
                Opacity = 0,
                IsHitTestVisible = true
            };
            if (kind == ToastKind.Error)
                border.BorderBrush = TryFindResource("BadBrush") as System.Windows.Media.Brush ?? border.BorderBrush;
            else if (kind == ToastKind.Warn)
                border.BorderBrush = TryFindResource("WarnBrush") as System.Windows.Media.Brush ?? border.BorderBrush;
            border.Child = new TextBlock
            {
                Text = prefix + text,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Foreground = TryFindResource("FgBrush") as System.Windows.Media.Brush
                             ?? System.Windows.Media.Brushes.White
            };
            ToastHost.Children.Add(border);
            while (ToastHost.Children.Count > 4)
                ToastHost.Children.RemoveAt(0);

            var inAnim = new System.Windows.Media.Animation.DoubleAnimation(
                0, 1, TimeSpan.FromMilliseconds(160));
            border.BeginAnimation(OpacityProperty, inAnim);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var outAnim = new System.Windows.Media.Animation.DoubleAnimation(
                    1, 0, TimeSpan.FromMilliseconds(220));
                outAnim.Completed += (_, _) => ToastHost.Children.Remove(border);
                border.BeginAnimation(OpacityProperty, outAnim);
            };
            timer.Start();
        }
        catch { /* тост не должен ронять приложение */ }
    }

    /// <summary>Статус: строка на Главной + полоска в титлбаре (видна отовсюду).</summary>
    private void SetStatus(string text, bool ok)
    {
        _homePage.SetStatus(text, ok);
        try
        {
            GlobalStatusText.Text = text;
            GlobalStatusText.Visibility = string.IsNullOrWhiteSpace(text)
                ? Visibility.Collapsed : Visibility.Visible;
            GlobalStatusText.ToolTip = text;
            GlobalStatusText.Foreground =
                (System.Windows.Media.Brush)FindResource(ok ? "FgDimmerBrush" : "BadBrush");
        }
        catch { /* ignore */ }
    }

    private double _globalProgress = -1;

    /// <summary>Глобальный прогресс 0..1 в титлбаре; null — скрыть.</summary>
    private void SetProgress(double? frac)
    {
        _homePage.SetProgress(frac);
        _globalProgress = frac == null ? -1 : Math.Clamp(frac.Value, 0, 1);
        GlobalProgress.Visibility = _globalProgress < 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateGlobalProgressWidth();
    }

    private void UpdateGlobalProgressWidth()
    {
        if (GlobalProgress == null) return;
        if (_globalProgress < 0 || GlobalProgress.ActualWidth <= 0)
        {
            GlobalProgressFill.Width = 0;
            return;
        }
        GlobalProgressFill.Width = GlobalProgress.ActualWidth * _globalProgress;
    }

    private void UpdateMaxIcon()
    {
        if (MaxIcon == null) return;
        bool max = WindowState == WindowState.Maximized;
        MaxIcon.Data = System.Windows.Media.Geometry.Parse(max ? "M2,2H8V8H2Z M0,0H10V10H0Z" : "M0,0H10V10H0Z");
        MaxIcon.Fill = System.Windows.Media.Brushes.Transparent;
        MaxButton.ToolTip = Lang.Get(max ? "Win_Restore" : "Win_Max");
    }

    // ---------- Несохранённые флаги ----------

    private bool _flagsDirty;

    /// <summary>Флаги в таблице не равны файлу: точка у пункта меню и у «Сохранить».</summary>
    private void SetFlagsDirty(bool dirty)
    {
        _flagsDirty = dirty;
        _flagsPage.SetDirty(dirty);
        try { NavFlags.Content = Lang.Get("Nav_Flags") + (dirty ? " •" : ""); }
        catch { /* ignore */ }
        RefreshPageTitle();
    }

    private void MarkFlagsDirty() => SetFlagsDirty(true);

    private string NavKey(RadioButton btn) =>
        btn == NavHome ? "Nav_Home" :
        btn == NavMods ? "Nav_Mods" :
        btn == NavFlags ? "Nav_Flags" :
        btn == NavVersions ? "Nav_Versions" :
        btn == NavHistory ? "Nav_History" :
        btn == NavChat ? "Nav_Chat" :
        btn == NavSettings ? "Nav_Settings" : "Nav_About";

    /// <summary>Заголовок раздела в титлбаре (плюс маркер несохранённого).</summary>
    private void RefreshPageTitle()
    {
        try
        {
            foreach (var kv in _pages)
            {
                if (!ReferenceEquals(kv.Value, PageHost.Content)) continue;
                PageTitleLabel.Text = Lang.Get(NavKey(kv.Key))
                    + (kv.Key == NavFlags && _flagsDirty ? " •" : "");
                return;
            }
            PageTitleLabel.Text = "";
        }
        catch { /* ignore */ }
    }

    // ---------- Конфиг и обои ----------

    private void LoadConfig()
    {
        _config = LauncherConfig.Load(RobloxPaths.ConfigPath);
        if (!string.IsNullOrWhiteSpace(_config.RobloxPath))
        {
            RobloxPaths.Configure(_config.RobloxPath);
            _config = LauncherConfig.Load(RobloxPaths.ConfigPath);
        }
        _playtime = PlaytimeStore.Load();
        _recent = RecentSessionStore.Load();
        ClickSound.Enabled = _config.Sounds;
    }

    /// <summary>
    /// Обои на всё окно: картинка/GIF + GPU-блюр + затемнение. Нет файла —
    /// сплошной фон. Всё рисует видеокарта — ноль нагрузки на UI-поток
    /// (гифку качает GDI+, каждый кадр — один блит).
    /// </summary>
    private void ApplyWallpaper()
    {
        try { _gifPlayer?.Dispose(); } catch { /* ignore */ }
        _gifPlayer = null;
        _hasWallpaper = false;
        if (!string.IsNullOrWhiteSpace(_config.WallpaperPath) && File.Exists(_config.WallpaperPath))
        {
            try
            {
                if (Path.GetExtension(_config.WallpaperPath).ToLowerInvariant() == ".gif")
                {
                    _gifPlayer = GifPlayer.TryCreate(_config.WallpaperPath, WallpaperImage);
                    _hasWallpaper = _gifPlayer != null;
                    _gifPlayer?.Start();
                }
                else
                {
                    var bmp = DecodeWallpaper(_config.WallpaperPath);
                    if (bmp != null)
                    {
                        WallpaperImage.Source = bmp;
                        _hasWallpaper = true;
                    }
                }
                if (!_hasWallpaper)
                    throw new InvalidOperationException(Lang.Get("Wallpaper_LoadErr"));
            }
            catch
            {
                // Битый файл — чистим путь, чтобы не дёргаться при каждом старте.
                try { _gifPlayer?.Dispose(); } catch { /* ignore */ }
                _gifPlayer = null;
                _config.WallpaperPath = "";
                SaveQuiet();
            }
        }
        UpdateWallpaperBlur();
        UpdateWallpaperDim();
        WallpaperImage.Visibility = _hasWallpaper ? Visibility.Visible : Visibility.Collapsed;
        WallpaperOverlay.Visibility = _hasWallpaper ? Visibility.Visible : Visibility.Collapsed;
        ApplyGlassToUi();
    }

    /// <summary>Затемнение фона 0..100 — прозрачность тёмного оверлея.</summary>
    private void UpdateWallpaperDim()
    {
        WallpaperOverlay.Opacity = Math.Clamp(_config.WallpaperDim, 0, 100) / 100.0;
    }

    // ---------- Геометрия окна ----------

    private void RestoreWindowBounds()
    {
        try
        {
            double vw = SystemParameters.VirtualScreenWidth;
            double vh = SystemParameters.VirtualScreenHeight;
            if (_config.WinW > 0 && _config.WinH > 0)
            {
                Width = Math.Min(_config.WinW, vw);
                Height = Math.Min(_config.WinH, vh);
            }
            if (_config.WinX >= 0 && _config.WinY >= 0
                && _config.WinX < vw && _config.WinY < vh)
            {
                Left = _config.WinX;
                Top = _config.WinY;
            }
            if (_config.WinMax)
                WindowState = WindowState.Maximized;
        }
        catch { /* ignore */ }
    }

    private void SaveWindowBounds()
    {
        try
        {
            _config.WinMax = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                _config.WinX = (int)Left;
                _config.WinY = (int)Top;
                _config.WinW = (int)Width;
                _config.WinH = (int)Height;
            }
            _config.Save(RobloxPaths.ConfigPath);
        }
        catch { /* ignore */ }
    }

    // ================= Бэкенд Главной =================

    private string? EffectiveVersion()
    {
        return RobloxPaths.FindInstalledVersion() ?? _config.InstalledVersion;
    }

    private void RefreshHomeMeta()
    {
        if (_closed) return;
        var mods = ModManager.List();
        var flags = FastFlagStore.Load(EffectiveVersion());
        _homePage.SetCounts(mods.Count, flags.Count,
            PlaytimeStore.Format(_playtime.TotalSeconds,
                Lang.Get("Time_LessMinute"), Lang.Get("Time_Min"), Lang.Get("Time_Hour")));
        string? v = EffectiveVersion();
        _homePage.SetVersionInfo(Lang.Format("Home_LauncherFmt", AppInfo.Version)
            + "   •   Roblox: " + (v ?? Lang.Get("Home_RobloxNone")));
        _homePage.SetLastGame(FormatLastGame(), _config.LastGamePlaceId);
        _homePage.SetAccount(Lang.Get("Home_AccountClient"));
    }

    private string FormatLastGame()
    {
        if (_config.LastGamePlaceId <= 0 && _config.LastGameName.Length == 0) return "";
        string name = _config.LastGameName.Length > 0
            ? _config.LastGameName
            : Lang.Format("Common_PlaceFmt", _config.LastGamePlaceId);
        string id = _config.LastGamePlaceId > 0 ? $"  •  PlaceId {_config.LastGamePlaceId}" : "";
        if (_config.LastGameAt == default) return name + id;
        var ago = DateTime.UtcNow - _config.LastGameAt;
        string when = ago.TotalMinutes < 1 ? Lang.Get("Time_Now")
            : ago.TotalMinutes < 60 ? Lang.Format("Time_MinAgoFmt", (int)ago.TotalMinutes)
            : ago.TotalHours < 24 ? Lang.Format("Time_HourAgoFmt", (int)ago.TotalHours)
            : ago.TotalDays < 7 ? Lang.Format("Time_DayAgoFmt", (int)ago.TotalDays)
            : _config.LastGameAt.ToLocalTime().ToString("d MMM yyyy", Lang.Culture);
        string played = _playtime.ForPlace(_config.LastGamePlaceId) > 0
            ? Lang.Format("Time_PlayedFmt", PlaytimeStore.Format(_playtime.ForPlace(_config.LastGamePlaceId),
                Lang.Get("Time_LessMinute"), Lang.Get("Time_Min"), Lang.Get("Time_Hour")))
            : "";
        return $"{name}{id}  •  {when}{played}";
    }

    /// <summary>
    /// «Играть снова»: тот же плейс через наш exe (ссылку кидаем ему
    /// аргументом). Реестр не трогаем.
    /// </summary>
    private void PlayAgain()
    {
        long placeId = _config.LastGamePlaceId;
        if (placeId <= 0)
        {
            Toast(Lang.Get("Play_Nothing"), ToastKind.Info);
            return;
        }
        LaunchPlaceById(placeId);
    }

    /// <summary>Запуск плейса через наш exe (без реестра).</summary>
    private void LaunchPlaceById(long placeId)
    {
        try
        {
            string? guid = EffectiveVersion();
            string? exe = RobloxLauncher.GetExePath(guid);
            if (exe == null)
            {
                Toast(Lang.Get("Play_NoRoblox"), ToastKind.Warn);
                return;
            }
            _watcher.Clear();
            SetStatus(Lang.Get("Play_OpenPlace"), false);
            if (_vanillaRun)
            {
                // Чистый запуск: снимок состояния + ваниль вместо авто-профиля.
                _vanillaRun = false;
                PrepareVanillaRun();
            }
            else
            {
                ApplyGameBinding(placeId); // авто-профиль игры (моды/флаги)
                ApplyModsNow(); // активный профиль модов — перед стартом
            }
            Process? process = RobloxLauncher.LaunchPlace(exe, placeId);
            if (process == null)
                throw new InvalidOperationException(Lang.Get("Play_LaunchFail"));
            AfterLaunch(process);
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("Play_OpenPlaceErr") + ex.Message, ToastKind.Warn);
        }
    }

    /// <summary>Общее после любого запуска игры: трекинг, моды, Discord, таймеры.</summary>
    private void AfterLaunch(Process process)
    {
        TrackPlayer(process);
        // Приоритет из настройки — до цикла, чтобы тот запомнил его как
        // «свой» и возвращал именно его, когда игра уходит в фон.
        try { ProcessOptimizer.ApplyLaunchPriority(process.Id, _config.RobloxPriority); }
        catch { /* нет прав или процесс уже завершён */ }
        // Оптимизация приоритета/памяти Roblox: старт + фоновый цикл.
        try
        {
            _optimizer?.Dispose();
            _optimizer = new ProcessOptimizer(process.Id);
            _optimizer.Start();
        }
        catch { /* не критично */ }
        FleasionManager.LaunchIfEnabled(_config.FleasionEnabled);
        _discord.SetIdle();
        SetStatus(Lang.Get("Play_Launched"), true);
        _serverTimer.Start();
        RefreshHomeMeta();

        if (_config.CloseOnLaunch && !_config.CloseToTray)
            Close();
    }

    /// <summary>
    /// Строка прогресса установки: фаза и деталь — коды установщика,
    /// тексты собираем по словарю (технические детали — как есть).
    /// </summary>
    private static string FormatInstall(InstallProgress p)
    {
        string phase = p.Phase switch
        {
            "check" => Lang.Get("Prog_PhaseCheck"),
            "done" => Lang.Get("Prog_PhaseDone"),
            "download" => Lang.Get("Prog_PhaseDownload"),
            "extract" => Lang.Get("Prog_PhaseExtract"),
            "setup" => Lang.Get("Prog_PhaseSetup"),
            _ => p.Phase
        };
        string detail = p.Detail switch
        {
            "already" => Lang.Format("Prog_AlreadyFmt", p.Arg),
            "installed" => Lang.Format("Prog_InstalledFmt", p.Arg),
            null => "",
            _ => p.Detail
        };
        return detail.Length > 0 ? $"{phase}: {detail}" : phase;
    }

    private async Task PlayFlowAsync()
    {
        if (_busy) return;
        _busy = true;
        _homePage.SetBusy(true);
        try
        {
            var progress = new Progress<InstallProgress>(p =>
            {
                if (_closed) return;
                SetStatus(FormatInstall(p), false);
                SetProgress(p.Fraction);
            });

            string oldGuid = _config.InstalledVersion;
            await StopBackgroundRobloxUpdateAsync(); // фон не должен качать параллельно
            string guid = await RobloxInstaller.EnsureInstalledAsync(
                _config, progress, CancellationToken.None);
            if (RobloxPaths.IsVersionGuid(oldGuid) && oldGuid != guid)
                _config.PreviousVersion = oldGuid; // для отката
            RobloxInstaller.CleanupExcept(guid, _config.PreviousVersion, _config.StudioVersion);
            _config.Save(RobloxPaths.ConfigPath);

            string? exe = RobloxLauncher.GetExePath(guid);
            if (exe == null)
                throw new InvalidOperationException(Lang.Get("Play_LaunchFail"));

            // FPS-лимит и фиксы Roblox применяются поверх перед стартом.
            FpsManager.Apply(guid, _config.FpsLimit);
            RobloxAppFixes.Apply(_config.RobloxNoTray, _config.RobloxNoStartup);

            SetStatus(Lang.Get("Play_Starting"), false);
            SetProgress(null);
            _watcher.Clear();
            Process? process = RobloxLauncher.Launch(exe);
            if (process == null)
                throw new InvalidOperationException(Lang.Get("Play_LaunchFail"));
            AfterLaunch(process);
        }
        catch (OperationCanceledException)
        {
            SetStatus(Lang.Get("Common_Cancelled"), false);
        }
        catch (Exception ex)
        {
            SetStatus(Lang.Get("Common_Error"), false);
            Toast(Lang.Get("Play_StartErr") + ex.Message, ToastKind.Error);
        }
        finally
        {
            SetProgress(null);
            _homePage.SetBusy(false);
            _busy = false;
        }
    }

    /// <summary>Тихая проверка обновлений при старте (без автоустановки).</summary>
    private void CheckForUpdatesQuiet()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var latest = await Deployment.GetLatestPlayerVersionAsync();
                string? installed = EffectiveVersion();
                if (installed == null || installed != latest.VersionGuid)
                {
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_closed) return;
                        SetStatus(Lang.Get("Upd_RobloxAvail"), false);
                        _homePage.SetVersionInfo(
                            Lang.Format("Home_LauncherFmt", AppInfo.Version)
                            + "   •   Roblox: " + (installed ?? Lang.Get("Home_RobloxNone"))
                            + $" → {latest.VersionGuid}");
                    }));
                }
            }
            catch { /* без сети — молча, статус не трогаем */ }
        });
    }

    // ---------- Фоновое обновление клиента ----------

    private Task? _bgUpdateTask;
    private CancellationTokenSource? _bgUpdateCts;

    /// <summary>
    /// Фоновое обновление Roblox (тумблер в настройках, выключен по умолчанию):
    /// новая версия качается заранее, чтобы «Играть» не ждала загрузку.
    /// Отменяется при старте игры, выключении тумблера и закрытии окна.
    /// </summary>
    private void MaybeStartBackgroundRobloxUpdate()
    {
        if (!_config.RobloxBgUpdate || _closed) return;
        if (_bgUpdateTask is { IsCompleted: false }) return;

        _bgUpdateCts?.Cancel();
        var cts = new CancellationTokenSource();
        _bgUpdateCts = cts;
        var token = cts.Token;
        void Ui(Action a) { try { Dispatcher.BeginInvoke(a); } catch { /* ignore */ } }

        // Progress ловит UI-контекст на момент создания → колбэки на диспетчере.
        var progress = new Progress<InstallProgress>(p =>
        {
            if (_closed || token.IsCancellationRequested) return;
            SetStatus(FormatInstall(p), false);
            SetProgress(p.Fraction);
        });

        _bgUpdateTask = Task.Run(async () =>
        {
            try
            {
                // Уже новое — молчим, статусы не трогаем.
                string? installed = EffectiveVersion();
                var latest = await Deployment.GetLatestPlayerVersionAsync(token);
                token.ThrowIfCancellationRequested();
                if (installed == latest.VersionGuid) return;
                // Игра или своя установка уже идут — фону тут не место.
                if (_busy || PlayerIsRunning(_playerProcess)) return;

                Ui(() =>
                {
                    if (_closed || token.IsCancellationRequested) return;
                    SetStatus(Lang.Get("BgUpd_Start"), false);
                });

                string guid = await RobloxInstaller.EnsureInstalledAsync(_config, progress, token);
                token.ThrowIfCancellationRequested();
                if (guid == installed) return;

                Ui(() =>
                {
                    if (_closed) return;
                    if (RobloxPaths.IsVersionGuid(installed ?? "") && installed != guid)
                        _config.PreviousVersion = installed!; // откат остаётся доступен
                    RobloxInstaller.CleanupExcept(guid, _config.PreviousVersion, _config.StudioVersion);
                    _config.Save(RobloxPaths.ConfigPath);
                    SetProgress(null);
                    RefreshHomeMeta();
                    SetStatus(Lang.Format("BgUpd_DoneFmt", guid), true);
                });
            }
            catch (OperationCanceledException)
            {
                // Игрок нажал «Играть» или выключил тумблер — это норма.
            }
            catch
            {
                // Фоновое обновление не должно ронять старт лаунчера.
            }
            finally
            {
                Ui(() =>
                {
                    if (!_closed) SetProgress(null);
                });
            }
        });
    }

    /// <summary>Стартуем через паузу, чтобы не мешать прогреву UI и тихой проверке.</summary>
    private void ScheduleBackgroundRobloxUpdate()
    {
        if (!_config.RobloxBgUpdate) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            if (_closed) return;
            _ = Dispatcher.BeginInvoke(new Action(MaybeStartBackgroundRobloxUpdate));
        });
    }

    /// <summary>Ждать, пока фоновое обновление остановится (нужно перед своей установкой).</summary>
    private async Task StopBackgroundRobloxUpdateAsync()
    {
        var task = _bgUpdateTask;
        _bgUpdateCts?.Cancel();
        if (task == null || task.IsCompleted) return;
        try { await task; } catch { /* ignore */ }
    }

    // ---------- Поиск игр ----------

    private int _searchSeq;
    private CancellationTokenSource? _searchCts;
    private DispatcherTimer? _searchDebounce;
    private string _pendingQuery = "";

    /// <summary>
    /// Печать в поиске: omni-search режет по 429, поэтому шлём один запрос
    /// только после паузы в наборе, а предыдущий — отменяем.
    /// </summary>
    private void QueueSearch(string query)
    {
        _pendingQuery = query;
        _searchDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _searchDebounce.Stop();
        _searchDebounce.Tick -= SearchDebounce_Tick;
        _searchDebounce.Tick += SearchDebounce_Tick;
        if (query.Length >= 3)
        {
            _homePage.SetSearchStatus(Lang.Get("Search_Debounce"));
            _searchDebounce.Start();
        }
    }

    private void CancelSearchDebounce() => _searchDebounce?.Stop();

    private async void SearchDebounce_Tick(object? sender, EventArgs e)
    {
        CancelSearchDebounce();
        if (_pendingQuery.Length >= 3)
            await SearchGamesAsync(_pendingQuery);
    }

    /// <summary>
    /// Поиск по живому omni-search: новый запрос отменяет старый (сравнение
    /// по поколению — результат устаревшего запроса не перечёркивает свежий),
    /// иконки UniverseId — вторым запросом, без них список уже готов.
    /// </summary>
    private async Task SearchGamesAsync(string query)
    {
        _searchCts?.Cancel();
        // Не Dispose(): предыдущий запрос ещё может держать токен, и поздний
        // Register на уже закрытом источнике дал бы ObjectDisposedException.
        _searchCts = new CancellationTokenSource();
        CancellationToken ct = _searchCts.Token;
        int seq = ++_searchSeq;

        bool Stale() => _closed || ct.IsCancellationRequested || seq != _searchSeq;

        _homePage.SetSearching();
        try
        {
            var hits = await GameSearch.SearchAsync(query, ct);
            if (Stale()) return;
            if (hits.Count == 0)
            {
                _homePage.ShowSearchResults(new List<SearchRow>(), Lang.Get("Search_Empty"));
                return;
            }

            Dictionary<long, string> icons = new();
            try
            {
                icons = await GameSearch.GetIconsAsync(
                    hits.Select(h => h.UniverseId).ToList(), ct);
            }
            catch { /* иконки не критичны */ }
            if (Stale()) return;

            var rows = hits.Select(h => new SearchRow
            {
                PlaceId = h.PlaceId,
                Name = h.Name.Length > 0 ? h.Name : Lang.Format("Common_PlaceFmt", h.PlaceId),
                Meta = SearchMeta(h),
                Icon = icons.TryGetValue(h.UniverseId, out string? icon) ? icon : ""
            }).ToList();
            _homePage.ShowSearchResults(rows, Lang.Format("Search_FoundFmt", rows.Count));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // наша же отмена — молча, свежий запрос покажет своё
        }
        catch (HttpRequestException)
        {
            if (!Stale())
                _homePage.ShowSearchResults(new List<SearchRow>(), Lang.Get("Search_NoNet"));
        }
        catch (Exception ex)
        {
            if (!Stale())
                _homePage.ShowSearchResults(new List<SearchRow>(),
                    Lang.Get("Search_Err") + ex.Message);
        }
    }

    private static string SearchMeta(GameHit h)
    {
        var parts = new List<string>(3);
        if (h.Players > 0) parts.Add(Lang.Format("Search_PlayersFmt", h.Players));
        if (h.Creator.Length > 0) parts.Add(h.Creator);
        parts.Add("PlaceId " + h.PlaceId);
        return string.Join("  •  ", parts);
    }

    // ---------- Трекинг игры: сервер + Discord ----------

    private void TrackPlayer(Process process)
    {
        try { _playerProcess?.Dispose(); } catch { /* ignore */ }
        _playerProcess = process;
        _playerStartedAt = DateTime.UtcNow;
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => _ = Dispatcher.BeginInvoke(new Action(OnPlayerExited));
        }
        catch { /* ignore */ }
    }

    private void OnPlayerExited()
    {
        if (_closed) return;
        try { _optimizer?.Dispose(); } catch { }
        _optimizer = null;
        int exitCode = 0;
        try { exitCode = _playerProcess?.ExitCode ?? 0; } catch { /* ignore */ }
        FlushSessionTime();
        _recent.EndAll();
        _watcher.Clear();
        _ = _chat.DisconnectAsync();
        try { _chatOverlay?.Hide(); } catch { /* ignore */ }
        HidePerfOverlay();
        _homePage.ClearServer();
        _homePage.SetInviteSession(false);
        _lastServerIp = "";
        _lastGeoText = "—";
        _lastPingText = "—";
        _lastGameName = "";
        _discord.SetIdle();
        _serverTimer.Stop();
        SetStatus(Lang.Get("Play_Closed"), true);
        RefreshHomeMeta();
        try { _playerProcess?.Dispose(); } catch { /* ignore */ }
        _playerProcess = null;
        RestoreVanillaRun(); // чистый запуск закончился — возвращаем профили
        // Ненулевой код = клиент упал, а не закрылся (крэш, AV, память).
        if (exitCode != 0) ReportPossibleCrash(exitCode);
    }

    private void BeginSessionTime(long placeId, string name)
    {
        FlushSessionTime();
        _sessionPlaceId = placeId;
        _sessionName = name;
        _sessionStartedAt = DateTime.UtcNow;
    }

    private void FlushSessionTime()
    {
        try
        {
            if (!_config.TrackPlaytime) return;
            if (_sessionPlaceId <= 0 || _sessionStartedAt == default) return;
            long seconds = (long)(DateTime.UtcNow - _sessionStartedAt).TotalSeconds;
            _sessionStartedAt = DateTime.UtcNow;
            if (seconds <= 0) return;
            _playtime.Add(_sessionPlaceId, _sessionName, seconds);
        }
        catch { /* ignore */ }
    }

    private void OnSessionChanged(GameSession session)
    {
        if (_closed) return;
        int seq = ++_sessionSeq;
        SyncChat();
        RefreshChatHint();
        _homePage.SetInviteSession(session.PlaceId > 0);
        TryClaimInvite(session);
        if (_config.ChatEnabled && _config.ChatOverlayOnJoin &&
            session.PlaceId > 0 && session.JobId.Length > 0)
            ShowChatOverlay();
        if (_config.PerfOverlay && session.PlaceId > 0 && session.JobId.Length > 0)
            ShowPerfOverlay();
        BeginSessionTime(session.PlaceId, "");
        try { _recent.Begin(session.PlaceId, session.JobId, session.ServerIp, session.ServerPort); } catch { /* ignore */ }
        _homePage.SetServer(session.ServerIp.Length > 0 ? session.ServerIp : Lang.Get("Server_Searching"),
            Lang.Get("Server_Locating"), "—");
        _ = Task.Run(async () =>
        {
            string? name = await ServerLookup.GetPlaceNameAsync(session.PlaceId);
            var geo = await ServerLookup.GetGeoAsync(session.ServerIp);
            var ping = await ServerLookup.PingGameAsync(session.ServerIp, session.ServerPort);
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closed || seq != _sessionSeq) return;
                if (_watcher.Current == null || _watcher.Current.JobId != session.JobId) return;
                string geoText = geo == null ? "—"
                    : string.Join(", ", new[] { geo.City, geo.Country }
                        .Where(s => s.Length > 0));
                if (geoText.Length == 0) geoText = "—";
                string pingText = ResolvePingText(ping);
                _lastServerIp = session.ServerIp.Length > 0 ? session.ServerIp : "—";
                _lastGeoText = geoText;
                _lastPingText = pingText;
                _lastGameName = name ?? "";
                if (session.PlaceId > 0)
                {
                    _config.LastGameName = name ?? "";
                    _config.LastGamePlaceId = session.PlaceId;
                    _config.LastGameAt = DateTime.UtcNow;
                    SaveQuiet();
                    if (seq == _sessionSeq) _sessionName = name ?? "";
                    try
                    {
                        if (name != null) _recent.SetName(session.JobId, name);
                        _recent.SetServer(session.JobId, session.ServerIp, session.ServerPort);
                    }
                    catch { /* ignore */ }
                }
                _homePage.SetLastGame(FormatLastGame(), session.PlaceId);
                _homePage.SetServer(_lastServerIp, geoText, pingText);
                RefreshHistory();
                if (_config.DiscordRpc)
                {
                    _discord.SetSession(name ?? "", geoText,
                        session.PlaceId, session.JobId, Lang.Get("Discord_OpenGame"));
                    LoadDiscordPlaceIcon(session.PlaceId, name ?? "");
                }
                ShowServerBalloon();
            }));
        });
    }

    /// <summary>Периодическое обновление пинга (и добивка гео), пока игра идёт.</summary>
    private void RefreshServerCard()
    {
        var session = _watcher.Current;
        bool playing = false;
        try { playing = _playerProcess != null && !_playerProcess.HasExited; } catch { /* ignore */ }
        if (!playing || session == null || session.ServerIp.Length == 0)
        {
            _serverTimer.Stop();
            return;
        }
        int seq = _sessionSeq;
        string ip = session.ServerIp;
        int port = session.ServerPort;
        string jobId = session.JobId;
        bool needGeo = _lastGeoText == "—";
        _ = Task.Run(async () =>
        {
            var ping = await ServerLookup.PingGameAsync(ip, port);
            string? geoText = null;
            if (needGeo)
            {
                var geo = await ServerLookup.GetGeoAsync(ip);
                if (geo != null)
                {
                    geoText = string.Join(", ", new[] { geo.City, geo.Country }
                        .Where(s => s.Length > 0));
                    if (geoText.Length == 0) geoText = null;
                }
            }
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closed || seq != _sessionSeq) return;
                var cur = _watcher.Current;
                if (cur == null || cur.JobId != jobId) return;
                if (geoText != null) _lastGeoText = geoText;
                _lastPingText = ResolvePingText(ping);
                _homePage.SetServer(ip, _lastGeoText, _lastPingText);
            }));
        });
    }

    /// <summary>
    /// Текст пинга: свежий замер самого клиента (из лога) важнее всего —
    /// это та же цифра, что Shift+F5. Дальше замер, потом оценка.
    /// </summary>
    private string ResolvePingText(PingResult ping)
    {
        try
        {
            if (_watcher.LastClientPingMs > 0 &&
                (DateTime.UtcNow - _watcher.LastClientPingAt).TotalSeconds < 10)
                return Lang.Format("Ping_MsFmt", _watcher.LastClientPingMs);
        }
        catch { /* ignore */ }
        return ping.Ms >= 0
            ? (ping.Estimated ? "~" : "") + Lang.Format("Ping_MsFmt", ping.Ms)
            : "—";
    }

    // ---------- Оверлей производительности поверх игры ----------

    /// <summary>Показать оверлей: конфиг, позиция, тик раз в секунду.</summary>
    private void ShowPerfOverlay()
    {
        if (_closed) return;
        if (_perfOverlay == null)
        {
            _perfOverlay = new PerfOverlayWindow();
            _perfOverlay.Closed += (_, _) => { _perfOverlay = null; StopPerfTimer(); };
        }
        bool wasVisible = _perfOverlay.IsVisible;
        _perfOverlay.ApplyStyle(_config.PerfOverlayGlass, _config.PerfOverlayOpacity);
        _perfOverlay.RefreshLabels();
        ApplyPerfRows();
        if (!wasVisible)
        {
            // Сохранённая позиция; иначе — правый верх (рядом с часами).
            if (_config.PerfOverlayX >= 0 && _config.PerfOverlayY >= 0)
            {
                _perfOverlay.Left = _config.PerfOverlayX;
                _perfOverlay.Top = _config.PerfOverlayY;
            }
            else
            {
                _perfOverlay.Left = SystemParameters.VirtualScreenLeft
                    + SystemParameters.VirtualScreenWidth - 210;
                _perfOverlay.Top = SystemParameters.VirtualScreenTop + 70;
            }
        }
        _perfOverlay.Show();
        StartPerfTimer();
        PerfTick();
    }

    /// <summary>Скрыть оверлей (позиция запоминается) и остановить тик.</summary>
    private void HidePerfOverlay()
    {
        StopPerfTimer();
        var ov = _perfOverlay;
        if (ov == null) return;
        if (ov.IsVisible)
        {
            CapturePerfPos();
            SaveQuiet();
        }
        ov.Hide();
        _perf.Update(null); // сброс CPU-базы/GPU-запроса до следующего старта
    }

    /// <summary>Запомнить позицию окна в конфиге (без записи на диск).</summary>
    private void CapturePerfPos()
    {
        var ov = _perfOverlay;
        if (ov == null || !ov.IsVisible) return;
        _config.PerfOverlayX = (int)ov.Left;
        _config.PerfOverlayY = (int)ov.Top;
    }

    /// <summary>Какие строки показывать — прямо из конфига, сразу.</summary>
    private void ApplyPerfRows() => _perfOverlay?.ApplyRows(
        _config.PerfShowPing, _config.PerfShowCpu, _config.PerfShowRam,
        _config.PerfShowGpu, _config.PerfShowServer, _config.PerfShowTime);

    private void StartPerfTimer()
    {
        if (_perfTimer == null)
        {
            _perfTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _perfTimer.Tick += (_, _) => PerfTick();
        }
        if (!_perfTimer.IsEnabled)
            _perfTimer.Start();
    }

    private void StopPerfTimer()
    {
        try { _perfTimer?.Stop(); } catch { /* ignore */ }
    }

    /// <summary>Секундный тик: значения из готовых источников лаунчера.</summary>
    private void PerfTick()
    {
        var ov = _perfOverlay;
        if (ov == null || !ov.IsVisible)
        {
            // Крестик прячет окно без Closed — тик гасим здесь, иначе
            // таймер крутится вхолостую до выхода из игры.
            StopPerfTimer();
            return;
        }
        try
        {
            var p = _playerProcess;
            try { if (p != null && p.HasExited) p = null; } catch { p = null; }
            _perf.Update(p);

            // Пинг: свежий клиентский замер (тот же, что Shift+F5), иначе
            // подсказка с главной (ICMP-оценка).
            int ping = -1;
            try
            {
                if (_watcher.LastClientPingMs > 0 &&
                    (DateTime.UtcNow - _watcher.LastClientPingAt).TotalSeconds < 10)
                    ping = _watcher.LastClientPingMs;
            }
            catch { /* ignore */ }

            string server = _lastServerIp == "—" ? "" : _lastServerIp;

            string time = "";
            try
            {
                if (_playerStartedAt != default)
                {
                    var up = DateTime.UtcNow - _playerStartedAt;
                    time = (int)up.TotalHours > 0
                        ? $"{(int)up.TotalHours}:{up.Minutes:00}:{up.Seconds:00}"
                        : $"{up.Minutes:00}:{up.Seconds:00}";
                }
            }
            catch { /* ignore */ }

            ov.SetValues(ping, _lastPingText, _perf.CpuPercent, _perf.RamMb,
                _perf.GpuPercent, server, time);
        }
        catch { /* ignore */ }
    }

    private void ApplyDiscord()
    {
        if (_config.DiscordRpc)
            _discord.Configure(LauncherConfig.DefaultDiscordAppId, new DiscordOptions
            {
                AllowJoin = _config.DiscordAllowJoin,
                ShowName = _config.DiscordShowName,
                ShowServer = _config.DiscordShowServer,
                ShowIcon = _config.DiscordShowIcon,
                ShowElapsed = _config.DiscordShowElapsed,
                ShowButton = _config.DiscordShowButton,
                AssetKey = _config.DiscordAssetKey
            });
        else
            _discord.Shutdown();
    }

    /// <summary>
    /// Картинка плейса в присутствии: официальный thumbnails API отдаёт URL,
    /// Discord их принимает. Ошибка сети молча остаётся без картинки — в этом
    /// случае работает ключ из настроек.
    /// </summary>
    private void LoadDiscordPlaceIcon(long placeId, string gameName)
    {
        if (!_config.DiscordRpc || placeId <= 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                if (!_placeIconCache.TryGetValue(placeId, out string? url))
                {
                    url = await Deployment.GetPlaceIconAsync(placeId, CancellationToken.None);
                    if (url.Length > 0) _placeIconCache[placeId] = url;
                }
                if (url!.Length == 0 || _closed) return;
                string text = gameName.Length > 0 ? gameName : "Roblox";
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_closed || !_config.DiscordRpc) return;
                    _discord.UpdateLargeAsset(url, text);
                }));
            }
            catch { /* без сети — остаётся картинка из настроек */ }
        });
    }

    private void SaveQuiet()
    {
        try { _config.Save(RobloxPaths.ConfigPath); } catch { /* ignore */ }
    }

    // ================= Внешний вид =================

    /// <summary>Прочитать карточку в конфиг и применить живьём.</summary>
    private void ApplyAppearanceFromUi(bool save)
    {
        _config.ThemeFontHeading = _settingsPage.HeadingFontName;
        _config.ThemeFontBody = _settingsPage.BodyFontName;
        _config.ThemeFontMono = _settingsPage.MonoFontName;
        _config.ThemeFg = NormHex(_settingsPage.FgHex);
        _config.ThemeDim = NormHex(_settingsPage.DimHex);
        _config.ThemeDimmer = NormHex(_settingsPage.DimmerHex);
        if (save) SaveQuiet();
        UiTheme.ApplyText(_config);
    }

    /// <summary>Пусто или мусор → пусто (дефолт палитры). Иначе #RRGGBB.</summary>
    private static string NormHex(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6 && hex.All(c =>
                (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            return "#" + hex.ToUpperInvariant();
        return "";
    }

    private void RefreshAppearanceSettings()
    {
        _settingsPage.SetAppearance(
            PickFont(_config.ThemeFontHeading, SettingsPage.HeadingFonts),
            PickFont(_config.ThemeFontBody, SettingsPage.BodyFonts),
            PickFont(_config.ThemeFontMono, SettingsPage.MonoFonts),
            _config.ThemeFg, _config.ThemeDim, _config.ThemeDimmer);
    }

    private static string PickFont(string want, string[] known)
    {
        foreach (var k in known)
            if (k.Equals(want, StringComparison.OrdinalIgnoreCase))
                return k;
        return known[0];
    }

    private void ResetAppearance()
    {
        _config.ThemeFontHeading = SettingsPage.HeadingFonts[0];
        _config.ThemeFontBody = SettingsPage.BodyFonts[0];
        _config.ThemeFontMono = SettingsPage.MonoFonts[0];
        _config.ThemeFg = "";
        _config.ThemeDim = "";
        _config.ThemeDimmer = "";
        SaveQuiet();
        RefreshAppearanceSettings();
        UiTheme.ApplyText(_config);
        SetStatus(Lang.Get("Theme_ResetDone"), true);
    }

    // ================= Язык =================

    /// <summary>
    /// Живое переключение языка: словари через {L} обновляются сами,
    /// построенные кодом ряды/кнопки/таблицы пересоздаём и обновляем здесь.
    /// </summary>
    private void ApplyLanguage(string choice, bool save)
    {
        if (choice != "ru" && choice != "en" && choice != "auto")
            choice = "auto";
        Lang.Set(Lang.Resolve(choice));
        if (save)
        {
            _config.Language = choice;
            SaveQuiet();
        }
        _settingsPage.SetLanguage(choice);
        _settingsPage.RefreshSoundRows();
        _settingsPage.RefreshColorRows();
        _settingsPage.RefreshPriorityBox();
        _paletteItems.Clear(); // заголовки палитры собраны на текущем языке
        _flagsPage.RefreshPresetButtons();
        RefreshSoundsSettings();
        RefreshAppearanceSettings();
        RefreshFavorites();
        RefreshHomeMeta();
        RefreshMods();
        RefreshFlagProfiles();
        RefreshVersions();
        RefreshHistory();
        RefreshCdnStatus();
        RefreshFleasionStatus();
        _chatPage.RefreshLabels();
        _homePage.RefreshInviteLabels();
        _perfOverlay?.RefreshLabels();
        _chatPage.SetState(_chat.State);
        RefreshChatHint();
        FooterText.Text = $"v{AppInfo.Version}  •  {Lang.Get("Footer_Ready")}";
        InitTray();
        RefreshPageTitle();
        SetFlagsDirty(_flagsDirty); // перерисовать маркер на новом языке
    }

    // ================= Кастомные звуки =================

    /// <summary>Применить сохранённые wav и громкости при старте.</summary>
    private void ApplySoundsFromConfig()
    {
        ClickSound.LoadFromConfig(_config.CustomSounds, _config.SoundVolumes);
        RefreshSoundsSettings();
    }

    private void RefreshSoundsSettings()
    {
        foreach (var (key, _) in ClickSound.Sounds)
        {
            string? path = ClickSound.GetCustomPath(key);
            _settingsPage.SetSoundFile(key,
                path != null ? Path.GetFileName(path) : "");
            _settingsPage.SetSoundVolume(key,
                (int)Math.Round(ClickSound.GetSoundVolume(key) * 100));
        }
    }

    private void PickCustomSound(string key)
    {
        string title = ClickSound.Sounds.FirstOrDefault(s => s.Key == key).Title ?? key;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Lang.Format("Dlg_SoundFmt", title),
            Filter = "WAV (*.wav)|*.wav|" + Lang.Get("Dlg_AllFiles") + " (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        if (ClickSound.TrySetCustom(key, dlg.FileName, out string error))
        {
            _config.CustomSounds[key] = dlg.FileName;
            SaveQuiet();
            RefreshSoundsSettings();
            SetStatus(Lang.Format("Snd_SetFmt", title), true);
        }
        else
        {
            Toast(Lang.Format("Snd_Err", error), ToastKind.Warn);
        }
    }

    private void ResetCustomSound(string key)
    {
        ClickSound.ClearCustom(key);
        _config.CustomSounds.Remove(key);
        SaveQuiet();
        RefreshSoundsSettings();
    }

    // ---------- Цветокоррекция ----------

    private ColorSettings CurrentColorSettings()
    {
        return new ColorSettings(
            Math.Clamp(_config.ColorFxSat, 0f, 2f),
            Math.Clamp(_config.ColorFxBri, -0.5f, 0.5f),
            Math.Clamp(_config.ColorFxCon, 0f, 2f),
            Math.Clamp(_config.ColorFxTemp, -1f, 1f));
    }

    /// <summary>
    /// Применяет/снимает эффект: в авто-режиме следит за окном игры,
    /// иначе — всегда. Пока открыт тюнер — эффект держим всегда,
    /// иначе не с чем сравнивать.
    /// </summary>
    private void UpdateColorFx()
    {
        try
        {
            if (!_config.ColorFxEnabled)
            {
                if (_colorTimer != null) _colorTimer.Stop();
                ColorFx.Reset();
                return;
            }
            if (_tuningOpen || !_config.ColorFxAuto)
            {
                // Тюнер открыт — эффект держим всегда, иначе не сравнить с игрой.
                if (_colorTimer != null) _colorTimer.Stop();
                ColorFx.Apply(CurrentColorSettings());
                return;
            }
            _colorTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _colorTimer.Tick -= ColorTimer_Tick;
            _colorTimer.Tick += ColorTimer_Tick;
            _colorTimer.Start();
            ColorTimer_Tick(this, EventArgs.Empty);
        }
        catch { /* ignore */ }
    }

    private void ColorTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (!_config.ColorFxEnabled) return;
            ColorFx.Apply(_tuningOpen || IsRobloxForeground()
                ? CurrentColorSettings()
                : ColorSettings.Default);
        }
        catch { /* ignore */ }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private static bool IsRobloxForeground()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            string n = p.ProcessName;
            return n.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase) ||
                   n.Equals("RobloxStudioBeta", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // ================= Моды =================

    private void RefreshMods()
    {
        var mods = ModManager.List();
        _modsPage.SetMods(mods);
        _modsPage.SetProfiles(ModManager.Profiles(), ModManager.ActiveProfile(), mods.Count);
        _modsPage.BuildQuickRows();
        RefreshGameBindings();
    }

    /// <summary>
    /// Откат прошлых правок + наложение активного профиля на установленную
    /// версию. Возвращает число применённых файлов (0 = нечего/нет версии).
    /// </summary>
    private int ApplyModsNow()
    {
        try
        {
            string? guid = EffectiveVersion();
            if (guid == null) return 0;
            return ModManager.ApplyTo(Path.Combine(RobloxPaths.VersionsDir, guid));
        }
        catch { return 0; }
    }

    private static bool PlayerIsRunning(Process? process)
    {
        try { return process != null && !process.HasExited; }
        catch { return false; }
    }

    /// <summary>Правки из UI: пока клиент запущен, его файлы не трогаем.</summary>
    private int ApplyModsIfIdle() =>
        PlayerIsRunning(_playerProcess) ? 0 : ApplyModsNow();

    // ---- Авто-профили на игру ----

    /// <summary>
    /// Перед запуском плейса подставляем привязанные ему профили: моды
    /// (переключением активного профиля, дальше их накладывает ApplyModsNow)
    /// и флаги (пишутся в ClientAppSettings сразу, без вопросов).
    /// </summary>
    private void ApplyGameBinding(long placeId)
    {
        try
        {
            var b = GameProfiles.Get(placeId);
            if (b == null) return;
            bool changed = false;

            if (b.ModProfile.Length > 0 &&
                !string.Equals(b.ModProfile, ModManager.ActiveProfile(), StringComparison.OrdinalIgnoreCase) &&
                ModManager.Profiles().Contains(b.ModProfile, StringComparer.OrdinalIgnoreCase) &&
                ModManager.SetActive(b.ModProfile))
            {
                RefreshMods();
                changed = true;
            }

            if (b.FlagProfile.Length > 0 &&
                !string.Equals(b.FlagProfile, _config.ActiveFlagProfile, StringComparison.OrdinalIgnoreCase) &&
                FlagProfiles.Get(b.FlagProfile) != null &&
                ForceFlagProfile(b.FlagProfile))
            {
                RefreshFlagProfiles();
                changed = true;
            }

            if (changed)
                SetStatus(Lang.Format("AutoP_AppliedFmt", GameTitle(b)), false);
        }
        catch { /* авто-профиль не должен ломать запуск */ }
    }

    /// <summary>Ставит профиль флагов в таблицу и сразу пишет его в клиент.</summary>
    private bool ForceFlagProfile(string name)
    {
        var flags = FlagProfiles.Get(name);
        if (flags == null) return false;
        _flagsPage.SetFlags(new Dictionary<string, string>(flags));
        FastFlagStore.Save(EffectiveVersion(), flags);
        if (!string.Equals(_config.ActiveFlagProfile, name, StringComparison.OrdinalIgnoreCase))
        {
            _config.ActiveFlagProfile = name;
            _config.Save(RobloxPaths.ConfigPath);
        }
        SetFlagsDirty(false);
        return true;
    }

    /// <summary>Привязать активный профиль к выбранной игре (п.3: не только к последней).</summary>
    private void BindProfileToGame(bool mod)
    {
        string value = mod ? ModManager.ActiveProfile() : _config.ActiveFlagProfile;
        if (value.Length == 0)
        {
            SetStatus(Lang.Get("AutoP_NoActive"), false);
            return;
        }

        var known = KnownGames();
        var dlg = new PickDialog(Lang.Get("Dlg_PickGame_T"), Lang.Get("Dlg_PickGame_H"),
            known.Select(g => new PickItem(GameLabel(g.placeId, g.name), g.placeId.ToString())),
            allowManual: true);
        if (dlg.ShowDialog() != true || dlg.Picked == null) return;

        if (!long.TryParse(dlg.Picked.Value, out long placeId) || placeId <= 0)
        {
            SetStatus(Lang.Format("AutoP_BadIdFmt", dlg.Picked.Value), false);
            return;
        }

        string gameName = "";
        foreach (var g in known)
            if (g.placeId == placeId) { gameName = g.name; break; }

        var b = GameProfiles.Get(placeId) ?? new GameBinding { PlaceId = placeId };
        if (b.GameName.Length == 0) b.GameName = gameName;
        if (mod) b.ModProfile = value; else b.FlagProfile = value;
        GameProfiles.Set(b);

        if (mod) _modsPage.SetBindStatus(BindStatus(b, mod: true));
        else _flagsPage.SetBindStatus(BindStatus(b, mod: false));
        SetStatus(Lang.Format("AutoP_BoundFmt", value, GameTitle(b)), true);
    }

    /// <summary>Снять привязку профиля у выбранной игры — список только привязанных.</summary>
    private void UnbindProfileFromGame(bool mod)
    {
        var bound = GameProfiles.List()
            .Where(b => (mod ? b.ModProfile : b.FlagProfile).Length > 0)
            .OrderBy(b => b.GameName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (bound.Count == 0)
        {
            SetStatus(Lang.Get("AutoP_NoBoundGames"), false);
            return;
        }

        var dlg = new PickDialog(Lang.Get("Dlg_PickUnbind_T"), Lang.Get("Dlg_PickUnbind_H"),
            bound.Select(b => new PickItem(GameLabel(b.PlaceId, b.GameName), b.PlaceId.ToString())));
        if (dlg.ShowDialog() != true || dlg.Picked == null) return;
        if (!long.TryParse(dlg.Picked.Value, out long placeId) || placeId <= 0) return;

        var b = GameProfiles.Get(placeId);
        if (b == null) return;
        if (mod) b.ModProfile = ""; else b.FlagProfile = "";
        GameProfiles.Set(b); // пустое поле = запись удаляется

        if (mod) _modsPage.SetBindStatus(BindStatus(GameProfiles.Get(placeId), mod: true));
        else _flagsPage.SetBindStatus(BindStatus(GameProfiles.Get(placeId), mod: false));
        SetStatus(Lang.Get("AutoP_Unbound"), true);
    }

    /// <summary>Все известные игры (история, избранное, сессии) — для диалога выбора.</summary>
    private List<(long placeId, string name, DateTime last)> KnownGames()
    {
        var map = new Dictionary<long, (string name, DateTime last)>();
        void Put(long id, string name, DateTime last)
        {
            if (id <= 0) return;
            if (!map.TryGetValue(id, out var cur)) cur = ("", DateTime.MinValue);
            string n = name.Trim().Length > 0 ? name.Trim() : cur.name;
            map[id] = (n, last > cur.last ? last : cur.last);
        }

        foreach (var kv in _playtime.Games)
            if (long.TryParse(kv.Key, out long id)) Put(id, kv.Value.Name, kv.Value.LastPlayed);
        foreach (var f in FavoritesStore.Load())
            Put(f.PlaceId, f.Name, f.AddedAt);
        foreach (var s in _recent.List())
            Put(s.PlaceId, s.Name, s.JoinedAt);
        Put(_config.LastGamePlaceId, _config.LastGameName, DateTime.UtcNow);

        return map
            .Select(kv => (placeId: kv.Key, name: kv.Value.name, last: kv.Value.last))
            .OrderByDescending(g => g.last)
            .ThenBy(g => g.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GameLabel(long placeId, string name) =>
        name.Length > 0 ? $"{name}  ·  {placeId}" : placeId.ToString();

    /// <summary>Статус привязок на обеих страницах (по последней игре).</summary>
    private void RefreshGameBindings()
    {
        GameBinding? b = _config.LastGamePlaceId > 0
            ? GameProfiles.Get(_config.LastGamePlaceId) : null;
        _modsPage.SetBindStatus(BindStatus(b, mod: true));
        _flagsPage.SetBindStatus(BindStatus(b, mod: false));
    }

    private static string BindStatus(GameBinding? b, bool mod)
    {
        if (b == null) return Lang.Get("AutoP_NoBind");
        string value = mod ? b.ModProfile : b.FlagProfile;
        return value.Length == 0
            ? Lang.Get("AutoP_NoBind")
            : Lang.Format("AutoP_ForGameFmt", value, GameTitle(b));
    }

    private static string GameTitle(GameBinding b) =>
        b.GameName.Length > 0 ? b.GameName : b.PlaceId.ToString();

    // ---- Экспорт/импорт профиля модов (zip) ----

    private void ExportModProfile()
    {
        string name = ModManager.ActiveProfile();
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = Lang.Get("Dlg_ProfileExport"),
            FileName = name + ".zip",
            Filter = $"ZIP (*.zip)|*.zip|{Lang.Get("Dlg_AllFiles")} (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            ModManager.ExportProfile(name, dlg.FileName);
            SetStatus(Lang.Format("ModP_ExportedFmt", name), true);
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("ModP_ExportErr") + ex.Message, ToastKind.Warn);
        }
    }

    private void ImportModProfile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Lang.Get("Dlg_ProfileImport"),
            Filter = $"ZIP (*.zip)|*.zip|{Lang.Get("Dlg_AllFiles")} (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            string name = ModManager.ImportProfile(dlg.FileName);
            RefreshMods();
            SetStatus(Lang.Format("ModP_ImportedFmt", name), true);
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("ModP_ImportErr") + ex.Message, ToastKind.Warn);
        }
    }

    // ---- Чистый запуск и диагностика крашей ----

    /// <summary>Следующий запуск — ванильный (без модов и без флагов).</summary>
    private bool _vanillaRun;

    private void StartCleanLaunch()
    {
        long placeId = _config.LastGamePlaceId;
        if (placeId <= 0)
        {
            Toast(Lang.Get("Play_Nothing"), ToastKind.Info);
            return;
        }
        if (PlayerIsRunning(_playerProcess))
        {
            SetStatus(Lang.Get("Play_CleanBusy"), false);
            return;
        }
        _vanillaRun = true;
        LaunchPlaceById(placeId);
    }

    /// <summary>Снимок «до» + клиент в ваниль — перед чистым стартом.</summary>
    private void PrepareVanillaRun()
    {
        try
        {
            string? guid = EffectiveVersion();
            if (guid != null)
            {
                VanillaStore.Save(new VanillaState
                {
                    VersionGuid = guid,
                    ModProfile = ModManager.ActiveProfile(),
                    FlagProfile = _config.ActiveFlagProfile,
                    Flags = FastFlagStore.Load(guid)
                });
                FastFlagStore.Save(guid, new Dictionary<string, string>());
                ModManager.RevertTo(Path.Combine(RobloxPaths.VersionsDir, guid));
            }
            RefreshMods();
            SetStatus(Lang.Get("Play_CleanStarting"), false);
        }
        catch { /* не собралось — запускаем как есть */ }
    }

    /// <summary>Вернуть состояние до чистого запуска (выход из игры / старт лаунчера).</summary>
    private void RestoreVanillaRun()
    {
        var s = VanillaStore.Load();
        if (s == null) return;
        try
        {
            if (s.VersionGuid.Length > 0)
            {
                FastFlagStore.Save(s.VersionGuid, s.Flags ?? new Dictionary<string, string>());
                if (s.ModProfile.Length > 0 &&
                    ModManager.Profiles().Contains(s.ModProfile, StringComparer.OrdinalIgnoreCase))
                    ModManager.SetActive(s.ModProfile);
                ModManager.ApplyTo(Path.Combine(RobloxPaths.VersionsDir, s.VersionGuid));
            }
            if (s.FlagProfile.Length > 0) _config.ActiveFlagProfile = s.FlagProfile;
            _config.Save(RobloxPaths.ConfigPath);
            VanillaStore.Clear();
            RefreshMods();
            RefreshFlagProfiles();
            SetStatus(Lang.Get("Play_CleanRestored"), true);
        }
        catch { /* не вышло — снимок дождётся следующего старта */ }
    }

    /// <summary>Клиент умер с ненулевым кодом — предлагаем чистый запуск.</summary>
    private void ReportPossibleCrash(int exitCode)
    {
        SetStatus(Lang.Format("Crash_Fmt", exitCode), false);
        var answer = MessageBox.Show(Lang.Format("Crash_AskFmt", exitCode), "NekoStrap",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes) StartCleanLaunch();
    }

    // ---- Профили модов ----

    private void SwitchModProfile(string name)
    {
        try
        {
            if (string.Equals(name, ModManager.ActiveProfile(), StringComparison.OrdinalIgnoreCase))
                return;
            if (!ModManager.SetActive(name)) return;
            bool running = PlayerIsRunning(_playerProcess);
            int applied = ApplyModsIfIdle();
            RefreshMods();
            RefreshHomeMeta();
            SetStatus(running
                ? Lang.Format("ModP_SwitchedLaterFmt", ModManager.ActiveProfile())
                : Lang.Format("ModP_SwitchedFmt", ModManager.ActiveProfile(), applied), true);
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("ModP_SwitchErr") + ex.Message, ToastKind.Warn);
        }
    }

    /// <summary>Создание профиля; copyActive = дубликат текущего со всеми файлами.</summary>
    private void CreateModProfile(bool copyActive)
    {
        string name = _modsPage.ProfileNameText;
        if (!ModManager.ValidName(name))
        {
            Toast(Lang.Get("ModP_NeedName") + "\n" + Lang.Get("ModP_BadName"), ToastKind.Info);
            return;
        }
        name = name.Trim();
        try
        {
            if (!ModManager.Create(name, copyActive))
            {
                Toast(Lang.Format("ModP_ExistsFmt", name), ToastKind.Info);
                return;
            }
            _modsPage.ClearProfileName();
            ModManager.SetActive(name);
            ApplyModsIfIdle();
            RefreshMods();
            RefreshHomeMeta();
            SetStatus(Lang.Format(copyActive ? "ModP_DupFmt" : "ModP_CreatedFmt", name), true);
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("ModP_Err") + ex.Message, ToastKind.Warn);
        }
    }

    private void DeleteModProfile()
    {
        string name = ModManager.ActiveProfile();
        if (ModManager.Profiles().Count <= 1)
        {
            Toast(Lang.Get("ModP_Last"), ToastKind.Info);
            return;
        }
        if (MessageBox.Show(Lang.Format("ModP_DelConfirm", name),
                "NekoStrap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            if (!ModManager.Delete(name)) return;
            ApplyModsIfIdle();
            RefreshMods();
            RefreshHomeMeta();
            SetStatus(Lang.Format("ModP_DeletedFmt", name), true);
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("ModP_Err") + ex.Message, ToastKind.Warn);
        }
    }

    private void OpenModProfileFolder()
    {
        try { ModManager.OpenActiveFolder(); } catch { /* ignore */ }
    }

    // ---- Быстрая замена ----

    private static string QuickFilter(string kind) => kind switch
    {
        "audio" => $"{Lang.Get("Dlg_AudioFiles")} (*.ogg;*.wav;*.mp3)|*.ogg;*.wav;*.mp3",
        "font" => $"{Lang.Get("Dlg_FontFiles")} (*.ttf;*.otf)|*.ttf;*.otf",
        "image" => $"{Lang.Get("Dlg_ImageFiles")} (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp",
        "tex" => $"{Lang.Get("Dlg_TexFiles")} (*.tex)|*.tex",
        _ => $"{Lang.Get("Dlg_AllFiles")} (*.*)|*.*"
    };

    private void PickQuickAsset(string id)
    {
        var slot = QuickAssets.Get(id);
        if (slot == null) return;
        if (slot.Target.Length == 0)
        {
            Toast(Lang.Format("Quick_NoTarget", Lang.Get(slot.TitleKey)), ToastKind.Info);
            return;
        }
        bool folder = QuickAssets.IsFolderTarget(slot.Target);
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Lang.Get(slot.TitleKey),
            Multiselect = folder,
            Filter = QuickFilter(slot.Filter)
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            int n = QuickAssets.Install(id, dlg.FileNames);
            if (n == 0) throw new InvalidOperationException(Lang.Get("Quick_NoFiles"));
            ApplyModsIfIdle();
            RefreshMods();
            RefreshHomeMeta();
            SetStatus(Lang.Format("Quick_InstalledFmt", Lang.Get(slot.TitleKey), n), true);
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("Quick_Err") + ex.Message, ToastKind.Warn);
        }
    }

    private void ResetQuickAsset(string id)
    {
        var slot = QuickAssets.Get(id);
        if (slot == null) return;
        try
        {
            if (!QuickAssets.Reset(id))
            {
                Dispatcher.BeginInvoke(new Action(RefreshMods), DispatcherPriority.Background);
                return;
            }
            ApplyModsIfIdle();
            RefreshMods();
            RefreshHomeMeta();
            SetStatus(Lang.Format("Quick_ResetFmt", Lang.Get(slot.TitleKey)), true);
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("Quick_Err") + ex.Message, ToastKind.Warn);
        }
    }

    private void EditQuickTarget(string id, string path)
    {
        var slot = QuickAssets.Get(id);
        if (slot == null) return;
        if (!QuickAssets.TrySetTarget(id, path))
        {
            Toast(Lang.Get("Quick_BadPath"), ToastKind.Info);
        }
        // Перестраиваем строки ПОСЛЕ события (фокус ещё в TextBox) — иначе
        // контрол выдернется из дерева посреди собственного LostFocus.
        Dispatcher.BeginInvoke(new Action(RefreshMods), DispatcherPriority.Background);
    }

    private void AddMods()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Lang.Get("Dlg_ModsTitle"),
            Multiselect = true,
            Filter = $"{Lang.Get("Dlg_AllFiles")} (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            ModManager.AddFiles(dlg.FileNames);
            ApplyModsIfIdle();
            RefreshMods();
            RefreshHomeMeta();
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("Mod_AddErr") + ex.Message, ToastKind.Warn);
        }
    }

    private void ToggleSelectedMods()
    {
        var selected = _modsPage.SelectedModPaths();
        if (selected.Count == 0)
        {
            Toast(Lang.Get("Mod_SelOne"), ToastKind.Info);
            return;
        }
        try
        {
            foreach (var rel in selected)
                ModManager.Toggle(rel);
            ApplyModsIfIdle();
            RefreshMods();
            RefreshHomeMeta();
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("Mod_ToggleErr") + ex.Message, ToastKind.Warn);
        }
    }

    private void DeleteSelectedMods()
    {
        var selected = _modsPage.SelectedModPaths();
        if (selected.Count == 0)
        {
            Toast(Lang.Get("Mod_SelOne"), ToastKind.Info);
            return;
        }
        if (MessageBox.Show(Lang.Format("Mod_DelConfirm", selected.Count),
                "NekoStrap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            // Удаление идёт через ModManager: путь нормализуется и
            // проверяется на выход за пределы папки активного профиля.
            ModManager.DeleteFiles(selected);
            ApplyModsIfIdle();
            RefreshMods();
            RefreshHomeMeta();
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("Common_DelErr") + ex.Message, ToastKind.Warn);
        }
    }

    private void RefreshFleasionStatus()
    {
        _modsPage.SetFleasionStatus(FleasionManager.IsInstalled()
            ? (FleasionManager.IsRunning() ? Lang.Get("Ver_InstalledRunning") : Lang.Get("Ver_Installed"))
            : Lang.Get("Ver_NotInstalledDl"));
    }

    private async Task DownloadFleasionAsync()
    {
        _modsPage.SetFleasionStatus(Lang.Get("Mod_FleaDl"));
        try
        {
            var progress = new Progress<(long done, long total)>(t =>
            {
                if (_closed) return;
                string detail = t.total > 0
                    ? Lang.Format("Size_MbFmt", t.done / 1048576.0) + " / "
                        + Lang.Format("Size_MbFmt", t.total / 1048576.0)
                    : Lang.Format("Size_MbFmt", t.done / 1048576.0);
                _modsPage.SetFleasionStatus(Lang.Format("Mod_FleaProgFmt", detail));
            });
            await FleasionManager.DownloadAsync(progress, CancellationToken.None);
            RefreshFleasionStatus();
        }
        catch (Exception ex)
        {
            _modsPage.SetFleasionStatus(Lang.Format("Common_ErrFmt", ex.Message));
        }
    }

    // ================= Избранное =================

    private void RefreshFavorites()
    {
        if (_closed) return;
        _homePage.SetFavorites(FavoritesStore.Load()
            .Select(f => (f.PlaceId, f.Display)));
    }

    private void AddFavoriteFromHistory()
    {
        var (placeId, name) = _historyPage.SelectedGame();
        if (placeId <= 0)
        {
            Toast(Lang.Get("Hist_SelGame"), ToastKind.Info);
            return;
        }
        bool existed = FavoritesStore.Contains(placeId);
        FavoritesStore.Add(placeId, name);
        RefreshFavorites();
        SetStatus(existed
            ? Lang.Get("Fav_Existed")
            : Lang.Format("Fav_AddedFmt",
                name.Length > 0 ? name : Lang.Format("Common_PlaceFmt", placeId)), true);
    }

    private void RemoveFavorite(long placeId)
    {
        FavoritesStore.Remove(placeId);
        RefreshFavorites();
        SetStatus(Lang.Get("Fav_Removed"), true);
    }

    // ================= FastFlags =================

    private void RefreshFlagProfiles()
    {
        _flagsPage.SetProfiles(FlagProfiles.List());
        RefreshGameBindings();
    }

    private void ApplyFlagProfile(string name)
    {
        var flags = FlagProfiles.Get(name);
        if (flags == null) return;
        if (_flagsPage.CollectFlags().Count > 0)
        {
            if (MessageBox.Show(Lang.Format("Prof_ApplyConfirm", name),
                    "NekoStrap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
        }
        _flagsPage.SetFlags(new Dictionary<string, string>(flags));
        // Профиль должен сразу попасть в клиент, иначе «Применить»
        // меняет только таблицу, а запуск идёт с прежними флагами.
        FastFlagStore.Save(EffectiveVersion(), flags);
        if (!string.Equals(_config.ActiveFlagProfile, name, StringComparison.OrdinalIgnoreCase))
        {
            _config.ActiveFlagProfile = name;
            _config.Save(RobloxPaths.ConfigPath);
        }
        SetFlagsDirty(false);
        RefreshGameBindings();
        SetStatus(Lang.Format("Prof_AppliedFmt", name, flags.Count), false);
    }

    private void SaveFlagProfile()
    {
        string name = _flagsPage.ProfileNameText;
        if (name.Length == 0)
        {
            Toast(Lang.Get("Prof_NeedName"), ToastKind.Info);
            return;
        }
        var flags = _flagsPage.CollectFlags();
        if (flags.Count == 0)
        {
            Toast(Lang.Get("Prof_EmptyTable"), ToastKind.Info);
            return;
        }
        FlagProfiles.SaveProfile(name, flags);
        _flagsPage.ClearProfileName();
        RefreshFlagProfiles();
        SetStatus(Lang.Format("Prof_SavedFmt", name), true);
    }

    private void DeleteFlagProfile()
    {
        string name = _flagsPage.ProfileNameText;
        if (name.Length > 0)
        {
            if (FlagProfiles.Get(name) == null)
            {
                Toast(Lang.Format("Prof_NoSuchFmt", name), ToastKind.Info);
                return;
            }
        }
        else
        {
            // П.4: не заставлять вводить имя — показываем список профилей.
            var profiles = FlagProfiles.List();
            if (profiles.Count == 0)
            {
                Toast(Lang.Get("Flags_ProfilesEmpty"), ToastKind.Info);
                return;
            }
            var dlg = new PickDialog(Lang.Get("Dlg_PickProfile_T"), Lang.Get("Dlg_PickProfile_H"),
                profiles.Select(p => new PickItem(p, p)));
            if (dlg.ShowDialog() != true || dlg.Picked == null) return;
            name = dlg.Picked.Value;
        }
        if (MessageBox.Show(Lang.Format("Prof_DelConfirm", name), "NekoStrap",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        FlagProfiles.Delete(name);
        _flagsPage.ClearProfileName();
        RefreshFlagProfiles();
        SetStatus(Lang.Format("Prof_DeletedFmt", name), true);
    }

    private void DeleteAllFlags()
    {
        if (_flagsPage.CollectFlags().Count == 0) return;
        if (MessageBox.Show(Lang.Get("Flags_ClearConfirm"),
                "NekoStrap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _flagsPage.ClearFlags();
        SetStatus(Lang.Get("Flags_Cleared"), false);
    }

    private void SaveFlags()
    {
        try
        {
            var flags = _flagsPage.CollectFlags();
            FastFlagStore.Save(EffectiveVersion(), flags);
            // Таблицу правили руками — она больше не равна профилю.
            if (_config.ActiveFlagProfile.Length > 0)
            {
                _config.ActiveFlagProfile = "";
                _config.Save(RobloxPaths.ConfigPath);
                RefreshGameBindings();
            }
            RefreshHomeMeta();
            SetFlagsDirty(false);
            SetStatus(Lang.Get("Flags_Saved"), true);
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("Flags_SaveErr") + ex.Message, ToastKind.Warn);
        }
    }

    private void ImportFlags()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Lang.Get("Dlg_FlagsImport"),
            Filter = $"JSON (*.json)|*.json|{Lang.Get("Dlg_AllFiles")} (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            ApplyImportedFlags(File.ReadAllText(dlg.FileName));
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("Flags_ImportErr") + ex.Message, ToastKind.Warn);
        }
    }

    /// <summary>Импорт JSON из буфера обмена — тот же путь, что и из файла.</summary>
    private void ImportFlagsFromClipboard()
    {
        string text = "";
        try { text = Clipboard.GetText(); } catch { /* буфер недоступен */ }
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus(Lang.Get("Flags_NoClipboard"), false);
            return;
        }
        try
        {
            ApplyImportedFlags(text);
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("Flags_ImportErr") + ex.Message, ToastKind.Warn);
        }
    }

    /// <summary>
    /// Наложение импорта: новые флаги берутся сразу, совпадающие пропускаются,
    /// а конфликты (флаг стоит с другим значением) — по спросу: заменить этот,
    /// заменить все или не заменять.
    /// </summary>
    private void ApplyImportedFlags(string json)
    {
        var incoming = FastFlagStore.ParseImportJson(json);
        if (incoming.Count == 0)
        {
            SetStatus(Lang.Get("Flags_EmptyImport"), false);
            return;
        }

        var current = _flagsPage.CollectFlags();
        bool replaceAll = false;
        bool keepRest = false;
        int applied = 0;
        int skipped = 0;

        foreach (var (name, value) in incoming)
        {
            if (!current.TryGetValue(name, out string? old))
            {
                current[name] = value;
                applied++;
                continue;
            }
            if (string.Equals(old, value, StringComparison.Ordinal)) continue;
            if (keepRest)
            {
                skipped++;
                continue;
            }
            if (!replaceAll)
            {
                var ask = new FlagConflictDialog(name, old, value) { Owner = this };
                ask.ShowDialog();
                switch (ask.Choice)
                {
                    case FlagConflictChoice.ReplaceAll:
                        replaceAll = true;
                        break;
                    case FlagConflictChoice.Keep:
                        keepRest = true;
                        skipped++;
                        continue;
                    case FlagConflictChoice.Replace:
                        break;
                }
            }
            current[name] = value;
            applied++;
        }

        _flagsPage.SetFlags(current);
        SetFlagsDirty(true);
        SetStatus(skipped > 0
            ? Lang.Format("Flags_ImportedSkipFmt", applied, skipped)
            : Lang.Format("Flags_ImportedFmt", applied), true);
    }

    private void ExportFlags()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = Lang.Get("Dlg_FlagsExport"),
            Filter = "JSON (*.json)|*.json",
            FileName = "fastflags.json"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var flags = _flagsPage.CollectFlags();
            using var ms = new MemoryStream();
            using (var w = new System.Text.Json.Utf8JsonWriter(ms,
                       new System.Text.Json.JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                foreach (var (k, v) in flags.OrderBy(x => x.Key))
                {
                    w.WritePropertyName(k);
                    FastFlagStore.WriteValue(w, v);
                }
                w.WriteEndObject();
            }
            File.WriteAllBytes(dlg.FileName, ms.ToArray());
            SetStatus(Lang.Format("Flags_ExportedFmt", flags.Count), true);
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("Flags_ExportErr") + ex.Message, ToastKind.Warn);
        }
    }

    // ================= Версии =================

    private void RefreshVersions()
    {
        var versions = VersionManager.List();
        _versionsPage.SetVersions(versions, _config.InstalledVersion, _config.PreviousVersion);
        // Переустановка доступна всегда, когда есть что переустанавливать:
        // раньше кнопка включалась только после неудачной проверки файлов.
        _versionsPage.SetRepairEnabled(versions.Count > 0 && !_busy);
        string? studio = _config.StudioVersion;
        _versionsPage.SetStudioStatus(
            RobloxPaths.IsVersionGuid(studio) &&
            File.Exists(Path.Combine(RobloxPaths.VersionsDir, studio, RobloxInstaller.StudioExe))
            ? studio : Lang.Get("Ver_StudioNotSet"));
    }

    // ================= Целостность клиента =================

    private bool _checkingIntegrity;

    /// <summary>
    /// Проверка выбранной (или установленной) версии: обязательные файлы плюс
    /// снимок размеров, снятый при установке. Правленные модами файлы в расчёт
    /// не берутся — там лежит содержимое профиля, а не оригинальный клиент.
    /// </summary>
    private async Task CheckClientIntegrityAsync()
    {
        if (_checkingIntegrity) return;
        string? guid = _versionsPage.SelectedVersion() ?? EffectiveVersion();
        string dir = guid != null ? Path.Combine(RobloxPaths.VersionsDir, guid) : "";
        if (guid == null || !RobloxPaths.IsVersionGuid(guid) || !Directory.Exists(dir))
        {
            _versionsPage.SetIntegrityStatus(Lang.Get("Int_NoVersion"));
            _versionsPage.SetRepairEnabled(false);
            return;
        }

        _checkingIntegrity = true;
        _versionsPage.SetRepairEnabled(false);
        _versionsPage.SetIntegrityStatus(Lang.Get("Int_Checking"));
        try
        {
            // Десятки тысяч файлов на диске — гоним в фоне, UI не трогаем.
            var report = await Task.Run(() => Integrity.Check(dir));
            var sb = new StringBuilder();
            if (report.Ok)
            {
                sb.Append(Lang.Format("Int_OkFmt", report.Checked));
            }
            else
            {
                sb.Append(Lang.Format("Int_ProblemsFmt", report.Issues.Count, report.Checked));
                foreach (var i in report.Issues.Take(5))
                    sb.AppendLine().Append("  • ").Append(i.Path)
                     .Append(" — ").Append(IntKindText(i.Kind));
                if (report.Issues.Count > 5)
                    sb.AppendLine().Append("  ...");
            }
            if (!report.HasBaseline && report.Ok)
                sb.AppendLine().Append(Lang.Get("Int_NoBaseline"));
            _versionsPage.SetIntegrityStatus(sb.ToString());
            _versionsPage.SetRepairEnabled(true);
        }
        catch (Exception ex)
        {
            _versionsPage.SetIntegrityStatus(Lang.Get("Int_CheckErr") + ex.Message);
            _versionsPage.SetRepairEnabled(true);
        }
        finally
        {
            _checkingIntegrity = false;
        }
    }

    private static string IntKindText(string kind) => kind switch
    {
        Integrity.KindMissing => Lang.Get("Int_Missing"),
        Integrity.KindEmpty => Lang.Get("Int_Empty"),
        _ => Lang.Get("Int_Size")
    };

    /// <summary>
    /// Ремонт: сносим папку активной версии и ставим её заново — установщик
    /// докачает только недостающее, битое перезапишет.
    /// </summary>
    private async Task RepairClientAsync()
    {
        if (_busy) return;
        string? guid = EffectiveVersion();
        if (guid == null || !RobloxPaths.IsVersionGuid(guid))
        {
            _versionsPage.SetIntegrityStatus(Lang.Get("Int_NoVersion"));
            return;
        }
        if (PlayerIsRunning(_playerProcess))
        {
            // Раньше тут был только текст статуса — казалось, что кнопка не работает.
            Toast(Lang.Get("Int_Running"), ToastKind.Info);
            return;
        }
        if (MessageBox.Show(Lang.Format("Int_RepairAsk", guid), "NekoStrap",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _busy = true;
        _homePage.SetBusy(true);
        try
        {
            await StopBackgroundRobloxUpdateAsync();
            var progress = new Progress<InstallProgress>(p =>
            {
                if (_closed) return;
                SetStatus(FormatInstall(p), false);
                SetProgress(p.Fraction);
            });

            _versionsPage.SetIntegrityStatus(Lang.Get("Int_Cleaning"));
            string dir = Path.Combine(RobloxPaths.VersionsDir, guid);
            await Task.Run(() =>
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
                catch { /* файл занят — установщик сообщит ошибкой */ }
            });
            // Папка могла не удалиться: без этого установщик решит, что всё цело.
            if (Directory.Exists(dir))
                throw new IOException(dir);

            string oldGuid = _config.InstalledVersion;
            string newGuid = await RobloxInstaller.EnsureInstalledAsync(
                _config, progress, CancellationToken.None);
            if (RobloxPaths.IsVersionGuid(oldGuid) && oldGuid != newGuid)
                _config.PreviousVersion = oldGuid;
            RobloxInstaller.CleanupExcept(newGuid, _config.PreviousVersion, _config.StudioVersion);
            _config.Save(RobloxPaths.ConfigPath);
            RefreshVersions();

            string done = Lang.Format("Int_RepairDoneFmt", newGuid);
            _versionsPage.SetIntegrityStatus(done);
            SetStatus(done, true);
        }
        catch (Exception ex)
        {
            _versionsPage.SetIntegrityStatus(Lang.Get("Int_RepairErr") + ex.Message);
        }
        finally
        {
            SetProgress(null);
            _homePage.SetBusy(false);
            _busy = false;
        }
    }

    private void ActivateSelectedVersion()
    {
        string? guid = _versionsPage.SelectedVersion();
        if (guid == null)
        {
            Toast(Lang.Get("Ver_SelOne"), ToastKind.Info);
            return;
        }
        if (guid == _config.InstalledVersion) return;
        if (RobloxPaths.IsVersionGuid(_config.InstalledVersion))
            _config.PreviousVersion = _config.InstalledVersion;
        _config.InstalledVersion = guid;
        _config.Save(RobloxPaths.ConfigPath);
        RefreshVersions();
        RefreshHomeMeta();
        SetStatus(Lang.Format("Ver_ActivatedFmt", guid), true);
    }

    private void DeleteSelectedVersion()
    {
        string? guid = _versionsPage.SelectedVersion();
        if (guid == null)
        {
            Toast(Lang.Get("Ver_SelOne"), ToastKind.Info);
            return;
        }
        if (guid == _config.InstalledVersion || guid == _config.StudioVersion)
        {
            Toast(Lang.Get("Ver_NoDelActive"), ToastKind.Warn);
            return;
        }
        if (MessageBox.Show(Lang.Format("Ver_DelConfirm", guid), "NekoStrap",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            VersionManager.Delete(guid);
            if (_config.PreviousVersion == guid) _config.PreviousVersion = "";
            _config.Save(RobloxPaths.ConfigPath);
            RefreshVersions();
        }
        catch (Exception ex)
        {
            Toast(ex.Message, ToastKind.Warn);
        }
    }

    private async Task InstallStudioAsync()
    {
        if (_busy) return;
        _busy = true;
        _homePage.SetBusy(true);
        try
        {
            var progress = new Progress<InstallProgress>(p =>
            {
                if (_closed) return;
                SetStatus($"Studio — {FormatInstall(p)}", false);
                SetProgress(p.Fraction);
            });
            await StopBackgroundRobloxUpdateAsync();
            string guid = await RobloxInstaller.EnsureStudioInstalledAsync(
                _config, progress, CancellationToken.None);
            _config.Save(RobloxPaths.ConfigPath);
            RefreshVersions();
            SetStatus(Lang.Format("Ver_StudioDone", guid), true);
        }
        catch (Exception ex)
        {
            SetStatus(Lang.Get("Ver_StudioErr"), false);
            Toast(Lang.Get("Ver_StudioErr2") + ex.Message, ToastKind.Error);
        }
        finally
        {
            SetProgress(null);
            _homePage.SetBusy(false);
            _busy = false;
        }
    }

    private void LaunchStudio()
    {
        try
        {
            string? guid = RobloxPaths.IsVersionGuid(_config.StudioVersion)
                ? _config.StudioVersion : null;
            string? exe = guid != null
                ? Path.Combine(RobloxPaths.VersionsDir, guid, RobloxInstaller.StudioExe) : null;
            if (exe == null || !File.Exists(exe))
                throw new InvalidOperationException(Lang.Get("Ver_StudioNeedInstall"));
            RobloxLauncher.LaunchStudio(exe);
        }
        catch (Exception ex)
        {
            Toast(ex.Message, ToastKind.Warn);
        }
    }

    // ================= История =================

    private void RefreshHistory()
    {
        if (_closed) return;
        var games = _playtime.Games
            .Select(kv => (
                placeId: long.TryParse(kv.Key, out long p) ? p : 0,
                name: kv.Value.Name,
                seconds: kv.Value.Seconds,
                last: kv.Value.LastPlayed))
            .Where(g => g.seconds > 0);
        games = _historyPage.SortByRecent
            ? games.OrderByDescending(g => g.last)
            : games.OrderByDescending(g => g.seconds);
        _historyPage.SetGames(games.ToList());
        _historyPage.SetSessions(_recent.List());
    }

    private void PlayHistorySelected()
    {
        long placeId = _historyPage.SelectedPlaceId();
        if (placeId <= 0)
        {
            Toast(Lang.Get("Hist_SelGame"), ToastKind.Info);
            return;
        }
        LaunchPlaceById(placeId);
    }

    private void DeleteHistorySelected()
    {
        long placeId = _historyPage.SelectedPlaceId();
        if (placeId <= 0)
        {
            Toast(Lang.Get("Hist_SelGame"), ToastKind.Info);
            return;
        }
        _playtime.Remove(placeId);
        RefreshHistory();
        RefreshHomeMeta();
    }

    private void ClearHistory()
    {
        if (_playtime.Games.Count == 0) return;
        if (MessageBox.Show(Lang.Get("Hist_ClearConfirm"), "NekoStrap",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _playtime.Clear();
        RefreshHistory();
        RefreshHomeMeta();
    }

    private RecentSession? RequireSession()
    {
        var s = _historyPage.SelectedSession();
        if (s == null)
        {
            Toast(Lang.Get("Hist_SelSession"), ToastKind.Info);
            return null;
        }
        if (s.PlaceId <= 0)
        {
            Toast(Lang.Get("Hist_NoPlace"), ToastKind.Warn);
            return null;
        }
        return s;
    }

    private void RejoinSessionSelected()
    {
        var s = RequireSession();
        if (s == null) return;
        if (s.JobId.Length == 0)
        {
            if (MessageBox.Show(Lang.Get("Common_NoJob"),
                    "NekoStrap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
        }
        LaunchServerByJob(s.PlaceId, s.JobId);
    }

    private void JoinSessionPlace()
    {
        var s = RequireSession();
        if (s == null) return;
        LaunchPlaceById(s.PlaceId);
    }

    private void CopySessionPlaceId()
    {
        var s = RequireSession();
        if (s == null) return;
        CopyText(s.PlaceId.ToString());
    }

    private void CopySessionLink()
    {
        var s = RequireSession();
        if (s == null) return;
        string dash = "—";
        string text =
            $"{Lang.Get("Srv_Game")}: {s.DisplayName}\n" +
            $"PlaceId: {s.PlaceId}\n" +
            $"JobId: {(s.JobId.Length > 0 ? s.JobId : dash)}\n" +
            $"{Lang.Get("Srv_Link")}: {RobloxLauncher.BuildServerUrl(s.PlaceId, s.JobId)}\n" +
            $"{Lang.Get("Srv_Site")}: {RobloxLauncher.BuildWebUrl(s.PlaceId)}";
        CopyText(text);
    }

    private void OpenSessionSite()
    {
        var s = RequireSession();
        if (s == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(
                RobloxLauncher.BuildWebUrl(s.PlaceId)) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("Common_BrowserErr") + ex.Message, ToastKind.Warn);
        }
    }

    private void DeleteSessionSelected()
    {
        var s = _historyPage.SelectedSession();
        if (s == null)
        {
            Toast(Lang.Get("Hist_SelSession2"), ToastKind.Info);
            return;
        }
        _recent.Remove(s);
        RefreshHistory();
    }

    private void ClearSessions()
    {
        if (_recent.List().Count == 0) return;
        if (MessageBox.Show(Lang.Get("Hist_ClearSessConfirm"),
                "NekoStrap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _recent.Clear();
        RefreshHistory();
    }

    private void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            SetStatus(Lang.Get("Common_Copied"), true);
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("Common_CopyErr") + ex.Message, ToastKind.Warn);
        }
    }

    /// <summary>
    /// Перезаход на тот же сервер по JobId. Без JobId — обычный заход на плейс.
    /// </summary>
    private void LaunchServerByJob(long placeId, string jobId)
    {
        try
        {
            string? guid = EffectiveVersion();
            string? exe = RobloxLauncher.GetExePath(guid);
            if (exe == null)
            {
                Toast(Lang.Get("Play_NoRoblox"), ToastKind.Warn);
                return;
            }
            _watcher.Clear();
            SetStatus(jobId.Length > 0
                ? Lang.Get("Hop_Rejoin") : Lang.Get("Play_OpenPlace"), false);
            ApplyGameBinding(placeId); // авто-профиль игры (моды/флаги)
            ApplyModsNow(); // активный профиль модов — перед стартом
            Process? process = RobloxLauncher.LaunchServer(exe, placeId, jobId);
            if (process == null)
                throw new InvalidOperationException(Lang.Get("Play_LaunchFail"));
            AfterLaunch(process);
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("Hop_RejoinErr") + ex.Message, ToastKind.Warn);
        }
    }

    // ================= Настройки =================

    private void ApplyConfigToUi()
    {
        ClickSound.Enabled = _config.Sounds;
        _settingsPage.CloseOnLaunchCheck.IsChecked = _config.CloseOnLaunch;
        _settingsPage.DiscordRpcCheck.IsChecked = _config.DiscordRpc;
        _settingsPage.DiscordNameCheck.IsChecked = _config.DiscordShowName;
        _settingsPage.DiscordServerCheck.IsChecked = _config.DiscordShowServer;
        _settingsPage.DiscordIconCheck.IsChecked = _config.DiscordShowIcon;
        _settingsPage.DiscordTimeCheck.IsChecked = _config.DiscordShowElapsed;
        _settingsPage.DiscordBtnCheck.IsChecked = _config.DiscordShowButton;
        _settingsPage.DiscordAllowJoinCheck.IsChecked = _config.DiscordAllowJoin;
        _settingsPage.SoundsCheck.IsChecked = _config.Sounds;
            _settingsPage.AutoUpdateCheck.IsChecked = _config.AutoUpdate;
            _settingsPage.RobloxBgUpdateCheck.IsChecked = _config.RobloxBgUpdate;
        _settingsPage.FpsCheck.IsChecked = _config.FpsLimit > 0;
        _settingsPage.FpsValueText = _config.FpsLimit > 0 ? _config.FpsLimit.ToString() : "240";
        _settingsPage.DiscordAssetKeyText = _config.DiscordAssetKey;
        _settingsPage.DownloadThreadsValue = _config.DownloadThreads;
        _settingsPage.RobloxPriorityValue = _config.RobloxPriority;
        _settingsPage.MinimizeToTrayCheck.IsChecked = _config.MinimizeToTray;
        _settingsPage.CloseToTrayCheck.IsChecked = _config.CloseToTray;
        _settingsPage.NotificationsCheck.IsChecked = _config.NotificationsEnabled;
        _historyPage.TrackPlaytimeCheck.IsChecked = _config.TrackPlaytime;
        _settingsPage.RobloxNoTrayCheck.IsChecked = _config.RobloxNoTray;
        _settingsPage.RobloxNoStartupCheck.IsChecked = _config.RobloxNoStartup;
        _settingsPage.RobloxPathText = _config.RobloxPath;
        _chatPage.ChatEnabledCheck.IsChecked = _config.ChatEnabled;
        _chatPage.ChatDmCheck.IsChecked = _config.ChatDmEnabled;
        _chatPage.ChatOverlayCheck.IsChecked = _config.ChatOverlayOnJoin;
        _chatPage.SetRoomMode(!IsServerRoom());
        _chatPage.SetDmEnabled(_config.ChatEnabled && _config.ChatDmEnabled);
        _modsPage.FleasionCheck.IsChecked = _config.FleasionEnabled;
        _settingsPage.SetTelemetryBlock(_config.BlockTelemetry);
        // Не применено + не админ => подсказка, что и почему не вышло.
        bool telemetryOn = TelemetryBlocker.IsApplied();
        _settingsPage.SetTelemetryStatus(
            Lang.Get(telemetryOn ? "Set_Telemetry_On"
                : TelemetryBlocker.IsAdministrator() ? "Set_Telemetry_Off"
                : "Set_Telemetry_NeedAdmin"),
            telemetryOn);
        _settingsPage.SetPerfState(_config.PerfOverlay, _config.PerfShowPing,
            _config.PerfShowCpu, _config.PerfShowRam, _config.PerfShowGpu,
            _config.PerfShowServer, _config.PerfShowTime, _config.PerfOverlayGlass,
            _config.PerfOverlayOpacity);
    }

    private void SaveConfigFromUi()
    {
        _config.CloseOnLaunch = _settingsPage.CloseOnLaunchCheck.IsChecked == true;
        _config.DiscordRpc = _settingsPage.DiscordRpcCheck.IsChecked == true;
        _config.DiscordShowName = _settingsPage.DiscordNameCheck.IsChecked == true;
        _config.DiscordShowServer = _settingsPage.DiscordServerCheck.IsChecked == true;
        _config.DiscordShowIcon = _settingsPage.DiscordIconCheck.IsChecked == true;
        _config.DiscordShowElapsed = _settingsPage.DiscordTimeCheck.IsChecked == true;
        _config.DiscordShowButton = _settingsPage.DiscordBtnCheck.IsChecked == true;
        _config.DiscordAllowJoin = _settingsPage.DiscordAllowJoinCheck.IsChecked == true;
        _config.DownloadThreads = _settingsPage.DownloadThreadsValue;
        _config.RobloxPriority = _settingsPage.RobloxPriorityValue;
        _config.Sounds = _settingsPage.SoundsCheck.IsChecked == true;
        _config.AutoUpdate = _settingsPage.AutoUpdateCheck.IsChecked == true;
        _config.RobloxBgUpdate = _settingsPage.RobloxBgUpdateCheck.IsChecked == true;
        _config.RobloxPath = _settingsPage.RobloxPathText;
        _config.FpsLimit = _settingsPage.FpsCheck.IsChecked == true
            && int.TryParse(_settingsPage.FpsValueText, out int fps) && fps >= 5 && fps <= 10000
            ? fps : 0;
        _config.DiscordAssetKey = _settingsPage.DiscordAssetKeyText;
        _config.MinimizeToTray = _settingsPage.MinimizeToTrayCheck.IsChecked == true;
        _config.CloseToTray = _settingsPage.CloseToTrayCheck.IsChecked == true;
        _config.NotificationsEnabled = _settingsPage.NotificationsCheck.IsChecked == true;
        _config.TrackPlaytime = _historyPage.TrackPlaytimeCheck.IsChecked == true;
        _config.RobloxNoTray = _settingsPage.RobloxNoTrayCheck.IsChecked == true;
        _config.RobloxNoStartup = _settingsPage.RobloxNoStartupCheck.IsChecked == true;
        _config.FleasionEnabled = _modsPage.FleasionCheck.IsChecked == true;
        _config.BlockTelemetry = _settingsPage.TelemetryBlockCheck.IsChecked == true;
        _config.PerfOverlay = _settingsPage.PerfOverlayCheck.IsChecked == true;
        _config.PerfShowPing = _settingsPage.PerfPingCheck.IsChecked == true;
        _config.PerfShowCpu = _settingsPage.PerfCpuCheck.IsChecked == true;
        _config.PerfShowRam = _settingsPage.PerfRamCheck.IsChecked == true;
        _config.PerfShowGpu = _settingsPage.PerfGpuCheck.IsChecked == true;
        _config.PerfShowServer = _settingsPage.PerfServerCheck.IsChecked == true;
        _config.PerfShowTime = _settingsPage.PerfTimeCheck.IsChecked == true;
        _config.PerfOverlayGlass = _settingsPage.PerfGlassCheck.IsChecked == true;
        _config.PerfOverlayOpacity = _settingsPage.PerfOpacity;
        ClickSound.Enabled = _config.Sounds;
        ApplyDiscord();

        string newBase = string.IsNullOrWhiteSpace(_config.RobloxPath)
            ? RobloxPaths.DefaultBaseDir()
            : _config.RobloxPath.Trim();
        if (!newBase.Equals(RobloxPaths.BaseDir, StringComparison.OrdinalIgnoreCase))
        {
            RobloxPaths.Configure(newBase);
            _config.InstalledVersion = RobloxPaths.FindInstalledVersion() ?? "";
        }
        _config.Save(RobloxPaths.ConfigPath);
        RefreshHomeMeta();
    }

    /// <summary>
    /// Значения, которые конфиг молча отбрасывает (лимит FPS, hex-цвета),
    /// проговариваем тостом — иначе «сохранилось», а эффекта нет.
    /// </summary>
    private void WarnInvalidSettings()
    {
        if (_settingsPage.FpsCheck.IsChecked == true && !_settingsPage.FpsValid)
            Toast(Lang.Get("Set_Fps_Bad"), ToastKind.Warn);
        if (!_settingsPage.ColorsValid)
            Toast(Lang.Get("Set_Hex_Bad"), ToastKind.Warn);
    }

    /// <summary>Сохранить настройки чата кнопкой на странице чата.</summary>
    private void SaveChatFromUi()
    {
        _config.Save(RobloxPaths.ConfigPath);
        SetStatus(Lang.Get("Set_Saved"), true);
    }

    /// <summary>Тумблеры применяются СРАЗУ, без кнопки «Сохранить».</summary>
    private void SubscribeLiveToggles()
    {
        void Bool(CheckBox box, Action<bool> apply)
        {
            box.Checked += (_, _) => { apply(true); SaveQuiet(); };
            box.Unchecked += (_, _) => { apply(false); SaveQuiet(); };
        }
        Bool(_settingsPage.CloseOnLaunchCheck, v => _config.CloseOnLaunch = v);
        Bool(_settingsPage.DiscordRpcCheck, v => { _config.DiscordRpc = v; ApplyDiscord(); });
        Bool(_settingsPage.DiscordNameCheck, v => { _config.DiscordShowName = v; ApplyDiscord(); });
        Bool(_settingsPage.DiscordServerCheck, v => { _config.DiscordShowServer = v; ApplyDiscord(); });
        Bool(_settingsPage.DiscordIconCheck, v => { _config.DiscordShowIcon = v; ApplyDiscord(); });
        Bool(_settingsPage.DiscordTimeCheck, v => { _config.DiscordShowElapsed = v; ApplyDiscord(); });
        Bool(_settingsPage.DiscordBtnCheck, v => { _config.DiscordShowButton = v; ApplyDiscord(); });
        Bool(_settingsPage.DiscordAllowJoinCheck, v => { _config.DiscordAllowJoin = v; ApplyDiscord(); });
        _settingsPage.DownloadThreadsChanged += v => { _config.DownloadThreads = v; SaveQuiet(); };
        _settingsPage.PriorityChanged += v => { _config.RobloxPriority = v; SaveQuiet(); };
        Bool(_settingsPage.AutoUpdateCheck, v => _config.AutoUpdate = v);
        Bool(_settingsPage.RobloxBgUpdateCheck, v =>
        {
            _config.RobloxBgUpdate = v;
            if (v) MaybeStartBackgroundRobloxUpdate();
            else _bgUpdateCts?.Cancel(); // выключили тумблер — качать бросили
        });
        Bool(_settingsPage.FpsCheck, v => _config.FpsLimit = v ? ParseFpsValue() : 0);
        Bool(_settingsPage.MinimizeToTrayCheck, v => _config.MinimizeToTray = v);
        Bool(_settingsPage.CloseToTrayCheck, v => _config.CloseToTray = v);
        Bool(_settingsPage.NotificationsCheck, v => _config.NotificationsEnabled = v);
        Bool(_historyPage.TrackPlaytimeCheck, v =>
        {
            _config.TrackPlaytime = v;
            if (!v)
            {
                _sessionStartedAt = default;
                _sessionPlaceId = 0;
            }
        });
        Bool(_settingsPage.RobloxNoTrayCheck, v =>
        {
            _config.RobloxNoTray = v;
            RobloxAppFixes.Apply(_config.RobloxNoTray, _config.RobloxNoStartup);
        });
        Bool(_settingsPage.RobloxNoStartupCheck, v =>
        {
            _config.RobloxNoStartup = v;
            RobloxAppFixes.Apply(_config.RobloxNoTray, _config.RobloxNoStartup);
        });
        Bool(_chatPage.ChatEnabledCheck, v =>
        {
            _config.ChatEnabled = v;
            _chatPage.SetDmEnabled(v && _config.ChatDmEnabled);
            SyncChat();
            RefreshChatHint();
        });
        Bool(_chatPage.ChatDmCheck, v =>
        {
            _config.ChatDmEnabled = v;
            _chat.DmAllowed = v;
            _chatPage.SetDmEnabled(_config.ChatEnabled && v);
        });
        Bool(_chatPage.ChatOverlayCheck, v => _config.ChatOverlayOnJoin = v);
    }

    private int ParseFpsValue()
    {
        if (int.TryParse(_settingsPage.FpsValueText, out int fps) && fps >= 5 && fps <= 10000)
            return fps;
        return 240;
    }

    // ================= Чат =================

    /// <summary>Связать страницу/оверлей чата с клиентом и событиями.</summary>
    private void WireChat()
    {
        _chatPage.SaveClicked += SaveChatFromUi;
        _chatPage.ServerMessageSend += text => _ = SendChatAsync(text, 0);
        _chatPage.DmMessageSend += (to, text) => _ = SendChatAsync(text, to);
        _chatPage.OpenOverlayClicked += () => ShowChatOverlay();
        _chatPage.RoomChanged += global =>
        {
            _config.ChatRoom = global ? "global" : "server";
            SaveQuiet();
            SyncChat();
            RefreshChatHint();
        };

        _chat.StateChanged += state =>
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closed) return;
                _chatPage.SetState(state);
                _chatOverlay?.SetState(state);
                RefreshChatHint();
            }));
        _chat.UsersChanged += users =>
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closed) return;
                _chatPage.SetUsers(users, _chat.SelfUid);
                _chatOverlay?.SetUserCount(users.Select(u => u.Uid).Distinct().Count());
            }));
        _chat.MessageReceived += msg =>
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closed) return;
                _chatLog.Add(msg);
                while (_chatLog.Count > 200) _chatLog.RemoveAt(0);
                _chatPage.AppendMessage(msg);
                _chatOverlay?.AppendMessage(msg);
                // Входящее ЛС — всплывашка, если оверлей не открыт на виду.
                if (msg.To > 0 && !msg.Mine &&
                    (_chatOverlay == null || !_chatOverlay.IsVisible) &&
                    _config.NotificationsEnabled)
                {
                    try
                    {
                        _trayIcon?.ShowBalloonTip(3000, Lang.Get("Tray_ChatBalloon"),
                            $"{msg.Name}: {msg.Text}", WinForms.ToolTipIcon.Info);
                    }
                    catch { /* ignore */ }
                }
            }));
        _chat.ServerError += err =>
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closed) return;
                _chatPage.SetStatus(err, false);
            }));
    }

    private async Task SendChatAsync(string text, long to)
    {
        try
        {
            if (to > 0)
                await _chat.SendDmAsync(to, text);
            else
                await _chat.SendServerAsync(text);
            _chatOverlay?.SetState(_chat.State);
        }
        catch (Exception ex)
        {
            string error = ex.Message == ChatClient.ErrNotConnected
                ? Lang.Get("Chat_NotConnected")
                : Lang.Format("Chat_SendErrorFmt", ex.Message);
            _chatPage.SetStatus(error, false);
            _chatOverlay?.SetHint(error);
        }
    }

    /// <summary>
    /// Две комнаты: «global» — общий чат лаунчера, живёт всегда (видят все,
    /// кто подключён); «server» — чат текущего роблокс-сервера
    /// (комната placeId_jobId), только вместе с живой сессией.
    /// Выбранная комната хранится в конфиге (ChatRoom).
    /// </summary>
    private void SyncChat()
    {
        string? room = null;
        if (_config.ChatEnabled)
        {
            if (!IsServerRoom())
            {
                room = "global";
            }
            else
            {
                var session = _watcher.Current;
                if (session != null && session.PlaceId > 0 && session.JobId.Length > 0)
                    room = session.PlaceId + "_" + session.JobId;
            }
        }
        if (room == null)
        {
            // Отключились осознанно — сбрасываем комнату, чтобы при
            // следующем подключении лента началась свежей с сервера.
            _chatRoom = "";
            _ = _chat.DisconnectAsync();
            return;
        }
        // Уже в нужной комнате — не дёргаем реконнект (ChatClient и так
        // переподключается сам, если оборвалось).
        if (string.Equals(_chatRoom, room, StringComparison.Ordinal)) return;
        _ = ConnectChatAsync(room);
    }

    private bool IsServerRoom() =>
        string.Equals(_config.ChatRoom, "server", StringComparison.OrdinalIgnoreCase);

    // ================= Коды приглашений =================

    /// <summary>Код, который спишем, когда игрок реально зайдёт на сервер.</summary>
    private string _pendingInviteCode = "";
    private long _pendingInvitePlace;
    private string _pendingInviteJob = "";

    /// <summary>Код на свой текущий сервер: место и джоба из живой сессии.</summary>
    private async Task CreateInviteAsync(int maxUses, int ttlSec)
    {
        var session = _watcher.Current;
        if (session == null || session.PlaceId <= 0 || session.JobId.Length == 0)
        {
            _homePage.SetInviteCreateError(InviteErrors.BadPlace);
            return;
        }
        _homePage.SetInviteBusy(true);
        var reply = await InviteClient.CreateAsync(session.PlaceId, session.JobId, maxUses, ttlSec);
        if (_closed) return;
        if (!reply.Ok || reply.Info == null)
        {
            _homePage.SetInviteCreateError(reply.Error);
            return;
        }
        StoreInvite(reply.Info);
        _homePage.SetInviteBusy(false);
        _homePage.ShowInviteCode(reply.Info.Code, reply.Info.Uses,
            reply.Info.MaxUses, reply.Info.ExpiresAt);
    }

    /// <summary>Вход по коду друга: сначала узнаём сервер (вход не списывается),
    /// потом убиваем текущий клиент и запускаемся на нужном месте.</summary>
    private async Task JoinByInviteAsync(string code)
    {
        _homePage.SetInviteJoinBusy(true);
        var peek = await InviteClient.PeekAsync(code);
        if (_closed)
        {
            _homePage.SetInviteJoinBusy(false);
            return;
        }
        if (!peek.Ok || peek.Info == null)
        {
            _homePage.SetInviteJoinBusy(false);
            _homePage.SetInviteJoinError(peek.Error);
            return;
        }
        var info = peek.Info;
        _pendingInviteCode = info.Code;
        _pendingInvitePlace = info.PlaceId;
        _pendingInviteJob = info.JobId;
        _homePage.SetInviteJoinStatus("Inv_JoinOk");
        _homePage.SetInviteJoinBusy(false);

        await KillCurrentPlayerAsync();
        if (_closed) return;
        if (info.JobId.Length > 0) LaunchServerByJob(info.PlaceId, info.JobId);
        else LaunchPlaceById(info.PlaceId);
    }

    /// <summary>Списание входа — только когда сессия реально поднялась на
    /// нужном месте. Так упавший запуск не сжигает одноразовый код.</summary>
    private void TryClaimInvite(GameSession session)
    {
        if (_pendingInviteCode.Length == 0 || session.PlaceId != _pendingInvitePlace)
            return;
        if (_pendingInviteJob.Length > 0 && session.JobId.Length > 0 &&
            !string.Equals(session.JobId, _pendingInviteJob, StringComparison.OrdinalIgnoreCase))
            return;

        string code = _pendingInviteCode;
        _pendingInviteCode = "";
        _pendingInvitePlace = 0;
        _pendingInviteJob = "";

        _ = Task.Run(async () =>
        {
            var reply = await InviteClient.ClaimAsync(code);
            if (_closed) return;
            if (reply.Ok)
                Toast(Lang.Format("Inv_UsedFmt", code), ToastKind.Info, 3000);
            else if (reply.Error is InviteErrors.Used or InviteErrors.Expired or InviteErrors.Gone)
                Toast(HomePage.InviteErrorText(reply.Error), ToastKind.Warn, 4500);
        });
    }

    /// <summary>Свой код пережил перезапуск? Сверяем с сервером при старте.</summary>
    private async Task VerifyStoredInviteAsync()
    {
        string code = _config.InviteCode;
        if (code.Length == 0) return;
        var peek = await InviteClient.PeekAsync(code);
        if (_closed || _config.InviteCode != code) return;
        if (!peek.Ok)
        {
            // Сеть лежит — код не трогаем. «Мёртв» значит только
            // «не найден / истёк / уже израсходован».
            if (peek.Error is InviteErrors.Gone or InviteErrors.Expired or InviteErrors.Used)
                ClearStoredInvite();
            return;
        }
        if (peek.Info == null) return;
        StoreInvite(peek.Info);
        _homePage.ShowInviteCode(peek.Info.Code, peek.Info.Uses,
            peek.Info.MaxUses, peek.Info.ExpiresAt);
    }

    private void StoreInvite(InviteCodeInfo info)
    {
        _config.InviteCode = info.Code;
        _config.InvitePlaceId = info.PlaceId;
        _config.InviteJobId = info.JobId;
        _config.InviteMaxUses = info.MaxUses;
        _config.InviteUses = info.Uses;
        _config.InviteExpiresAt = info.ExpiresAt;
        SaveQuiet();
    }

    private void ClearStoredInvite()
    {
        _config.InviteCode = "";
        _config.InvitePlaceId = 0;
        _config.InviteJobId = "";
        _config.InviteMaxUses = 1;
        _config.InviteUses = 0;
        _config.InviteExpiresAt = DateTime.MinValue;
        SaveQuiet();
    }

    private void DropInvite()
    {
        ClearStoredInvite();
        _pendingInviteCode = "";
        _pendingInvitePlace = 0;
        _pendingInviteJob = "";
    }

    private async Task ConnectChatAsync(string room)
    {
        try
        {
            long uid = _config.ChatUserId;
            if (uid <= 0)
            {
                uid = Random.Shared.NextInt64(1, long.MaxValue);
                _config.ChatUserId = uid;
                SaveQuiet();
            }
            // Авто-ник: имя активного аккаунта Roblox, иначе Player####.
            string name = "";
            try { name = AccountStore.Load().Active?.Name ?? ""; } catch { /* ignore */ }
            if (name.Length == 0)
                name = "Player" + uid % 10000;

            _chat.DmAllowed = _config.ChatDmEnabled;
            string url = ChatClient.NormalizeUrl(LauncherConfig.DefaultChatServerUrl);
            if (url.Length == 0)
            {
                _chatPage.SetStatus(Lang.Get("Chat_BadUrl"), false);
                return;
            }
            // Новая комната — чистим ленту от прошлого захода.
            if (!string.Equals(_chatRoom, room, StringComparison.Ordinal))
            {
                _chatRoom = room;
                _chatLog.Clear();
                _chatPage.ResetForRoom();
                _chatOverlay?.ResetMessages();
            }
            await _chat.ConnectAsync(url, room, uid, name);
        }
        catch (Exception ex)
        {
            _chatPage.SetStatus(ex.Message, false);
        }
    }

    private void RefreshChatHint()
    {
        if (_closed) return;
        if (!_config.ChatEnabled)
        {
            _chatPage.SetHint(Lang.Get("Chat_HintOff"));
            return;
        }
        if (!IsServerRoom())
        {
            _chatPage.SetHint(Lang.Get("Chat_HintGlobal"));
            return;
        }
        var session = _watcher.Current;
        _chatPage.SetHint(session != null && session.JobId.Length > 0
            ? Lang.Get("Chat_HintLive")
            : Lang.Get("Chat_NotInGame"));
    }

    /// <summary>Летающий оверлей чата: показать/перепоказать (или создать).</summary>
    private void ShowChatOverlay()
    {
        try
        {
            if (_chatOverlay != null)
            {
                _chatOverlay.Show();
                _chatOverlay.Activate();
                return;
            }
            _chatOverlay = new ChatOverlayWindow();
            _chatOverlay.SendRequested += text => _ = SendChatAsync(text, 0);
            _chatOverlay.Closed += (_, _) =>
            {
                try
                {
                    _config.ChatOverlayX = (int)_chatOverlay.Left;
                    _config.ChatOverlayY = (int)_chatOverlay.Top;
                    SaveQuiet();
                }
                catch { /* ignore */ }
                _chatOverlay = null;
            };
            double vw = SystemParameters.VirtualScreenWidth;
            double vh = SystemParameters.VirtualScreenHeight;
            if (_config.ChatOverlayX >= 0 && _config.ChatOverlayY >= 0 &&
                _config.ChatOverlayX < vw - 80 && _config.ChatOverlayY < vh - 40)
            {
                _chatOverlay.Left = _config.ChatOverlayX;
                _chatOverlay.Top = _config.ChatOverlayY;
            }
            else
            {
                _chatOverlay.Left = SystemParameters.VirtualScreenLeft + vw - 380;
                _chatOverlay.Top = SystemParameters.VirtualScreenTop + vh - 540;
            }
            _chatOverlay.SetState(_chat.State);
            _chatOverlay.Replay(_chatLog);
            _chatOverlay.Show();
        }
        catch { /* ignore */ }
    }

    // ---------- CDN-фикс (hosts) ----------

    private void RefreshCdnStatus()
    {
        bool active = false;
        try { active = CdnFix.IsApplied(); } catch { /* ignore */ }
        _settingsPage.SetCdnStatus(active, active ? "IP " + CdnFix.FixIp : "");
    }

    private void CdnApply()
    {
        if (!EnsureAdmin(Lang.Get("Cdn_ActOn"))) return;
        try
        {
            string result = CdnFix.Apply();
            _settingsPage.SetCdnStatus(CdnFix.IsApplied(), result);
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("Cdn_OnErr") + ex.Message, ToastKind.Warn);
        }
    }

    private void CdnRollback()
    {
        if (!EnsureAdmin(Lang.Get("Cdn_ActOff"))) return;
        try
        {
            string result = CdnFix.Rollback();
            _settingsPage.SetCdnStatus(CdnFix.IsApplied(), result);
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("Cdn_OffErr") + ex.Message, ToastKind.Warn);
        }
    }

    private void ApplyTelemetryBlock(bool enable)
    {
        try
        {
            bool ok = TelemetryBlocker.Set(enable);
            if (ok)
            {
                _settingsPage.SetTelemetryStatus(
                    Lang.Get(enable ? "Set_Telemetry_On" : "Set_Telemetry_Off"), true);
            }
            else
            {
                _settingsPage.SetTelemetryStatus(Lang.Get("Set_Telemetry_Fail"), false);
                Toast(Lang.Get("Set_Telemetry_Fail"), ToastKind.Warn);
            }
        }
        catch (Exception ex)
        {
            _settingsPage.SetTelemetryStatus(Lang.Get("Set_Telemetry_Fail") + " " + ex.Message, false);
        }
    }

    private bool EnsureAdmin(string action)
    {
        if (CdnFix.IsAdmin()) return true;
        if (MessageBox.Show(
                Lang.Format("Cdn_AdminFmt", action),
                "NekoStrap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return false;
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe == null) return false;
            Process.Start(new ProcessStartInfo(exe)
            {
                Verb = "runas",
                UseShellExecute = true
            });
            Close();
        }
        catch { /* отказались в UAC — остаёмся */ }
        return false;
    }

    // ---------- Обои и стекло ----------

    private static readonly string[] WallpaperExts =
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

    /// <summary>
    /// Декод картинки в BitmapSource без лока файла. Webp WPF не умеет —
    /// его тянем через SkiaSharp в PNG-байты. Гифки идут через GifPlayer.
    /// </summary>
    private static System.Windows.Media.Imaging.BitmapSource? DecodeWallpaper(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (!WallpaperExts.Contains(ext))
            throw new InvalidOperationException(Lang.Get("Wall_BadFmt"));
        Stream stream;
        MemoryStream? owned = null;
        if (ext == ".webp")
        {
            using var sk = SkiaSharp.SKBitmap.Decode(path);
            if (sk == null)
                throw new InvalidOperationException(Lang.Get("Wall_BadWebp"));
            using var img = SkiaSharp.SKImage.FromBitmap(sk);
            using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            owned = new MemoryStream(data.ToArray());
            stream = owned;
        }
        else
        {
            owned = new MemoryStream(File.ReadAllBytes(path));
            stream = owned;
        }
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.StreamSource = stream;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        finally
        {
            owned?.Dispose();
        }
    }

    private void PickWallpaper()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Lang.Get("Dlg_WallTitle"),
            Filter = $"{Lang.Get("Dlg_Photo")} (*.jpg;*.png;*.gif;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            // Пробная загрузка — битый файл отсекаем до сохранения в конфиг.
            // Гифки проверяет плеер (там же проверка на анимацию не нужна —
            // статичная гифка тоже показывается первым кадром).
            if (Path.GetExtension(dlg.FileName).ToLowerInvariant() == ".gif")
            {
                using var trial = GifPlayer.TryCreate(dlg.FileName, WallpaperImage);
                if (trial == null)
                    throw new InvalidOperationException(Lang.Get("Wall_BadGif"));
            }
            else
            {
                DecodeWallpaper(dlg.FileName);
            }
            _config.WallpaperPath = dlg.FileName;
            SaveQuiet();
            ApplyWallpaper();
            RefreshWallpaperSettings();
            SetStatus(Lang.Format("Wall_SetFmt", Path.GetFileName(dlg.FileName)), true);
        }
        catch (Exception ex)
        {
            Toast(Lang.Format("Wall_LoadErrFmt", ex.Message), ToastKind.Warn);
        }
    }

    private void ResetWallpaper()
    {
        _config.WallpaperPath = "";
        SaveQuiet();
        ApplyWallpaper();
        RefreshWallpaperSettings();
        SetStatus(Lang.Get("Wall_Reset"), true);
    }

    /// <summary>
    /// Живое изменение блюра: только радиус GPU-эффекта, без перезагрузки.
    /// Сюда же подмешан «блюр подложки» стекла: панели полупрозрачные,
    /// сквозь них виден фон — чем сильнее размыт фон, тем стекляннее вид.
    /// </summary>
    private void ApplyWallpaperBlur()
    {
        UpdateWallpaperBlur();
    }

    private void UpdateWallpaperBlur()
    {
        // Только блюр фото. Блюр подложки стекла — свой, под каждой панелью (см. Glass).
        WallpaperBlur.Radius = Math.Clamp(_config.WallpaperBlur, 0, 100) * 0.25;
    }

    private void RefreshWallpaperSettings()
    {
        string path = _config.WallpaperPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _settingsPage.SetWallpaperInfo("", null, _config.WallpaperBlur);
            _settingsPage.SetWallpaperDim(_config.WallpaperDim);
            return;
        }
        System.Windows.Media.Imaging.BitmapSource? thumb = null;
        try { thumb = MakeWallpaperThumb(path); } catch { /* ignore */ }
        _settingsPage.SetWallpaperInfo(Path.GetFileName(path), thumb, _config.WallpaperBlur);
        _settingsPage.SetWallpaperDim(_config.WallpaperDim);
    }

    private static System.Windows.Media.Imaging.BitmapSource? MakeWallpaperThumb(string path)
    {
        var full = DecodeWallpaper(path);
        if (full == null || full.PixelWidth <= 0) return full;
        double scale = 320.0 / full.PixelWidth;
        var thumb = new System.Windows.Media.Imaging.TransformedBitmap(full,
            new System.Windows.Media.ScaleTransform(scale, scale));
        thumb.Freeze();
        return thumb;
    }

    /// <summary>Стекло живьём: тинты + блюр подложки под каждой панелью.</summary>
    private void ApplyGlassToUi()
    {
        UiTheme.GlassBlur = _config.GlassBlur;
        UiTheme.GlassDim = _config.GlassDim;
        UiTheme.GlassPure = _config.GlassPure;
        UiTheme.Apply(_hasWallpaper, _config.GlassEnabled, _config.GlassOpacity);
        UpdateWallpaperBlur();
        // Страховка сходимости: раз в секунду дотягиваем сэмплы, если стекло
        // активно. Update change-aware — без изменений визуал не трогается,
        // так что тик почти бесплатный.
        if (Glass.Active)
        {
            _glassTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _glassTimer.Tick -= GlassTimer_Tick;
            _glassTimer.Tick += GlassTimer_Tick;
            _glassTimer.Start();
        }
        else
        {
            _glassTimer?.Stop();
        }
    }

    private void GlassTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (Glass.Active && !_closed)
                Glass.NotifySettingsChanged();
        }
        catch { /* ignore */ }
    }

    private void RefreshGlassSettings()
    {
        _settingsPage.SetGlassInfo(_config.GlassEnabled, _config.GlassOpacity, _config.GlassBlur, _config.GlassDim, _config.GlassPure);
    }

    // ================= Обновления лаунчера =================

    /// <summary>Тихая проверка при старте: только статус на Главной, без скачивания.</summary>
    private void CheckForAppUpdatesQuiet()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var rel = await AppUpdater.GetLatestAsync();
                if (rel == null || !AppUpdater.IsNewer(AppInfo.Version, rel.Tag)) return;
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_closed) return;
                    SetStatus(Lang.Format("Upd_AppAvailFmt", rel.Tag), false);
                }));
            }
            catch { /* без сети — молча */ }
        });
    }

    private async Task CheckForAppUpdatesAsync(bool manual)
    {
        _aboutPage.ShowDownloadButton(false);
        _aboutPage.SetReleaseNotes("");
        _aboutPage.SetUpdateStatus(Lang.Get("Upd_Checking"));
        try
        {
            var rel = await AppUpdater.GetLatestAsync();
            if (_closed) return;
            if (rel == null)
            {
                _aboutPage.SetUpdateStatus(Lang.Format("Upd_FailFmt",
                    manual ? Lang.Get("Upd_FailNet") : ""));
                return;
            }
            if (!AppUpdater.IsNewer(AppInfo.Version, rel.Tag))
            {
                _pendingRelease = null;
                _aboutPage.SetUpdateStatus(Lang.Format("Upd_FreshFmt", AppInfo.Version, rel.Tag));
                return;
            }
            _pendingRelease = rel;
            string when = rel.PublishedAt == default ? ""
                : $" ({rel.PublishedAt.ToLocalTime().ToString("d MMM yyyy", Lang.Culture)})";
            _aboutPage.SetUpdateStatus(Lang.Format("Upd_AvailFmt", rel.Tag, when, AppInfo.Version));
            _aboutPage.SetReleaseNotes(rel.Notes);
            _aboutPage.ShowDownloadButton(true);
        }
        catch (Exception ex)
        {
            if (!_closed)
                _aboutPage.SetUpdateStatus(Lang.Format("Upd_FailFmt", ": " + ex.Message));
        }
    }

    private async Task DownloadAndInstallAsync()
    {
        var rel = _pendingRelease;
        if (rel == null) return;
        _aboutPage.ShowDownloadButton(false);
        try
        {
            string dest = Path.Combine(Path.GetTempPath(), $"NekoStrap-{rel.Tag}.exe");
            var progress = new Progress<(long done, long total)>(t =>
            {
                if (_closed) return;
                string detail = t.total > 0
                    ? Lang.Format("Size_MbFmt", t.done / 1048576.0) + " / "
                        + Lang.Format("Size_MbFmt", t.total / 1048576.0)
                    : Lang.Format("Size_MbFmt", t.done / 1048576.0);
                _aboutPage.SetUpdateStatus(Lang.Format("Upd_DlFmt", rel.Tag, detail));
            });
            await AppUpdater.DownloadAsync(rel.DownloadUrl, dest, progress, CancellationToken.None);
            if (_closed) return;
            _aboutPage.SetUpdateStatus(Lang.Get("Upd_Done"));
            await Task.Delay(800);
            AppUpdater.InstallAndRestart(dest);
            _reallyExit = true;
            Close();
        }
        catch (Exception ex)
        {
            if (!_closed)
            {
                _aboutPage.SetUpdateStatus(Lang.Get("Upd_DlErr") + ex.Message);
                _aboutPage.ShowDownloadButton(true);
            }
        }
    }

    // ================= Трей =================

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyExit && _config.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
        }
        else
        {
            ClickSound.PlayClose();
            SaveWindowBounds();
        }
    }

    private void HideToTray()
    {
        Hide();
        if (_config.NotificationsEnabled)
            _trayIcon?.ShowBalloonTip(1500, "NekoStrap",
                Lang.Get("Tray_Minimized"),
                WinForms.ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void InitTray()
    {
        try { _trayIcon?.Dispose(); } catch { /* ignore */ }
        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = MakeTrayIcon(),
            Text = "NekoStrap",
            Visible = true
        };

        // Стиль тот же, что в окне: палитра, шрифты и подсветка берутся из
        // ресурсов WPF при каждой отрисовке (см. TrayMenuRenderer).
        var menu = new WinForms.ContextMenuStrip { Renderer = new TrayMenuRenderer() };
        menu.Font = TrayFont(heading: false);
        // Иконок в пунктах нет — пустая «иконная» колонка только раздувает
        // меню; галочки у тумблеров уводим в собственное поле слева.
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = true;

        menu.Items.Add(TrayHeader("NekoStrap"));
        menu.Items.Add(new WinForms.ToolStripSeparator());

        menu.Items.Add(TrayAction(Lang.Get("Tray_Open"), (_, _) => ShowFromTray()));
        menu.Items.Add(TrayAction(Lang.Get("Btn_Play"), async (_, _) =>
        {
            ShowFromTray();
            await PlayFlowAsync();
        }));
        menu.Items.Add(new WinForms.ToolStripSeparator());

        // ---- Сервер ----
        menu.Items.Add(TraySection(Lang.Get("Tray_SecServer")));
        menu.Items.Add(TrayAction(Lang.Get("Tray_ServerInfo"), (_, _) =>
        {
            ShowFromTray();
            ShowServerInfoDialog();
        }));
        menu.Items.Add(TrayAction(Lang.Get("Tray_Rejoin"), async (_, _) =>
        {
            ShowFromTray();
            await RejoinSameServerAsync();
        }));
        menu.Items.Add(TrayAction(Lang.Get("Tray_Hop"), async (_, _) =>
        {
            ShowFromTray();
            await ServerHopAsync();
        }));
        menu.Items.Add(new WinForms.ToolStripSeparator());

        // ---- Медиа ----
        menu.Items.Add(TraySection(Lang.Get("Tray_SecMedia")));
        menu.Items.Add(BuildMusicMenu());
        menu.Items.Add(BuildColorMenu());
        menu.Items.Add(new WinForms.ToolStripSeparator());

        // ---- Общение ----
        menu.Items.Add(TraySection(Lang.Get("Tray_SecTalk")));
        menu.Items.Add(TrayAction(Lang.Get("Inv_Title"), (_, _) => OpenHomePageFromTray()));
        var chatOverlayItem = new WinForms.ToolStripMenuItem(Lang.Get("Tray_ChatOverlayOn"))
        {
            Checked = _config.ChatOverlayOnJoin,
            CheckOnClick = true
        };
        chatOverlayItem.CheckedChanged += (_, _) =>
        {
            _config.ChatOverlayOnJoin = chatOverlayItem.Checked;
            _chatPage.ChatOverlayCheck.IsChecked = chatOverlayItem.Checked;
            SaveQuiet();
        };
        menu.Items.Add(chatOverlayItem);
        menu.Items.Add(TrayAction(Lang.Get("Tray_ChatOpen"), (_, _) => ShowChatOverlay()));
        menu.Items.Add(TrayAction(Lang.Get("Tray_ChatPage"), (_, _) =>
        {
            ShowFromTray();
            NavChat.IsChecked = true;
        }));
        menu.Items.Add(new WinForms.ToolStripSeparator());

        // ---- Лаунчер ----
        menu.Items.Add(TraySection(Lang.Get("Tray_SecApp")));
        menu.Items.Add(TrayAction(Lang.Get("Nav_Settings"), (_, _) =>
        {
            ShowFromTray();
            NavSettings.IsChecked = true;
        }));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(TrayAction(Lang.Get("Tray_Exit"), (_, _) =>
        {
            _reallyExit = true;
            Close();
        }));

        // Подменю живут в собственных ToolStripDropDown — рендерер иначе
        // остаётся системным и выбивается из темы.
        foreach (WinForms.ToolStripItem it in menu.Items)
        {
            if (it is WinForms.ToolStripMenuItem mi && mi.HasDropDownItems)
                mi.DropDown.Renderer = menu.Renderer;
        }

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    /// <summary>Шапка меню: заголовок набран шрифтом заголовков лаунчера.</summary>
    private static WinForms.ToolStripMenuItem TrayHeader(string text)
    {
        var item = new WinForms.ToolStripMenuItem(text)
        {
            Enabled = false,
            Tag = TrayMenuRenderer.AccentTag,
            Font = TrayFont(heading: true),
            Padding = new System.Windows.Forms.Padding(6, 4, 6, 4),
        };
        return item;
    }

    /// <summary>Подзаголовок секции: приглушённый, капслоком — группировка
    /// вместо сплошного списка однообразных пунктов.</summary>
    private static WinForms.ToolStripMenuItem TraySection(string text)
    {
        var item = new WinForms.ToolStripMenuItem(text.ToUpperInvariant())
        {
            Enabled = false,
            Padding = new System.Windows.Forms.Padding(6, 6, 6, 2),
        };
        return item;
    }

    private static WinForms.ToolStripMenuItem TrayAction(string text, EventHandler onClick)
    {
        var item = new WinForms.ToolStripMenuItem(text) { Padding = new System.Windows.Forms.Padding(6, 3, 6, 3) };
        item.Click += onClick;
        return item;
    }

    /// <summary>Шрифт меню = шрифт интерфейса (Inter/Unbounded из ресурсов).</summary>
    private static System.Drawing.Font TrayFont(bool heading)
    {
        string family = FontName(heading ? "HeadingFont" : "UiFont", "Segoe UI");
        var style = heading ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular;
        try { return new System.Drawing.Font(family, heading ? 11.5f : 9.5f, style); }
        catch { return new System.Drawing.Font("Segoe UI", 9.5f, style); }
    }

    private static string FontName(string key, string fallback)
    {
        try
        {
            if (Application.Current?.Resources?[key] is System.Windows.Media.FontFamily ff)
            {
                foreach (var kv in ff.FamilyNames)
                    if (!string.IsNullOrWhiteSpace(kv.Value)) return kv.Value;
            }
        }
        catch { /* ignore */ }
        return fallback;
    }

    private void OpenHomePageFromTray()
    {
        ShowFromTray();
        NavHome.IsChecked = true;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static System.Drawing.Icon MakeTrayIcon()
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var bg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xF2, 0xF2, 0xEF));
            g.FillEllipse(bg, 1, 1, 30, 30);
            using var tb = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x0A, 0x0A, 0x0B));
            using var font = new System.Drawing.Font("Segoe UI", 17f, System.Drawing.FontStyle.Bold);
            var sf = new System.Drawing.StringFormat
            {
                Alignment = System.Drawing.StringAlignment.Center,
                LineAlignment = System.Drawing.StringAlignment.Center
            };
            g.DrawString("N", font, tb, new System.Drawing.RectangleF(0, 1, 32, 30), sf);
        }
        // Icon.FromHandle не владеет хэндлом: клонируем и гасим оригинал —
        // InitTray зовётся при каждой смене языка, без этого GDI течёт.
        IntPtr hicon = bmp.GetHicon();
        try
        {
            using var owned = System.Drawing.Icon.FromHandle(hicon);
            return (System.Drawing.Icon)owned.Clone();
        }
        finally
        {
            DestroyIcon(hicon);
        }
    }

    /// <summary>NekoStrap Music в трее: управление + список треков из папки Music.</summary>
    private WinForms.ToolStripMenuItem BuildMusicMenu()
    {
        var music = new WinForms.ToolStripMenuItem(Lang.Get("Tray_Music"));
        music.DropDownOpening += (_, _) =>
        {
            music.DropDownItems.Clear();
            try
            {
                _music.Refresh();
                string now = _music.CurrentTitle;
                var header = new WinForms.ToolStripMenuItem(
                    now.Length > 0 ? "♪ " + now : Lang.Get("Tray_MusicEmpty"))
                {
                    Enabled = false
                };
                music.DropDownItems.Add(header);
                music.DropDownItems.Add(new WinForms.ToolStripSeparator());
                music.DropDownItems.Add(_music.IsPlaying ? Lang.Get("Tray_Pause") : Lang.Get("Btn_Play"), null,
                    (_, _) => _music.PlayPause());
                music.DropDownItems.Add(Lang.Get("Tray_Next"), null, (_, _) => _music.Next());
                music.DropDownItems.Add(Lang.Get("Tray_Prev"), null, (_, _) => _music.Prev());
                music.DropDownItems.Add(Lang.Get("Tray_Stop"), null, (_, _) => _music.Stop());
                var shuffle = new WinForms.ToolStripMenuItem(Lang.Get("Tray_Shuffle"))
                {
                    Checked = _music.Shuffle,
                    CheckOnClick = true
                };
                shuffle.CheckedChanged += (_, _) => _music.Shuffle = shuffle.Checked;
                music.DropDownItems.Add(shuffle);
                music.DropDownItems.Add(Lang.Get("Tray_Louder"), null,
                    (_, _) => _music.SetVolume(_music.Volume + 0.1f));
                music.DropDownItems.Add(Lang.Get("Tray_Quieter"), null,
                    (_, _) => _music.SetVolume(_music.Volume - 0.1f));
                music.DropDownItems.Add(new WinForms.ToolStripSeparator());

                int shown = 0;
                for (int i = 0; i < _music.Tracks.Count && shown < 12; i++, shown++)
                {
                    var track = _music.Tracks[i];
                    var item = new WinForms.ToolStripMenuItem((i == _music.CurrentIndex ? "▶ " : "") + track.Title)
                    {
                        Tag = i
                    };
                    item.Click += (_, _) =>
                    {
                        if (item.Tag is int idx) _music.PlayAt(idx);
                    };
                    music.DropDownItems.Add(item);
                }
                if (_music.Tracks.Count > shown)
                    music.DropDownItems.Add(new WinForms.ToolStripMenuItem(
                        Lang.Format("Tray_MoreFmt", _music.Tracks.Count - shown)) { Enabled = false });

                music.DropDownItems.Add(new WinForms.ToolStripSeparator());
                music.DropDownItems.Add(Lang.Get("Tray_OpenMusic"), null, (_, _) =>
                {
                    try
                    {
                        Directory.CreateDirectory(_music.MusicDir);
                        Process.Start(new ProcessStartInfo(
                            "explorer.exe", $"\"{_music.MusicDir}\"") { UseShellExecute = true });
                    }
                    catch { /* ignore */ }
                });
            }
            catch { /* ignore */ }
        };
        return music;
    }

    /// <summary>Цветокоррекция в трее: вкл, пресеты, авто-режим, тюнер.</summary>
    private WinForms.ToolStripMenuItem BuildColorMenu()
    {
        var root = new WinForms.ToolStripMenuItem(Lang.Get("Tray_Color"));
        root.DropDownOpening += (_, _) =>
        {
            root.DropDownItems.Clear();
            try
            {
                var state = new WinForms.ToolStripMenuItem(
                    !_config.ColorFxEnabled ? Lang.Get("Color_Off")
                    : _config.ColorFxAuto ? (IsRobloxForeground() ? Lang.Get("Color_OnGame") : Lang.Get("Color_WaitGame"))
                    : Lang.Get("Color_OnAlways"))
                { Enabled = false };
                root.DropDownItems.Add(state);
                root.DropDownItems.Add(new WinForms.ToolStripSeparator());

                var on = new WinForms.ToolStripMenuItem(Lang.Get("Tune_On_L"))
                    { Checked = _config.ColorFxEnabled, CheckOnClick = true };
                on.CheckedChanged += (_, _) =>
                {
                    _config.ColorFxEnabled = on.Checked;
                    SaveQuiet();
                    UpdateColorFx();
                };
                root.DropDownItems.Add(on);

                var auto = new WinForms.ToolStripMenuItem(Lang.Get("Color_Auto"))
                    { Checked = _config.ColorFxAuto, CheckOnClick = true };
                auto.CheckedChanged += (_, _) =>
                {
                    _config.ColorFxAuto = auto.Checked;
                    SaveQuiet();
                    UpdateColorFx();
                };
                root.DropDownItems.Add(auto);
                root.DropDownItems.Add(new WinForms.ToolStripSeparator());

                void Preset(string name, ColorSettings s)
                {
                    var item = new WinForms.ToolStripMenuItem(name);
                    item.Click += (_, _) => ApplyColorPreset(s);
                    root.DropDownItems.Add(item);
                }
                Preset(Lang.Get("Tune_Standard"), ColorSettings.Default);
                Preset(Lang.Get("Tune_Cinema"), ColorSettings.Cinema);
                Preset(Lang.Get("Tune_Vivid"), ColorSettings.Vivid);
                Preset(Lang.Get("Tune_Mono"), ColorSettings.Mono);
                root.DropDownItems.Add(new WinForms.ToolStripSeparator());
                root.DropDownItems.Add(Lang.Get("Tray_Tune"), null, (_, _) => OpenColorTuner());
            }
            catch { /* ignore */ }
        };
        return root;
    }

    /// <summary>
    /// Окно настройки с живыми ползунками поверх игры. Пока открыто —
    /// эффект принудительно включён, чтобы было с чем сравнивать.
    /// </summary>
    private void OpenColorTuner()
    {
        try
        {
            if (_tuneWindow != null)
            {
                _tuneWindow.Focus();
                return;
            }
            _tuningOpen = true;
            _tuneWindow = new ColorTuneWindow(_config, ColorChanged, () =>
            {
                _tuningOpen = false;
                _tuneWindow = null;
                UpdateColorFx();
            });
            _tuneWindow.Closed += (_, _) =>
            {
                _tuningOpen = false;
                _tuneWindow = null;
                UpdateColorFx();
            };
            _tuneWindow.Show();
            UpdateColorFx();
        }
        catch { /* ignore */ }
    }

    private void ApplyColorPreset(ColorSettings s)
    {
        _config.ColorFxSat = s.Saturation;
        _config.ColorFxBri = s.Brightness;
        _config.ColorFxCon = s.Contrast;
        _config.ColorFxTemp = s.Temperature;
        _config.ColorFxEnabled = true;
        ColorChanged();
    }

    private void ColorChanged()
    {
        SaveQuiet();
        UpdateColorFx();
    }

    // ---------- Серверхоп / перезаход из трея ----------

    /// <summary>
    /// Текущий плейс: живой сессии доверяем больше, чем запомненному в конфиге.
    /// </summary>
    private long CurrentHopPlaceId()
    {
        try
        {
            var cur = _watcher.Current;
            if (cur != null && cur.PlaceId > 0) return cur.PlaceId;
        }
        catch { /* ignore */ }
        return _config.LastGamePlaceId;
    }

    private static bool IsPlayerRunning(Process? p)
    {
        try { return p != null && !p.HasExited; } catch { return false; }
    }

    /// <summary>
    /// Гасит текущий клиент перед хопом/перезаходом, чтобы новый запуск
    /// точно создал свежую игру, а не подцепился к старой.
    /// </summary>
    private async Task KillCurrentPlayerAsync()
    {
        var p = _playerProcess;
        if (!IsPlayerRunning(p)) return;
        try
        {
            SetStatus(Lang.Get("Hop_Closing"), false);
            p!.Kill();
            try { await p.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { /* не дождались — запускаем всё равно */ }
            catch { /* ignore */ }
            await Task.Delay(600);
        }
        catch (Exception ex)
        {
            Toast(Lang.Get("Hop_KillErr") + ex.Message, ToastKind.Warn);
        }
    }

    /// <summary>
    /// «Другой сервер этого плейса»: убивает текущий и заходит на
    /// случайный сервер того же PlaceId. Кнопка живёт в трее.
    /// </summary>
    private async Task ServerHopAsync()
    {
        if (_busy) return;
        long placeId = CurrentHopPlaceId();
        if (placeId <= 0)
        {
            Toast(Lang.Get("Hop_NoPlace"), ToastKind.Info);
            return;
        }
        _busy = true;
        _homePage.SetBusy(true);
        try
        {
            await KillCurrentPlayerAsync();
            LaunchPlaceById(placeId);
        }
        finally
        {
            _homePage.SetBusy(false);
            _busy = false;
        }
    }

    /// <summary>
    /// «Перезайти (тот же сервер)»: тот же PlaceId + тот же JobId
    /// из живой сессии или из лога последних заходов.
    /// </summary>
    private async Task RejoinSameServerAsync()
    {
        if (_busy) return;
        long placeId = CurrentHopPlaceId();
        if (placeId <= 0)
        {
            Toast(Lang.Get("Hop_NoRejoin"), ToastKind.Info);
            return;
        }
        string jobId = "";
        try
        {
            var cur = _watcher.Current;
            if (cur != null && cur.PlaceId == placeId && cur.JobId.Length > 0)
                jobId = cur.JobId;
            if (jobId.Length == 0)
            {
                jobId = _recent.List()
                    .Where(s => s.PlaceId == placeId && s.JobId.Length > 0)
                    .OrderByDescending(s => s.JoinedAt)
                    .Select(s => s.JobId)
                    .FirstOrDefault() ?? "";
            }
        }
        catch { /* ignore */ }
        if (jobId.Length == 0)
        {
            if (MessageBox.Show(Lang.Get("Common_NoJob"),
                    "NekoStrap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
        }
        _busy = true;
        _homePage.SetBusy(true);
        try
        {
            await KillCurrentPlayerAsync();
            LaunchServerByJob(placeId, jobId);
        }
        finally
        {
            _homePage.SetBusy(false);
            _busy = false;
        }
    }

    private void ShowServerInfoDialog()
    {
        var session = _watcher.Current;
        bool playing = false;
        try { playing = _playerProcess != null && !_playerProcess.HasExited; } catch { /* ignore */ }
        if (!playing || session == null)
        {
            MessageBox.Show(Lang.Get("Srv_NotPlaying"),
                Lang.Get("Srv_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string dash = "—";
        string text =
            $"{Lang.Get("Srv_Game")}: {(_lastGameName.Length > 0 ? _lastGameName : dash)}\n" +
            $"PlaceId: {session.PlaceId}\n" +
            $"IP: {_lastServerIp}\n" +
            $"{Lang.Get("Srv_Geo")}: {_lastGeoText}\n" +
            $"{Lang.Get("Srv_Ping")}: {_lastPingText}\n" +
            $"JobId: {(session.JobId.Length > 0 ? session.JobId : dash)}";
        if (MessageBox.Show(text + Lang.Get("Srv_CopyAsk"), Lang.Get("Srv_Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            try { Clipboard.SetText(text); } catch { /* ignore */ }
        }
    }

    /// <summary>Всплывашка о сервере + пункт в трее.</summary>
    private void ShowServerBalloon()
    {
        if (!_config.NotificationsEnabled) return;
        try
        {
            _trayIcon?.ShowBalloonTip(4000, Lang.Get("Tray_ServerBalloon"),
                $"{(_lastGameName.Length > 0 ? _lastGameName : "Roblox")}\n" +
                $"{_lastServerIp}  •  {_lastGeoText}  •  {_lastPingText}",
                WinForms.ToolTipIcon.Info);
        }
        catch { /* ignore */ }
    }
}


