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
    private AccountStore _accounts = new();
    private readonly Dictionary<long, string> _accountStatus = new();
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
    private Process? _playerProcess;
    private WinForms.NotifyIcon? _trayIcon;
    private ColorTuneWindow? _tuneWindow;
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
    private readonly AccountsPage _accountsPage = new();
    private readonly HistoryPage _historyPage = new();
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
        _pages[NavAccounts] = _accountsPage;
        _pages[NavHistory] = _historyPage;
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

        _accountsPage.AddClicked += async (_, _) => await AddAccountAsync();
        _accountsPage.RefreshClicked += async (_, _) => await ValidateAccountsAsync();
        _accountsPage.ActivateClicked += (_, _) => ActivateSelectedAccount();
        _accountsPage.DeleteClicked += (_, _) => DeleteSelectedAccount();
        _accountsPage.ExportClicked += (_, _) => ExportAccounts();
        _accountsPage.ImportClicked += (_, _) => ImportAccounts();
        _accountsPage.PlayAsActiveClicked += async (_, _) => await PlayFlowAsync();
        _accountsPage.ClearActiveClicked += (_, _) =>
        {
            _accounts.SetActive(null);
            RefreshAccounts();
            RefreshHomeMeta();
        };

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
            _homePage.SetStatus("Настройки сохранены", true);
        };
        _settingsPage.SoundPickClicked += (key) => PickCustomSound(key);
        _settingsPage.SoundResetClicked += (key) => ResetCustomSound(key);
        _settingsPage.SoundVolumeChanged += (key, v) =>
        {
            ClickSound.SetSoundVolume(key, v / 100f);
            _config.SoundVolumes[key] = v / 100f;
            SaveQuiet();
        };
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

        SubscribeLiveToggles();

        // Сессии из лога → карта сервера + Discord.
        _watcher.SessionChanged += (_, s) =>
            _ = Dispatcher.BeginInvoke(new Action(() => OnSessionChanged(s)));

        _serverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _serverTimer.Tick += (_, _) => RefreshServerCard();

        LoadConfig();
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
            RefreshWallpaperSettings();
            RefreshGlassSettings();
            InitTray();
            FooterText.Text = $"v{AppInfo.Version}  •  готов";
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
            else if (btn == NavAccounts)
            {
                RefreshAccounts();
            }
            else if (btn == NavHistory)
            {
                RefreshHistory();
            }
            else if (btn == NavSettings)
            {
                RefreshCdnStatus();
            }
        }
        catch (Exception ex)
        {
            _homePage.SetStatus("Не вышло обновить раздел: " + ex.Message, false);
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
        _accounts = AccountStore.Load();
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
                    throw new InvalidOperationException("не вышло загрузить фон");
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
            PlaytimeStore.Format(_playtime.TotalSeconds));
        string? v = EffectiveVersion();
        _homePage.SetVersionInfo($"Лаунчер {AppInfo.Version}   •   Roblox: " + (v ?? "не установлен"));
        _homePage.SetLastGame(FormatLastGame(), _config.LastGamePlaceId);
        var active = _accounts.Active;
        _homePage.SetAccount("Аккаунт: " + (active != null
            ? $"{active.Label} (@{active.Name})"
            : "клиент (не выбран)"));
    }

    private string FormatLastGame()
    {
        if (_config.LastGamePlaceId <= 0 && _config.LastGameName.Length == 0) return "";
        string name = _config.LastGameName.Length > 0
            ? _config.LastGameName
            : "Place " + _config.LastGamePlaceId;
        string id = _config.LastGamePlaceId > 0 ? $"  •  PlaceId {_config.LastGamePlaceId}" : "";
        if (_config.LastGameAt == default) return name + id;
        var ago = DateTime.UtcNow - _config.LastGameAt;
        string when = ago.TotalMinutes < 1 ? "только что"
            : ago.TotalMinutes < 60 ? $"{(int)ago.TotalMinutes} мин назад"
            : ago.TotalHours < 24 ? $"{(int)ago.TotalHours} ч назад"
            : ago.TotalDays < 7 ? $"{(int)ago.TotalDays} дн назад"
            : _config.LastGameAt.ToLocalTime().ToString("d MMM yyyy");
        string played = _playtime.ForPlace(_config.LastGamePlaceId) > 0
            ? $"  •  наиграно {PlaytimeStore.Format(_playtime.ForPlace(_config.LastGamePlaceId))}"
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
            MessageBox.Show("Пока не во что переигрывать — зайди в игру хоть раз.",
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        LaunchPlaceById(placeId);
    }

    /// <summary>Запуск плейса через наш exe (без реестра).</summary>
    private async void LaunchPlaceById(long placeId)
    {
        try
        {
            string? guid = EffectiveVersion();
            string? exe = RobloxLauncher.GetExePath(guid);
            if (exe == null)
            {
                MessageBox.Show("Roblox не установлен — нажми «Играть».",
                    "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _watcher.Clear();
            Process? process = null;
            var (ticket, acc) = await GetActiveTicketAsync();
            if (ticket.Length > 0 && acc != null)
            {
                _homePage.SetStatus("Открываю плейс: " + acc.Label + "...", false);
                process = RobloxAuth.LaunchPlace(exe, ticket, placeId);
            }
            if (process == null)
            {
                _homePage.SetStatus("Открываю плейс...", false);
                process = RobloxLauncher.LaunchPlace(exe, placeId);
            }
            if (process == null)
                throw new InvalidOperationException("не вышло запустить процесс клиента");
            AfterLaunch(process);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не вышло открыть плейс:\n" + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Тикет активного аккаунта для запуска. Пусто — запускаемся
    /// под залогиненным в клиенте.
    /// </summary>
    private async Task<(string ticket, AccountEntry? acc)> GetActiveTicketAsync()
    {
        var acc = _accounts.Active;
        if (acc == null) return ("", null);
        if (!_accounts.TryGetCookie(acc, out string cookie, out string _))
            return ("", null);
        _homePage.SetStatus("Вход: " + acc.Label + "...", false);
        var (ticket, _) = await RobloxAuth.GetAuthTicketAsync(cookie, CancellationToken.None);
        if (ticket.Length == 0)
            return ("", null);
        acc.LastUsedAt = DateTime.UtcNow;
        _accounts.Save();
        return (ticket, acc);
    }

    /// <summary>Общее после любого запуска игры: трекинг, моды, Discord, таймеры.</summary>
    private void AfterLaunch(Process process)
    {
        TrackPlayer(process);
        FleasionManager.LaunchIfEnabled(_config.FleasionEnabled);
        _discord.SetIdle();
        _homePage.SetStatus("Roblox запущен", true);
        _serverTimer.Start();
        RefreshHomeMeta();

        if (_config.CloseOnLaunch && !_config.CloseToTray)
            Close();
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
                _homePage.SetStatus($"{p.Phase}: {p.Detail}", false);
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
                throw new InvalidOperationException("exe клиента не найден после установки");

            // FPS-лимит и фиксы Roblox применяются поверх перед стартом.
            FpsManager.Apply(guid, _config.FpsLimit);
            RobloxAppFixes.Apply(_config.RobloxNoTray, _config.RobloxNoStartup);

            _homePage.SetStatus("Запуск Roblox...", false);
            _homePage.SetProgress(null);
            _watcher.Clear();
            Process? process = null;
            var (ticket, acc) = await GetActiveTicketAsync();
            if (ticket.Length > 0 && acc != null)
            {
                _homePage.SetStatus("Запуск Roblox: " + acc.Label + "...", false);
                process = RobloxAuth.LaunchApp(exe, ticket);
            }
            if (process == null)
                process = RobloxLauncher.Launch(exe);
            if (process == null)
                throw new InvalidOperationException("не вышло запустить процесс клиента");
            AfterLaunch(process);
        }
        catch (OperationCanceledException)
        {
            _homePage.SetStatus("Отменено", false);
        }
        catch (Exception ex)
        {
            _homePage.SetStatus("Ошибка", false);
            MessageBox.Show("Не вышло запустить Roblox:\n" + ex.Message,
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
                        _homePage.SetStatus("Доступно обновление Roblox — нажми Играть", false);
                        _homePage.SetVersionInfo(
                            $"Лаунчер {AppInfo.Version}   •   Roblox: {installed ?? "не установлен"} → {latest.VersionGuid}");
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
        _homePage.ClearServer();
        _lastServerIp = "";
        _lastGeoText = "—";
        _lastPingText = "—";
        _lastGameName = "";
        _discord.SetIdle();
        _serverTimer.Stop();
        _homePage.SetStatus("Игра закрыта", true);
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
        BeginSessionTime(session.PlaceId, "");
        try { _recent.Begin(session.PlaceId, session.JobId, session.ServerIp, session.ServerPort); } catch { /* ignore */ }
        _homePage.SetServer(session.ServerIp.Length > 0 ? session.ServerIp : "поиск...",
            "определяю...", "—");
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
                return _watcher.LastClientPingMs + " мс";
        }
        catch { /* ignore */ }
        return ping.Ms >= 0
            ? (ping.Estimated ? "~" : "") + ping.Ms + " мс"
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
            Title = $"Свой звук: {title}",
            Filter = "WAV (*.wav)|*.wav|Все файлы (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        if (ClickSound.TrySetCustom(key, dlg.FileName, out string error))
        {
            _config.CustomSounds[key] = dlg.FileName;
            SaveQuiet();
            RefreshSoundsSettings();
            _homePage.SetStatus($"Звук «{title}»: свой файл", true);
        }
        else
        {
            MessageBox.Show($"Не вышло взять звук:\n{error}",
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
            Title = "Выбери файлы мода",
            Multiselect = true,
            Filter = "Все файлы (*.*)|*.*"
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
            MessageBox.Show("Не вышло добавить мод:\n" + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ToggleSelectedMods()
    {
        var selected = _modsPage.SelectedModPaths();
        if (selected.Count == 0)
        {
            MessageBox.Show("Выбери мод в списке.", "NekoStrap",
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
            MessageBox.Show("Не вышло переключить мод:\n" + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteSelectedMods()
    {
        var selected = _modsPage.SelectedModPaths();
        if (selected.Count == 0)
        {
            MessageBox.Show("Выбери мод в списке.", "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Удалить модов: {selected.Count}?",
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
            MessageBox.Show("Не вышло удалить:\n" + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshFleasionStatus()
    {
        _modsPage.SetFleasionStatus(FleasionManager.IsInstalled()
            ? (FleasionManager.IsRunning() ? "установлен, запущен" : "установлен")
            : "не установлен — нажми Скачать");
    }

    private async Task DownloadFleasionAsync()
    {
        _modsPage.SetFleasionStatus("качаю с GitHub...");
        try
        {
            var progress = new Progress<(long done, long total)>(t =>
            {
                if (_closed) return;
                string detail = t.total > 0
                    ? $"{t.done / 1048576.0:F1} / {t.total / 1048576.0:F1} МБ"
                    : $"{t.done / 1048576.0:F1} МБ";
                _modsPage.SetFleasionStatus("качаю: " + detail);
            });
            await FleasionManager.DownloadAsync(progress, CancellationToken.None);
            RefreshFleasionStatus();
        }
        catch (Exception ex)
        {
            _modsPage.SetFleasionStatus("ошибка: " + ex.Message);
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
            MessageBox.Show("Выбери игру в списке.", "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        bool existed = FavoritesStore.Contains(placeId);
        FavoritesStore.Add(placeId, name);
        RefreshFavorites();
        _homePage.SetStatus(existed
            ? "Уже в избранном — название обновил"
            : $"В избранном: {(name.Length > 0 ? name : "Place " + placeId)}", true);
    }

    private void RemoveFavorite(long placeId)
    {
        FavoritesStore.Remove(placeId);
        RefreshFavorites();
        _homePage.SetStatus("Убрал из избранного", true);
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
            if (MessageBox.Show($"Применить профиль «{name}»? Таблица заменится целиком.",
                    "NekoStrap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
        }
        _flagsPage.SetFlags(new Dictionary<string, string>(flags));
        _homePage.SetStatus($"Профиль «{name}»: {flags.Count} флагов (нажми «Сохранить»)", false);
    }

    private void SaveFlagProfile()
    {
        string name = _flagsPage.ProfileNameText;
        if (name.Length == 0)
        {
            MessageBox.Show("Введи название профиля.", "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var flags = _flagsPage.CollectFlags();
        if (flags.Count == 0)
        {
            MessageBox.Show("Таблица пустая — нечего сохранять.", "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        FlagProfiles.SaveProfile(name, flags);
        _flagsPage.ClearProfileName();
        RefreshFlagProfiles();
        _homePage.SetStatus($"Профиль «{name}» сохранён", true);
    }

    private void DeleteFlagProfile()
    {
        string name = _flagsPage.ProfileNameText;
        if (name.Length == 0)
        {
            MessageBox.Show("Введи название профиля для удаления.", "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (FlagProfiles.Get(name) == null)
        {
            MessageBox.Show($"Профиля «{name}» нет.", "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Удалить профиль «{name}»?", "NekoStrap",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        FlagProfiles.Delete(name);
        _flagsPage.ClearProfileName();
        RefreshFlagProfiles();
        _homePage.SetStatus($"Профиль «{name}» удалён", true);
    }

    private void DeleteAllFlags()
    {
        if (_flagsPage.CollectFlags().Count == 0) return;
        if (MessageBox.Show("Удалить ВСЕ флаги из таблицы?\nВ файл запишется после кнопки «Сохранить».",
                "NekoStrap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _flagsPage.ClearFlags();
        _homePage.SetStatus("Все флаги удалены (нажми «Сохранить»)", false);
    }

    private void SaveFlags()
    {
        try
        {
            var flags = _flagsPage.CollectFlags();
            FastFlagStore.Save(EffectiveVersion(), flags);
            RefreshHomeMeta();
            _homePage.SetStatus("Флаги сохранены", true);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не вышло сохранить флаги:\n" + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportFlags()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Импорт флагов из JSON",
            Filter = "JSON (*.json)|*.json|Все файлы (*.*)|*.*"
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
            _homePage.SetStatus($"Импортировано флагов: {dict.Count}", true);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не вышло импортировать:\n" + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportFlags()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Экспорт флагов в JSON",
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
            _homePage.SetStatus($"Экспортировано флагов: {flags.Count}", true);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не вышло экспортировать:\n" + ex.Message,
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
            ? studio : "не установлено");
    }

    private void ActivateSelectedVersion()
    {
        string? guid = _versionsPage.SelectedVersion();
        if (guid == null)
        {
            MessageBox.Show("Выбери версию в списке.", "NekoStrap",
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
        _homePage.SetStatus("Активна версия " + guid, true);
    }

    private void DeleteSelectedVersion()
    {
        string? guid = _versionsPage.SelectedVersion();
        if (guid == null)
        {
            MessageBox.Show("Выбери версию в списке.", "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (guid == _config.InstalledVersion || guid == _config.StudioVersion)
        {
            MessageBox.Show("Активную версию удалить нельзя.", "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show($"Удалить {guid}?", "NekoStrap",
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
                _homePage.SetStatus($"Studio — {p.Phase}: {p.Detail}", false);
                _homePage.SetProgress(p.Fraction);
            });
            string guid = await RobloxInstaller.EnsureStudioInstalledAsync(
                _config, progress, CancellationToken.None);
            _config.Save(RobloxPaths.ConfigPath);
            RefreshVersions();
            _homePage.SetStatus("Studio установлено: " + guid, true);
        }
        catch (Exception ex)
        {
            _homePage.SetStatus("Ошибка Studio", false);
            MessageBox.Show("Не вышло поставить Studio:\n" + ex.Message,
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
                throw new InvalidOperationException("Studio не установлено — нажми Установить");
            RobloxLauncher.LaunchStudio(exe);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ================= Аккаунты =================

    private void RefreshAccounts()
    {
        if (_closed) return;
        var active = _accounts.Active;
        _accountsPage.SetActive(active != null
            ? $"{active.Label}  •  @{active.Name}  •  id {active.UserId}"
            : "Не выбран — запуск идёт под залогиненным в клиенте.");
        _accountsPage.SetAccounts(_accounts.Accounts.Select(a =>
        {
            string st = _accountStatus.TryGetValue(a.UserId, out var s) ? s : "—";
            return (a, st, a.UserId == _accounts.ActiveId);
        }).ToList());
    }

    private async Task AddAccountAsync()
    {
        var dlg = new AddAccountDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _homePage.SetStatus("Проверяю куку...", false);
        var (entry, error) = await _accounts.AddAsync(dlg.CookieText, dlg.AliasText, CancellationToken.None);
        if (entry == null)
        {
            MessageBox.Show("Не вышло добавить аккаунт:\n" + error,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
            _homePage.SetStatus("Готов к запуску", true);
            return;
        }
        _accountStatus[entry.UserId] = "готов";
        RefreshAccounts();
        RefreshHomeMeta();
        _homePage.SetStatus("Аккаунт добавлен: " + entry.Label, true);
    }

    /// <summary>Проверка всех кук через users API (имена могли смениться).</summary>
    private async Task ValidateAccountsAsync()
    {
        if (_accounts.Accounts.Count == 0)
        {
            MessageBox.Show("Список пуст — добавь аккаунт.", "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _homePage.SetStatus("Проверяю аккаунты...", false);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        foreach (var a in _accounts.Accounts.ToList())
        {
            if (!_accounts.TryGetCookie(a, out string cookie, out string decErr))
            {
                _accountStatus[a.UserId] = "ошибка: " + decErr;
                continue;
            }
            var (id, name, display, err) = await RobloxAuth.GetAuthenticatedUserAsync(cookie, cts.Token);
            if (err.Length > 0)
            {
                _accountStatus[a.UserId] = "ошибка: кука протухла";
                continue;
            }
            a.Name = name;
            a.DisplayName = display;
            _accountStatus[a.UserId] = "готов";
        }
        _accounts.Save();
        RefreshAccounts();
        RefreshHomeMeta();
        _homePage.SetStatus("Проверка аккаунтов готова", true);
    }

    private void ActivateSelectedAccount()
    {
        var entry = _accountsPage.SelectedEntry();
        if (entry == null)
        {
            MessageBox.Show("Выбери аккаунт в списке.", "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _accounts.SetActive(entry);
        RefreshAccounts();
        RefreshHomeMeta();
        _homePage.SetStatus("Активен: " + entry.Label, true);
    }

    private void DeleteSelectedAccount()
    {
        var entry = _accountsPage.SelectedEntry();
        if (entry == null)
        {
            MessageBox.Show("Выбери аккаунт в списке.", "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Удалить «{entry.Label}»?\nКука сотрётся с этого ПК.",
                "NekoStrap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _accountStatus.Remove(entry.UserId);
        _accounts.Remove(entry);
        RefreshAccounts();
        RefreshHomeMeta();
    }

    private void ExportAccounts()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Бэкап аккаунтов",
            Filter = "JSON (*.json)|*.json",
            FileName = "nekostrap-accounts.json"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            _accounts.Save();
            File.Copy(AccountStore.FilePath, dlg.FileName, overwrite: true);
            MessageBox.Show(
                "Готово. Бэкап восстановится только под тем же пользователем Windows\n(шифр DPAPI привязан к нему).",
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не вышло экспортировать:\n" + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportAccounts()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Импорт бэкапа аккаунтов",
            Filter = "JSON (*.json)|*.json|Все файлы (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var (added, updated) = _accounts.Import(dlg.FileName);
            RefreshAccounts();
            RefreshHomeMeta();
            _homePage.SetStatus($"Импорт: новых {added}, обновлено {updated}", true);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не вышло импортировать:\n" + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("Выбери игру в списке.", "NekoStrap",
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
            MessageBox.Show("Выбери игру в списке.", "NekoStrap",
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
        if (MessageBox.Show("Стереть всю историю игр?", "NekoStrap",
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
            MessageBox.Show("Выбери заход в нижнем списке «Последние заходы».",
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        if (s.PlaceId <= 0)
        {
            MessageBox.Show("У этого захода нет PlaceId — туда не перезайти.",
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
            if (MessageBox.Show(
                    "JobId сервера не сохранился — зайду на случайный сервер того же плейса?",
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
        string text =
            $"Игра: {s.DisplayName}\n" +
            $"PlaceId: {s.PlaceId}\n" +
            $"JobId: {(s.JobId.Length > 0 ? s.JobId : "—")}\n" +
            $"Ссылка: {RobloxLauncher.BuildServerUrl(s.PlaceId, s.JobId)}\n" +
            $"На сайте: {RobloxLauncher.BuildWebUrl(s.PlaceId)}";
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
            MessageBox.Show("Не вышло открыть браузер:\n" + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteSessionSelected()
    {
        var s = _historyPage.SelectedSession();
        if (s == null)
        {
            MessageBox.Show("Выбери заход в нижнем списке.", "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _recent.Remove(s);
        RefreshHistory();
    }

    private void ClearSessions()
    {
        if (_recent.List().Count == 0) return;
        if (MessageBox.Show("Стереть лог последних заходов?\n(Суммарное время по играм останется.)",
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
            _homePage.SetStatus("Скопировано в буфер обмена", true);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не вышло скопировать:\n" + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Перезаход на тот же сервер по JobId. Без JobId — обычный заход на плейс.
    /// </summary>
    private async void LaunchServerByJob(long placeId, string jobId)
    {
        try
        {
            string? guid = EffectiveVersion();
            string? exe = RobloxLauncher.GetExePath(guid);
            if (exe == null)
            {
                MessageBox.Show("Roblox не установлен — нажми «Играть».",
                    "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _watcher.Clear();
            Process? process = null;
            var (ticket, acc) = await GetActiveTicketAsync();
            if (ticket.Length > 0 && acc != null)
            {
                _homePage.SetStatus(jobId.Length > 0
                    ? "Возвращаю на тот же сервер: " + acc.Label + "..." : "Открываю плейс: " + acc.Label + "...", false);
                process = RobloxAuth.LaunchServer(exe, ticket, placeId, jobId);
            }
            if (process == null)
            {
                _homePage.SetStatus(jobId.Length > 0
                    ? "Возвращаю на тот же сервер..." : "Открываю плейс...", false);
                process = RobloxLauncher.LaunchServer(exe, placeId, jobId);
            }
            if (process == null)
                throw new InvalidOperationException("не вышло запустить процесс клиента");
            AfterLaunch(process);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не вышло перезайти:\n" + ex.Message,
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
            _accounts = AccountStore.Load();
            _accountStatus.Clear();
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
    }

    private int ParseFpsValue()
    {
        if (int.TryParse(_settingsPage.FpsValueText, out int fps) && fps >= 5 && fps <= 10000)
            return fps;
        return 240;
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
        if (!EnsureAdmin("включить CDN-фикс")) return;
        try
        {
            string result = CdnFix.Apply();
            _settingsPage.SetCdnStatus(CdnFix.IsApplied(), result);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не вышло включить фикс:\n" + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CdnRollback()
    {
        if (!EnsureAdmin("откатить CDN-фикс")) return;
        try
        {
            string result = CdnFix.Rollback();
            _settingsPage.SetCdnStatus(CdnFix.IsApplied(), result);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не вышло откатить:\n" + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool EnsureAdmin(string action)
    {
        if (CdnFix.IsAdmin()) return true;
        if (MessageBox.Show(
                $"Чтобы {action}, нужны права администратора.\nПерезапустить лаунчер с правами?",
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
    /// его тянем через SkiaSharp в PNG-байты (как было в WinForms).
    /// Гифки: WPF показывает первый кадр статично (анимация — позже).
    /// </summary>
    private static System.Windows.Media.Imaging.BitmapSource? DecodeWallpaper(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (!WallpaperExts.Contains(ext))
            throw new InvalidOperationException("Формат не поддерживается (jpg/png/gif/webp).");
        Stream stream;
        MemoryStream? owned = null;
        if (ext == ".webp")
        {
            using var sk = SkiaSharp.SKBitmap.Decode(path);
            if (sk == null)
                throw new InvalidOperationException("Не вышло декодировать webp.");
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
            Title = "Выбери фон",
            Filter = "Фото (*.jpg;*.png;*.gif;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp"
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
                    throw new InvalidOperationException("не вышло загрузить гифку");
            }
            else
            {
                DecodeWallpaper(dlg.FileName);
            }
            _config.WallpaperPath = dlg.FileName;
            SaveQuiet();
            ApplyWallpaper();
            RefreshWallpaperSettings();
            _homePage.SetStatus("Фон: " + Path.GetFileName(dlg.FileName), true);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не вышло загрузить фон: " + ex.Message,
                "NekoStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResetWallpaper()
    {
        _config.WallpaperPath = "";
        SaveQuiet();
        ApplyWallpaper();
        RefreshWallpaperSettings();
        _homePage.SetStatus("Фон сброшен", true);
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
        _settingsPage.SetGlassInfo(_config.GlassEnabled, _config.GlassOpacity, _config.GlassBlur, _config.GlassDim);
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
                    _homePage.SetStatus($"Доступна версия {rel.Tag} — см. раздел «О программе»", false);
                }));
            }
            catch { /* без сети — молча */ }
        });
    }

    private async Task CheckForAppUpdatesAsync(bool manual)
    {
        _aboutPage.ShowDownloadButton(false);
        _aboutPage.SetReleaseNotes("");
        _aboutPage.SetUpdateStatus("Проверяю релизы на GitHub...");
        try
        {
            var rel = await AppUpdater.GetLatestAsync();
            if (_closed) return;
            if (rel == null)
            {
                _aboutPage.SetUpdateStatus(
                    $"Не вышло проверить{(manual ? " (нет сети?)" : "")}. Релизы вручную: github.com/mesorg-dev/NekoStrap/releases");
                return;
            }
            if (!AppUpdater.IsNewer(AppInfo.Version, rel.Tag))
            {
                _pendingRelease = null;
                _aboutPage.SetUpdateStatus($"У тебя свежая версия (v{AppInfo.Version}, релиз {rel.Tag}).");
                return;
            }
            _pendingRelease = rel;
            string when = rel.PublishedAt == default ? "" : $" ({rel.PublishedAt.ToLocalTime():d MMM yyyy})";
            _aboutPage.SetUpdateStatus($"Доступна {rel.Tag}{when}. Текущая: v{AppInfo.Version}.");
            _aboutPage.SetReleaseNotes(rel.Notes);
            _aboutPage.ShowDownloadButton(true);
        }
        catch (Exception ex)
        {
            if (!_closed)
                _aboutPage.SetUpdateStatus("Не вышло проверить: " + ex.Message);
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
                    ? $"{t.done / 1048576.0:F1} / {t.total / 1048576.0:F1} МБ"
                    : $"{t.done / 1048576.0:F1} МБ";
                _aboutPage.SetUpdateStatus($"Качаю {rel.Tag}: " + detail);
            });
            await AppUpdater.DownloadAsync(rel.DownloadUrl, dest, progress, CancellationToken.None);
            if (_closed) return;
            _aboutPage.SetUpdateStatus("Готово, перезапускаюсь на новую версию...");
            await Task.Delay(800);
            AppUpdater.InstallAndRestart(dest);
            _reallyExit = true;
            Close();
        }
        catch (Exception ex)
        {
            if (!_closed)
            {
                _aboutPage.SetUpdateStatus("Не вышло скачать: " + ex.Message);
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
                "Лаунчер свёрнут в трей. Выход — правый клик по иконке.",
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
        menu.Items.Add("Открыть", null, (_, _) => ShowFromTray());
        menu.Items.Add("Играть", null, async (_, _) =>
        {
            ShowFromTray();
            await PlayFlowAsync();
        });
        menu.Items.Add("Информация о сервере", null, (_, _) =>
        {
            ShowFromTray();
            ShowServerInfoDialog();
        });
        menu.Items.Add("Перезайти (тот же сервер)", null, async (_, _) =>
        {
            ShowFromTray();
            await RejoinSameServerAsync();
        });
        menu.Items.Add("Другой сервер этого плейса", null, async (_, _) =>
        {
            ShowFromTray();
            await ServerHopAsync();
        });
        menu.Items.Add(BuildMusicMenu());
        menu.Items.Add(BuildColorMenu());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) =>
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
        var music = new WinForms.ToolStripMenuItem("Музыка");
        music.DropDownOpening += (_, _) =>
        {
            music.DropDownItems.Clear();
            try
            {
                _music.Refresh();
                string now = _music.CurrentTitle;
                var header = new WinForms.ToolStripMenuItem(
                    now.Length > 0 ? "♪ " + now : "Папка Music пуста — кинь туда mp3")
                {
                    Enabled = false
                };
                music.DropDownItems.Add(header);
                music.DropDownItems.Add(new WinForms.ToolStripSeparator());
                music.DropDownItems.Add(_music.IsPlaying ? "Пауза" : "Играть", null,
                    (_, _) => _music.PlayPause());
                music.DropDownItems.Add("Следующий", null, (_, _) => _music.Next());
                music.DropDownItems.Add("Предыдущий", null, (_, _) => _music.Prev());
                music.DropDownItems.Add("Стоп", null, (_, _) => _music.Stop());
                var shuffle = new WinForms.ToolStripMenuItem("Шаффл")
                {
                    Checked = _music.Shuffle,
                    CheckOnClick = true
                };
                shuffle.CheckedChanged += (_, _) => _music.Shuffle = shuffle.Checked;
                music.DropDownItems.Add(shuffle);
                music.DropDownItems.Add("Громче", null,
                    (_, _) => _music.SetVolume(_music.Volume + 0.1f));
                music.DropDownItems.Add("Тише", null,
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
                        $"…и ещё {_music.Tracks.Count - shown}") { Enabled = false });

                music.DropDownItems.Add(new WinForms.ToolStripSeparator());
                music.DropDownItems.Add("Открыть папку музыки", null, (_, _) =>
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
        var root = new WinForms.ToolStripMenuItem("Цветокоррекция");
        root.DropDownOpening += (_, _) =>
        {
            root.DropDownItems.Clear();
            try
            {
                var state = new WinForms.ToolStripMenuItem(
                    !_config.ColorFxEnabled ? "Выключена"
                    : _config.ColorFxAuto ? (IsRobloxForeground() ? "Активна (игра)" : "Ждёт игру…")
                    : "Активна всегда")
                { Enabled = false };
                root.DropDownItems.Add(state);
                root.DropDownItems.Add(new WinForms.ToolStripSeparator());

                var on = new WinForms.ToolStripMenuItem("Включена")
                    { Checked = _config.ColorFxEnabled, CheckOnClick = true };
                on.CheckedChanged += (_, _) =>
                {
                    _config.ColorFxEnabled = on.Checked;
                    SaveQuiet();
                    UpdateColorFx();
                };
                root.DropDownItems.Add(on);

                var auto = new WinForms.ToolStripMenuItem("Только когда игра активна")
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
                Preset("Стандарт", ColorSettings.Default);
                Preset("Кино", ColorSettings.Cinema);
                Preset("Ярко", ColorSettings.Vivid);
                Preset("Чёрно-белое", ColorSettings.Mono);
                root.DropDownItems.Add(new WinForms.ToolStripSeparator());
                root.DropDownItems.Add("Настроить… (ползунки)", null, (_, _) => OpenColorTuner());
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
            _homePage.SetStatus("Закрываю текущий сервер...", false);
            p!.Kill();
            try { await p.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { /* не дождались — запускаем всё равно */ }
            catch { /* ignore */ }
            await Task.Delay(600);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не вышло закрыть Roblox:\n" + ex.Message,
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
            MessageBox.Show("Не знаю куда хопать — зайди в игру хоть раз.",
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
            MessageBox.Show("Пока некуда возвращаться — зайди в игру хоть раз.",
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
            if (MessageBox.Show(
                    "JobId сервера не сохранился — зайду на случайный сервер того же плейса?",
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
            MessageBox.Show("Сейчас никуда не зашёл — запусти игру.",
                "Сервер", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string text =
            $"Игра: {(_lastGameName.Length > 0 ? _lastGameName : "—")}\n" +
            $"PlaceId: {session.PlaceId}\n" +
            $"IP: {_lastServerIp}\n" +
            $"Место: {_lastGeoText}\n" +
            $"Пинг: {_lastPingText}\n" +
            $"JobId: {(session.JobId.Length > 0 ? session.JobId : "—")}";
        if (MessageBox.Show(text + "\n\nСкопировать всё в буфер обмена?", "Сервер",
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
            _trayIcon?.ShowBalloonTip(4000, "NekoStrap — сервер",
                $"{(_lastGameName.Length > 0 ? _lastGameName : "Roblox")}\n" +
                $"{_lastServerIp}  •  {_lastGeoText}  •  {_lastPingText}",
                WinForms.ToolTipIcon.Info);
        }
        catch { /* ignore */ }
    }
}
