using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    private readonly Media.MusicManager _music = new();
    private readonly ChatClient _chat = new();
    private Process? _playerProcess;
    private WinForms.NotifyIcon? _trayIcon;
    private ColorTuneWindow? _tuneWindow;
    private ChatOverlayWindow? _chatOverlay;
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
        _homePage.FavoritePlayClicked += (placeId) => LaunchPlaceById(placeId);
        _homePage.FavoriteRemoveClicked += (placeId) => RemoveFavorite(placeId);

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

        _flagsPage.AddFlagClicked += (_, _) => _flagsPage.AddNewFlag();
        _flagsPage.ProfileApplyClicked += (name) => ApplyFlagProfile(name);
        _flagsPage.ProfileSaveClicked += (_, _) => SaveFlagProfile();
        _flagsPage.ProfileDeleteClicked += (_, _) => DeleteFlagProfile();
        _flagsPage.DeleteAllClicked += (_, _) => DeleteAllFlags();
        _flagsPage.SaveClicked += (_, _) => SaveFlags();
        _flagsPage.ImportClicked += (_, _) => ImportFlags();
        _flagsPage.ExportClicked += (_, _) => ExportFlags();

        _versionsPage.ActivateClicked += (_, _) => ActivateSelectedVersion();
        _versionsPage.DeleteClicked += (_, _) => DeleteSelectedVersion();
        _versionsPage.StudioInstallClicked += async (_, _) => await InstallStudioAsync();
        _versionsPage.StudioLaunchClicked += (_, _) => LaunchStudio();

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
            _homePage.SetStatus(Lang.Get("Set_Saved"), true);
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
            ApplyDiscord();
            UpdateColorFx();
            _music.LoadState();
            CheckForUpdatesQuiet();
            CheckForAppUpdatesQuiet();
            ApplyConfigToUi();
            RefreshFavorites();
            ApplySoundsFromConfig();
            RefreshAppearanceSettings();
            UiTheme.ApplyText(_config);
            RefreshWallpaperSettings();
            RefreshGlassSettings();
            FooterText.Text = $"v{AppInfo.Version}  •  {Lang.Get("Footer_Ready")}";
            NavHome.IsChecked = true;
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized && _config.MinimizeToTray)
                HideToTray();
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closed = true;
            try { _chatOverlay?.Close(); } catch { /* ignore */ }
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
        // Стекло дотягиваем отложенно, после layout свежей страницы:
        // тогда у всех панелей уже есть размеры и семплы встают сразу.
        _ = Dispatcher.BeginInvoke(new Action(() => Glass.NotifySettingsChanged()),
            DispatcherPriority.Render);
        // Обновление данных при входе в раздел (дешёвые локальные чтения).
        try
        {
            if (btn == NavMods)
            {
                RefreshMods();
                RefreshFleasionStatus();
            }
            else if (btn == NavFlags)
            {
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
            _homePage.SetStatus(Lang.Get("Upd_SectFail") + " " + ex.Message, false);
        }
    }

    private void MinButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
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
            MessageBox.Show(Lang.Get("Play_Nothing"),
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show(Lang.Get("Play_NoRoblox"),
                    "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _watcher.Clear();
            _homePage.SetStatus(Lang.Get("Play_OpenPlace"), false);
            Process? process = RobloxLauncher.LaunchPlace(exe, placeId);
            if (process == null)
                throw new InvalidOperationException(Lang.Get("Play_LaunchFail"));
            AfterLaunch(process);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Lang.Get("Play_OpenPlaceErr") + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Общее после любого запуска игры: трекинг, моды, Discord, таймеры.</summary>
    private void AfterLaunch(Process process)
    {
        TrackPlayer(process);
        FleasionManager.LaunchIfEnabled(_config.FleasionEnabled);
        _discord.SetIdle();
        _homePage.SetStatus(Lang.Get("Play_Launched"), true);
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
                _homePage.SetStatus(FormatInstall(p), false);
                _homePage.SetProgress(p.Fraction);
            });

            string oldGuid = _config.InstalledVersion;
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

            _homePage.SetStatus(Lang.Get("Play_Starting"), false);
            _homePage.SetProgress(null);
            _watcher.Clear();
            Process? process = RobloxLauncher.Launch(exe);
            if (process == null)
                throw new InvalidOperationException(Lang.Get("Play_LaunchFail"));
            AfterLaunch(process);
        }
        catch (OperationCanceledException)
        {
            _homePage.SetStatus(Lang.Get("Common_Cancelled"), false);
        }
        catch (Exception ex)
        {
            _homePage.SetStatus(Lang.Get("Common_Error"), false);
            MessageBox.Show(Lang.Get("Play_StartErr") + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _homePage.SetProgress(null);
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
                        _homePage.SetStatus(Lang.Get("Upd_RobloxAvail"), false);
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

    // ---------- Трекинг игры: сервер + Discord ----------

    private void TrackPlayer(Process process)
    {
        try { _playerProcess?.Dispose(); } catch { /* ignore */ }
        _playerProcess = process;
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
        FlushSessionTime();
        _recent.EndAll();
        _watcher.Clear();
        _ = _chat.DisconnectAsync();
        try { _chatOverlay?.Hide(); } catch { /* ignore */ }
        _homePage.ClearServer();
        _lastServerIp = "";
        _lastGeoText = "—";
        _lastPingText = "—";
        _lastGameName = "";
        _discord.SetIdle();
        _serverTimer.Stop();
        _homePage.SetStatus(Lang.Get("Play_Closed"), true);
        RefreshHomeMeta();
        try { _playerProcess?.Dispose(); } catch { /* ignore */ }
        _playerProcess = null;
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
        SyncChatWithSession();
        if (_config.ChatEnabled && _config.ChatOverlayOnJoin &&
            session.PlaceId > 0 && session.JobId.Length > 0)
            ShowChatOverlay();
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
                        session.PlaceId, session.JobId);
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

    private void ApplyDiscord()
    {
        if (_config.DiscordRpc && _config.DiscordAppId.Length > 0)
            _discord.Configure(_config.DiscordAppId, _config.DiscordAllowJoin);
        else
            _discord.Shutdown();
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
        _homePage.SetStatus(Lang.Get("Theme_ResetDone"), true);
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
        _chatPage.SetState(_chat.State);
        RefreshChatHint();
        FooterText.Text = $"v{AppInfo.Version}  •  {Lang.Get("Footer_Ready")}";
        InitTray();
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
            _homePage.SetStatus(Lang.Format("Snd_SetFmt", title), true);
        }
        else
        {
            MessageBox.Show(Lang.Format("Snd_Err", error),
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        _modsPage.SetMods(ModManager.List());
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
            RefreshMods();
            RefreshHomeMeta();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Lang.Get("Mod_AddErr") + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ToggleSelectedMods()
    {
        var selected = _modsPage.SelectedModPaths();
        if (selected.Count == 0)
        {
            MessageBox.Show(Lang.Get("Mod_SelOne"), "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            foreach (var rel in selected)
            {
                string full = Path.Combine(RobloxPaths.ModsDir, rel);
                if (!File.Exists(full)) continue;
                if (full.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    File.Move(full, full[..^".disabled".Length]);
                else
                    File.Move(full, full + ".disabled");
            }
            RefreshMods();
            RefreshHomeMeta();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Lang.Get("Mod_ToggleErr") + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteSelectedMods()
    {
        var selected = _modsPage.SelectedModPaths();
        if (selected.Count == 0)
        {
            MessageBox.Show(Lang.Get("Mod_SelOne"), "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(Lang.Format("Mod_DelConfirm", selected.Count),
                "NekoStrap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            foreach (var rel in selected)
            {
                string full = Path.Combine(RobloxPaths.ModsDir, rel);
                // Защита от выхода за папку модов.
                string root = Path.GetFullPath(RobloxPaths.ModsDir);
                if (!Path.GetFullPath(full).StartsWith(root + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                if (File.Exists(full)) File.Delete(full);
            }
            RefreshMods();
            RefreshHomeMeta();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Lang.Get("Common_DelErr") + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(Lang.Get("Hist_SelGame"), "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        bool existed = FavoritesStore.Contains(placeId);
        FavoritesStore.Add(placeId, name);
        RefreshFavorites();
        _homePage.SetStatus(existed
            ? Lang.Get("Fav_Existed")
            : Lang.Format("Fav_AddedFmt",
                name.Length > 0 ? name : Lang.Format("Common_PlaceFmt", placeId)), true);
    }

    private void RemoveFavorite(long placeId)
    {
        FavoritesStore.Remove(placeId);
        RefreshFavorites();
        _homePage.SetStatus(Lang.Get("Fav_Removed"), true);
    }

    // ================= FastFlags =================

    private void RefreshFlagProfiles()
    {
        _flagsPage.SetProfiles(FlagProfiles.List());
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
        _homePage.SetStatus(Lang.Format("Prof_AppliedFmt", name, flags.Count), false);
    }

    private void SaveFlagProfile()
    {
        string name = _flagsPage.ProfileNameText;
        if (name.Length == 0)
        {
            MessageBox.Show(Lang.Get("Prof_NeedName"), "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var flags = _flagsPage.CollectFlags();
        if (flags.Count == 0)
        {
            MessageBox.Show(Lang.Get("Prof_EmptyTable"), "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        FlagProfiles.SaveProfile(name, flags);
        _flagsPage.ClearProfileName();
        RefreshFlagProfiles();
        _homePage.SetStatus(Lang.Format("Prof_SavedFmt", name), true);
    }

    private void DeleteFlagProfile()
    {
        string name = _flagsPage.ProfileNameText;
        if (name.Length == 0)
        {
            MessageBox.Show(Lang.Get("Prof_NeedNameDel"), "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (FlagProfiles.Get(name) == null)
        {
            MessageBox.Show(Lang.Format("Prof_NoSuchFmt", name), "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(Lang.Format("Prof_DelConfirm", name), "NekoStrap",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        FlagProfiles.Delete(name);
        _flagsPage.ClearProfileName();
        RefreshFlagProfiles();
        _homePage.SetStatus(Lang.Format("Prof_DeletedFmt", name), true);
    }

    private void DeleteAllFlags()
    {
        if (_flagsPage.CollectFlags().Count == 0) return;
        if (MessageBox.Show(Lang.Get("Flags_ClearConfirm"),
                "NekoStrap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _flagsPage.ClearFlags();
        _homePage.SetStatus(Lang.Get("Flags_Cleared"), false);
    }

    private void SaveFlags()
    {
        try
        {
            var flags = _flagsPage.CollectFlags();
            FastFlagStore.Save(EffectiveVersion(), flags);
            RefreshHomeMeta();
            _homePage.SetStatus(Lang.Get("Flags_Saved"), true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Lang.Get("Flags_SaveErr") + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            var dict = new Dictionary<string, string>();
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(dlg.FileName));
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                dict[p.Name] = p.Value.ValueKind == System.Text.Json.JsonValueKind.String
                    ? p.Value.GetString() ?? ""
                    : p.Value.GetRawText();
            }
            var current = _flagsPage.CollectFlags();
            foreach (var (k, v) in dict)
                current[k] = v;
            _flagsPage.SetFlags(current);
            _homePage.SetStatus(Lang.Format("Flags_ImportedFmt", dict.Count), true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Lang.Get("Flags_ImportErr") + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
            _homePage.SetStatus(Lang.Format("Flags_ExportedFmt", flags.Count), true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Lang.Get("Flags_ExportErr") + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ================= Версии =================

    private void RefreshVersions()
    {
        _versionsPage.SetVersions(VersionManager.List(), _config.InstalledVersion, _config.PreviousVersion);
        string? studio = _config.StudioVersion;
        _versionsPage.SetStudioStatus(
            RobloxPaths.IsVersionGuid(studio) &&
            File.Exists(Path.Combine(RobloxPaths.VersionsDir, studio, RobloxInstaller.StudioExe))
            ? studio : Lang.Get("Ver_StudioNotSet"));
    }

    private void ActivateSelectedVersion()
    {
        string? guid = _versionsPage.SelectedVersion();
        if (guid == null)
        {
            MessageBox.Show(Lang.Get("Ver_SelOne"), "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (guid == _config.InstalledVersion) return;
        if (RobloxPaths.IsVersionGuid(_config.InstalledVersion))
            _config.PreviousVersion = _config.InstalledVersion;
        _config.InstalledVersion = guid;
        _config.Save(RobloxPaths.ConfigPath);
        RefreshVersions();
        RefreshHomeMeta();
        _homePage.SetStatus(Lang.Format("Ver_ActivatedFmt", guid), true);
    }

    private void DeleteSelectedVersion()
    {
        string? guid = _versionsPage.SelectedVersion();
        if (guid == null)
        {
            MessageBox.Show(Lang.Get("Ver_SelOne"), "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (guid == _config.InstalledVersion || guid == _config.StudioVersion)
        {
            MessageBox.Show(Lang.Get("Ver_NoDelActive"), "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(ex.Message, "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Warning);
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
                _homePage.SetStatus($"Studio — {FormatInstall(p)}", false);
                _homePage.SetProgress(p.Fraction);
            });
            string guid = await RobloxInstaller.EnsureStudioInstalledAsync(
                _config, progress, CancellationToken.None);
            _config.Save(RobloxPaths.ConfigPath);
            RefreshVersions();
            _homePage.SetStatus(Lang.Format("Ver_StudioDone", guid), true);
        }
        catch (Exception ex)
        {
            _homePage.SetStatus(Lang.Get("Ver_StudioErr"), false);
            MessageBox.Show(Lang.Get("Ver_StudioErr2") + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _homePage.SetProgress(null);
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
            MessageBox.Show(ex.Message, "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(Lang.Get("Hist_SelGame"), "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        LaunchPlaceById(placeId);
    }

    private void DeleteHistorySelected()
    {
        long placeId = _historyPage.SelectedPlaceId();
        if (placeId <= 0)
        {
            MessageBox.Show(Lang.Get("Hist_SelGame"), "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show(Lang.Get("Hist_SelSession"),
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        if (s.PlaceId <= 0)
        {
            MessageBox.Show(Lang.Get("Hist_NoPlace"),
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(Lang.Get("Common_BrowserErr") + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteSessionSelected()
    {
        var s = _historyPage.SelectedSession();
        if (s == null)
        {
            MessageBox.Show(Lang.Get("Hist_SelSession2"), "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
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
            _homePage.SetStatus(Lang.Get("Common_Copied"), true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Lang.Get("Common_CopyErr") + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show(Lang.Get("Play_NoRoblox"),
                    "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _watcher.Clear();
            _homePage.SetStatus(jobId.Length > 0
                ? Lang.Get("Hop_Rejoin") : Lang.Get("Play_OpenPlace"), false);
            Process? process = RobloxLauncher.LaunchServer(exe, placeId, jobId);
            if (process == null)
                throw new InvalidOperationException(Lang.Get("Play_LaunchFail"));
            AfterLaunch(process);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Lang.Get("Hop_RejoinErr") + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ================= Настройки =================

    private void ApplyConfigToUi()
    {
        ClickSound.Enabled = _config.Sounds;
        _settingsPage.CloseOnLaunchCheck.IsChecked = _config.CloseOnLaunch;
        _settingsPage.DiscordRpcCheck.IsChecked = _config.DiscordRpc;
        _settingsPage.SoundsCheck.IsChecked = _config.Sounds;
        _settingsPage.AutoUpdateCheck.IsChecked = _config.AutoUpdate;
        _settingsPage.FpsCheck.IsChecked = _config.FpsLimit > 0;
        _settingsPage.FpsValueText = _config.FpsLimit > 0 ? _config.FpsLimit.ToString() : "240";
        _settingsPage.DiscordAppIdText = _config.DiscordAppId;
        _settingsPage.MinimizeToTrayCheck.IsChecked = _config.MinimizeToTray;
        _settingsPage.CloseToTrayCheck.IsChecked = _config.CloseToTray;
        _settingsPage.NotificationsCheck.IsChecked = _config.NotificationsEnabled;
        _settingsPage.TrackPlaytimeCheck.IsChecked = _config.TrackPlaytime;
        _settingsPage.RobloxNoTrayCheck.IsChecked = _config.RobloxNoTray;
        _settingsPage.RobloxNoStartupCheck.IsChecked = _config.RobloxNoStartup;
        _settingsPage.RobloxPathText = _config.RobloxPath;
        _settingsPage.ChatEnabledCheck.IsChecked = _config.ChatEnabled;
        _settingsPage.ChatDmCheck.IsChecked = _config.ChatDmEnabled;
        _settingsPage.ChatOverlayCheck.IsChecked = _config.ChatOverlayOnJoin;
        _settingsPage.ChatUrlText = _config.ChatServerUrl;
        _settingsPage.ChatNickText = _config.ChatNickname;
        _chatPage.SetDmEnabled(_config.ChatEnabled && _config.ChatDmEnabled);
        _modsPage.FleasionCheck.IsChecked = _config.FleasionEnabled;
    }

    private void SaveConfigFromUi()
    {
        _config.CloseOnLaunch = _settingsPage.CloseOnLaunchCheck.IsChecked == true;
        _config.DiscordRpc = _settingsPage.DiscordRpcCheck.IsChecked == true;
        _config.Sounds = _settingsPage.SoundsCheck.IsChecked == true;
        _config.AutoUpdate = _settingsPage.AutoUpdateCheck.IsChecked == true;
        _config.RobloxPath = _settingsPage.RobloxPathText;
        _config.FpsLimit = _settingsPage.FpsCheck.IsChecked == true
            && int.TryParse(_settingsPage.FpsValueText, out int fps) && fps >= 5 && fps <= 10000
            ? fps : 0;
        _config.DiscordAppId = _settingsPage.DiscordAppIdText;
        _config.MinimizeToTray = _settingsPage.MinimizeToTrayCheck.IsChecked == true;
        _config.CloseToTray = _settingsPage.CloseToTrayCheck.IsChecked == true;
        _config.NotificationsEnabled = _settingsPage.NotificationsCheck.IsChecked == true;
        _config.TrackPlaytime = _settingsPage.TrackPlaytimeCheck.IsChecked == true;
        _config.RobloxNoTray = _settingsPage.RobloxNoTrayCheck.IsChecked == true;
        _config.RobloxNoStartup = _settingsPage.RobloxNoStartupCheck.IsChecked == true;
        _config.ChatServerUrl = _settingsPage.ChatUrlText;
        _config.ChatNickname = _settingsPage.ChatNickText;
        _config.FleasionEnabled = _modsPage.FleasionCheck.IsChecked == true;
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
        Bool(_settingsPage.AutoUpdateCheck, v => _config.AutoUpdate = v);
        Bool(_settingsPage.FpsCheck, v => _config.FpsLimit = v ? ParseFpsValue() : 0);
        Bool(_settingsPage.MinimizeToTrayCheck, v => _config.MinimizeToTray = v);
        Bool(_settingsPage.CloseToTrayCheck, v => _config.CloseToTray = v);
        Bool(_settingsPage.NotificationsCheck, v => _config.NotificationsEnabled = v);
        Bool(_settingsPage.TrackPlaytimeCheck, v =>
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
        Bool(_settingsPage.ChatEnabledCheck, v =>
        {
            _config.ChatEnabled = v;
            _chatPage.SetDmEnabled(v && _config.ChatDmEnabled);
            SyncChatWithSession();
            RefreshChatHint();
        });
        Bool(_settingsPage.ChatDmCheck, v =>
        {
            _config.ChatDmEnabled = v;
            _chat.DmAllowed = v;
            _chatPage.SetDmEnabled(_config.ChatEnabled && v);
        });
        Bool(_settingsPage.ChatOverlayCheck, v => _config.ChatOverlayOnJoin = v);
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
        _chatPage.ServerMessageSend += text => _ = SendChatAsync(text, 0);
        _chatPage.DmMessageSend += (to, text) => _ = SendChatAsync(text, to);
        _chatPage.OpenOverlayClicked += () => ShowChatOverlay();

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
        }
        catch (Exception ex)
        {
            _chatPage.SetStatus(ex.Message, false);
        }
    }

    /// <summary>
    /// Чат живёт только вместе с игрой: подключаемся при живой сессии с
    /// JobId (одна комната = один роблокс-сервер), иначе отключаемся.
    /// Достаточно самой сессии из лога — не важно, кто запустил клиент.
    /// </summary>
    private void SyncChatWithSession()
    {
        var session = _watcher.Current;
        if (!_config.ChatEnabled || session == null ||
            session.PlaceId <= 0 || session.JobId.Length == 0)
        {
            _ = _chat.DisconnectAsync();
            return;
        }
        _ = ConnectChatAsync(session);
    }

    private async Task ConnectChatAsync(GameSession session)
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
            string name = _config.ChatNickname;
            if (name.Length == 0)
            {
                try { name = AccountStore.Load().Active?.Name ?? ""; } catch { /* ignore */ }
            }
            if (name.Length == 0)
                name = "Player" + uid % 10000;

            _chat.DmAllowed = _config.ChatDmEnabled;
            string url = ChatClient.NormalizeUrl(_config.ChatServerUrl);
            if (url.Length == 0)
            {
                _chatPage.SetStatus(Lang.Get("Chat_BadUrl"), false);
                return;
            }
            string room = session.PlaceId + "_" + session.JobId;
            // Новый сервер (другая комната) — чистим ленту от прошлого захода.
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
            MessageBox.Show(Lang.Get("Cdn_OnErr") + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(Lang.Get("Cdn_OffErr") + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            _homePage.SetStatus(Lang.Format("Wall_SetFmt", Path.GetFileName(dlg.FileName)), true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Lang.Format("Wall_LoadErrFmt", ex.Message),
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResetWallpaper()
    {
        _config.WallpaperPath = "";
        SaveQuiet();
        ApplyWallpaper();
        RefreshWallpaperSettings();
        _homePage.SetStatus(Lang.Get("Wall_Reset"), true);
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
                    _homePage.SetStatus(Lang.Format("Upd_AppAvailFmt", rel.Tag), false);
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
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(Lang.Get("Tray_Open"), null, (_, _) => ShowFromTray());
        menu.Items.Add(Lang.Get("Btn_Play"), null, async (_, _) =>
        {
            ShowFromTray();
            await PlayFlowAsync();
        });
        menu.Items.Add(Lang.Get("Tray_ServerInfo"), null, (_, _) =>
        {
            ShowFromTray();
            ShowServerInfoDialog();
        });
        menu.Items.Add(Lang.Get("Tray_Rejoin"), null, async (_, _) =>
        {
            ShowFromTray();
            await RejoinSameServerAsync();
        });
        menu.Items.Add(Lang.Get("Tray_Hop"), null, async (_, _) =>
        {
            ShowFromTray();
            await ServerHopAsync();
        });
        menu.Items.Add(BuildMusicMenu());
        menu.Items.Add(BuildColorMenu());
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var chatOverlayItem = new WinForms.ToolStripMenuItem(Lang.Get("Tray_ChatOverlayOn"))
        {
            Checked = _config.ChatOverlayOnJoin,
            CheckOnClick = true
        };
        chatOverlayItem.CheckedChanged += (_, _) =>
        {
            _config.ChatOverlayOnJoin = chatOverlayItem.Checked;
            _settingsPage.ChatOverlayCheck.IsChecked = chatOverlayItem.Checked;
            SaveQuiet();
        };
        menu.Items.Add(chatOverlayItem);
        menu.Items.Add(Lang.Get("Tray_ChatOpen"), null, (_, _) => ShowChatOverlay());
        menu.Items.Add(Lang.Get("Tray_ChatPage"), null, (_, _) =>
        {
            ShowFromTray();
            NavChat.IsChecked = true;
        });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Lang.Get("Tray_Exit"), null, (_, _) =>
        {
            _reallyExit = true;
            Close();
        });
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private static System.Drawing.Icon MakeTrayIcon()
    {
        var bmp = new System.Drawing.Bitmap(32, 32);
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
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
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
            _homePage.SetStatus(Lang.Get("Hop_Closing"), false);
            p!.Kill();
            try { await p.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { /* не дождались — запускаем всё равно */ }
            catch { /* ignore */ }
            await Task.Delay(600);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Lang.Get("Hop_KillErr") + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(Lang.Get("Hop_NoPlace"),
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show(Lang.Get("Hop_NoRejoin"),
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Information);
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
