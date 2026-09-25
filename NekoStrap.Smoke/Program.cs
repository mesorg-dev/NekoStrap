using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using NekoStrap.Roblox;
using NekoStrap.Utils;
using NekoStrap.Wpf;
using NekoStrap.Wpf.Pages;

namespace NekoStrap.Smoke;

/// <summary>
/// Дымовой тест всего лаунчера без показа окон: создаёт главное окно,
/// принудительно раскладывает все 8 страниц (применяет шаблоны — ловит
/// XAML-краши класса BgPanelBrush), дёргает все сеттеры/обновлялки,
/// декод картинок, звуки, тему. Ничего не показывает, не сохраняет,
/// буфер обмена и сеть не трогает. Код возврата = число провалов.
/// </summary>
internal static class Program
{
    private static int _fail;
    private static int _pass;

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            // Загружает Theme.xaml (FindResource в страницах иначе упадёт).
            // Важно: вызвать InitializeComponent — ctor App этого не делает
            // (обычно его дёргает сгенерированный Main).
            var app = new NekoStrap.Wpf.App();
            app.InitializeComponent();

            Check("App+тема загрузились", () =>
            {
                if (Application.Current?.TryFindResource("CardBrush") == null)
                    throw new Exception("нет ресурса CardBrush");
            });

            Check("Звуковые ресурсы вшиты", () =>
            {
                var names = typeof(ClickSound).Assembly.GetManifestResourceNames();
                foreach (var need in new[] { "NekoStrap.Assets.click.wav", "NekoStrap.Assets.transition.wav" })
                    if (!names.Contains(need))
                        throw new Exception("нет ресурса " + need);
            });

            MainWindow? win = null;
            Check("MainWindow конструируется", () => { win = new MainWindow(); });
            if (win == null) return Report();

            // Раскладка всех страниц = применение всех шаблонов.
            var host = (ContentControl)GetField(win, "PageHost")!;
            var pages = (System.Collections.IDictionary)GetField(win, "_pages")!;
            Check("Страниц в навигации: 8", () =>
            {
                if (pages.Count != 8) throw new Exception("нашлось " + pages.Count);
            });
            win.Measure(new Size(1120, 700));
            win.Arrange(new Rect(0, 0, 1120, 700));
            foreach (System.Collections.DictionaryEntry kv in pages)
            {
                var page = (UserControl)kv.Value!;
                string name = page.GetType().Name;
                Check("Раскладка " + name, () =>
                {
                    host.Content = page;
                    host.UpdateLayout();
                    win.UpdateLayout();
                });
            }

            var home = (HomePage)GetField(win, "_homePage")!;
            Check("Home: сеттеры", () =>
            {
                home.SetStatus("Тест", true);
                home.SetStatus("Тест...", false);
                home.SetAccount("Аккаунт: тест");
                home.SetVersionInfo("v");
                home.SetBusy(true);
                home.SetProgress(0.5);
                home.SetProgress(null);
                home.SetBusy(false);
                home.SetCounts(3, 5, "10 ч");
                home.SetLastGame("Игра • PlaceId 1", 1);
                home.SetLastGame("", 0);
                home.SetServer("1.2.3.4", "Город, Страна", "50 мс");
                home.ClearServer();
            });

            var mods = (ModsPage)GetField(win, "_modsPage")!;
            Check("Mods: таблица", () =>
            {
                mods.SetMods(new List<ModEntry>
                {
                    new("content\\sounds\\ouch.ogg", 12345, true),
                    new("mod.dll.disabled", 999, false),
                });
                mods.SetFleasionStatus("установлен");
            });

            var flags = (FastFlagsPage)GetField(win, "_flagsPage")!;
            Check("Flags: таблица+пресеты", () =>
            {
                flags.SetFlags(new Dictionary<string, string> { { "BFlag", "2" }, { "AFlag", "true" } });
                var got = flags.CollectFlags();
                if (got.Count != 2 || got["AFlag"] != "true") throw new Exception("CollectFlags врёт");
                flags.AddNewFlag();
                // Тоггл пресета туда-обратно.
                var before = flags.CollectFlags().Count;
                flags.SetFlags(new Dictionary<string, string>());
                if (flags.CollectFlags().Count != 0) throw new Exception("SetFlags не чистит");
            });

            var versions = (VersionsPage)GetField(win, "_versionsPage")!;
            Check("Versions: таблица", () =>
            {
                versions.SetVersions(new List<InstalledVersionInfo>
                {
                    new("version-abc", 100_000_000, DateTime.UtcNow, true, false),
                }, "version-abc", "");
                versions.SetStudioStatus("не установлено");
                if (versions.SelectedVersion() != null) throw new Exception("пустой выбор врёт");
            });

            var history = (HistoryPage)GetField(win, "_historyPage")!;
            Check("History: таблицы+сортировка", () =>
            {
                history.SetGames(new List<(long, string, long, DateTime)>
                {
                    (123, "Игра", 3600, DateTime.UtcNow),
                });
                history.SetSessions(new List<RecentSession>
                {
                    new() { PlaceId = 123, JobId = "abcdef12-3456", ServerIp = "5.6.7.8", JoinedAt = DateTime.UtcNow },
                });
                if (history.SelectedPlaceId() != 0) throw new Exception("пустой выбор врёт");
                if (history.SelectedSession() != null) throw new Exception("пустой выбор врёт");
            });

            var settings = (SettingsPage)GetField(win, "_settingsPage")!;
            Check("Settings: сеттеры", () =>
            {
                settings.RobloxPathText = "C:\\R";
                settings.FpsValueText = "240";
                settings.DiscordAppIdText = "123";
                settings.ChatUrlText = "ws://localhost:8787/ws";
                settings.ChatNickText = "Neko";
                if (settings.RobloxPathText != "C:\\R") throw new Exception("PathText врёт");
                if (settings.ChatUrlText != "ws://localhost:8787/ws") throw new Exception("ChatUrlText врёт");
                settings.SetCdnStatus(true, "IP 1.1.1.1");
                settings.SetCdnStatus(false, "");
                settings.SetWallpaperInfo("bg.png", null, 30);
                settings.SetWallpaperDim(50);
                settings.SetGlassInfo(true, 50, 50, 80, false);
            });

            var chat = (ChatPage)GetField(win, "_chatPage")!;
            Check("Chat: сеттеры+сообщения", () =>
            {
                chat.SetState(ChatState.Connecting);
                chat.SetState(ChatState.Connected);
                chat.SetStatus("тест", true);
                chat.SetHint("подсказка");
                chat.SetDmEnabled(true);
                chat.SetUsers(new List<ChatUser>
                {
                    new(1, "Alice"),
                    new(2, "Bob"),
                }, 1);
                chat.AppendMessage(new ChatMessage
                {
                    From = 1, Name = "Alice", Text = "привет",
                    SelfUid = 1, At = DateTime.UtcNow,
                });
                chat.AppendMessage(new ChatMessage
                {
                    From = 2, Name = "Bob", Text = "лс", To = 1,
                    SelfUid = 1, At = DateTime.UtcNow,
                });
                chat.ResetForRoom();
                chat.RefreshLabels();
                chat.SetDmEnabled(false);
                chat.SetState(ChatState.Disconnected);
            });

            Check("ChatOverlay: конструируется+сеттеры", () =>
            {
                var ov = new ChatOverlayWindow();
                ov.SetState(ChatState.Connected);
                ov.SetUserCount(3);
                ov.AppendMessage(new ChatMessage
                {
                    From = 9, Name = "X", Text = "hi",
                    SelfUid = 1, At = DateTime.UtcNow,
                });
                ov.Replay(new List<ChatMessage>
                {
                    new() { From = 9, Name = "X", Text = "hi2", SelfUid = 1 },
                });
                ov.ResetMessages();
                ov.Close();
            });

            Check("UiTheme: все комбинации", () =>
            {
                foreach (bool wall in new[] { false, true })
                    foreach (bool glass in new[] { false, true })
                        foreach (int op in new[] { 0, 50, 100 })
                            UiTheme.Apply(wall, glass, op);
            });

            Check("Glass: семплирование при включённых обоях", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "nekosmoke");
                Directory.CreateDirectory(dir);
                string png = Path.Combine(dir, "glass.png");
                try
                {
                    using (var bmp = new System.Drawing.Bitmap(200, 120))
                    {
                        using var g = System.Drawing.Graphics.FromImage(bmp);
                        g.Clear(System.Drawing.Color.Purple);
                        bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    var m = typeof(MainWindow);
                    var dec = m.GetMethod("DecodeWallpaper", BindingFlags.NonPublic | BindingFlags.Static)!;
                    var src = dec.Invoke(null, new object[] { png });
                    if (src == null) throw new Exception("decode вернул null");
                    var img = (System.Windows.Controls.Image)GetField(win, "WallpaperImage")!;
                    img.Source = (System.Windows.Media.ImageSource)src;
                    UiTheme.GlassBlur = 60;
                    UiTheme.Apply(true, true, 50);
                    win.UpdateLayout();
                    // Хосты должны были прицепиться (карточки+хром+статы).
                    var glassT = typeof(MainWindow).Assembly.GetType("NekoStrap.Wpf.Glass")!;
                    var hostT = glassT.GetNestedType("Host", BindingFlags.NonPublic)!;
                    var allF = hostT.GetField("All", BindingFlags.NonPublic | BindingFlags.Static)!;
                    var all = (System.Collections.ICollection)allF.GetValue(null)!;
                    if (all.Count == 0) throw new Exception("ни один Glass-хост не прицепился");
                    Console.WriteLine("  (glass-хостов: " + all.Count + ")");
                    // Видимость сэмплов в headless не проверить: непоказанное окно
                    // не раскладывается (Border 0x0), layout-события не фаерят.
                    // Проверяем структуру: каждый хост перестроил Border в Grid
                    // [блюр, тинт, контент] — размеры и видимость придут живым layout'ом.
                    var gridF = hostT.GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    var bordF = hostT.GetField("_border", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    int ok = 0;
                    foreach (var h in all)
                    {
                        var gr = (System.Windows.Controls.Grid?)gridF.GetValue(h);
                        var b = (System.Windows.Controls.Border)bordF.GetValue(h)!;
                        if (gr != null && gr.Children.Count == 3 && ReferenceEquals(b.Child, gr))
                            ok++;
                    }
                    Console.WriteLine("  (корректно перестроено: " + ok + ")");
                    if (ok == 0) throw new Exception("Glass-хосты не перестроили панели");
                    UiTheme.Apply(false, false, 85);
                    win.UpdateLayout();
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch { /* ignore */ }
                }
            });

            Check("Звуки: все шесть (должны быть слышны тихие клики)", () =>
            {
                ClickSound.Enabled = true;
                ClickSound.PlayClick();
                ClickSound.PlayHover();
                ClickSound.PlayOpen();
                ClickSound.PlayClose();
                ClickSound.PlayToggle(true);
                ClickSound.PlayToggle(false);
                System.Threading.Thread.Sleep(400); // дать доиграть
            });

            Check("Звуки: кастомный wav + громкости", () =>
            {
                if (ClickSound.Sounds.Count != 6)
                    throw new Exception("звуков не 6: " + ClickSound.Sounds.Count);
                string dir = Path.Combine(Path.GetTempPath(), "nekosmoke");
                Directory.CreateDirectory(dir);
                try
                {
                    string good = Path.Combine(dir, "custom.wav");
                    File.WriteAllBytes(good, BuildSineWav());
                    string bad = Path.Combine(dir, "bad.wav");
                    File.WriteAllBytes(bad, new byte[] { 1, 2, 3, 4 });
                    if (!ClickSound.TrySetCustom("click", good, out string err))
                        throw new Exception("свой wav не взялся: " + err);
                    if (ClickSound.GetCustomPath("click") != good)
                        throw new Exception("путь не запомнился");
                    if (ClickSound.TrySetCustom("click", bad, out _))
                        throw new Exception("мусор приняли за wav");
                    if (ClickSound.TrySetCustom("nope", good, out _))
                        throw new Exception("неизвестный ключ приняли");
                    ClickSound.SetSoundVolume("click", 0.5f);
                    if (Math.Abs(ClickSound.GetSoundVolume("click") - 0.5f) > 0.001)
                        throw new Exception("громкость не выставилась");
                    ClickSound.SetSoundVolume("click", 5f); // кламп к 1
                    if (Math.Abs(ClickSound.GetSoundVolume("click") - 1f) > 0.001)
                        throw new Exception("кламп не работает");
                    ClickSound.PlayClick(); // кастом должен играться без падения
                    ClickSound.ClearCustom("click");
                    if (ClickSound.GetCustomPath("click") != null)
                        throw new Exception("сброс не сработал");
                    // Битые пути из конфига — молча мимо, без исключений.
                    ClickSound.LoadFromConfig(
                        new Dictionary<string, string> { { "click", Path.Combine(dir, "нет-такого.wav") } },
                        new Dictionary<string, float> { { "click", 0.3f } });
                    if (Math.Abs(ClickSound.GetSoundVolume("click") - 0.3f) > 0.001)
                        throw new Exception("громкость из конфига не взялась");
                    ClickSound.SetSoundVolume("click", 1f);
                    System.Threading.Thread.Sleep(300);
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch { /* ignore */ }
                }
            });

            Check("Декод PNG/JPG + миниатюра", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "nekosmoke");
                Directory.CreateDirectory(dir);
                try
                {
                    string png = Path.Combine(dir, "t.png");
                    string jpg = Path.Combine(dir, "t.jpg");
                    using (var bmp = new System.Drawing.Bitmap(64, 48))
                    {
                        using var g = System.Drawing.Graphics.FromImage(bmp);
                        g.Clear(System.Drawing.Color.Red);
                        bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                        bmp.Save(jpg, System.Drawing.Imaging.ImageFormat.Jpeg);
                    }
                    var m = typeof(MainWindow);
                    var dec = m.GetMethod("DecodeWallpaper", BindingFlags.NonPublic | BindingFlags.Static)!;
                    var thumb = m.GetMethod("MakeWallpaperThumb", BindingFlags.NonPublic | BindingFlags.Static)!;
                    foreach (var f in new[] { png, jpg })
                    {
                        var src = dec.Invoke(null, new object[] { f });
                        if (src == null) throw new Exception("DecodeWallpaper вернул null: " + f);
                        var th = thumb.Invoke(null, new object[] { f });
                        if (th == null) throw new Exception("thumb null: " + f);
                    }
                    // webp-ветка через Skia: кодируем и декодируем.
                    string webp = Path.Combine(dir, "t.webp");
                    using (var sk = new SkiaSharp.SKBitmap(32, 32))
                    {
                        using var c = new SkiaSharp.SKCanvas(sk);
                        c.Clear(SkiaSharp.SKColors.Blue);
                        using var img = SkiaSharp.SKImage.FromBitmap(sk);
                        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Webp, 90);
                        File.WriteAllBytes(webp, data.ToArray());
                    }
                    if (dec.Invoke(null, new object[] { webp }) == null)
                        throw new Exception("webp не декодировался");
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch { /* ignore */ }
                }
            });

            Check("GIF-плеер: статичная гифка (загрузка/кадр/stop/dispose)", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "nekosmoke");
                Directory.CreateDirectory(dir);
                string gif = Path.Combine(dir, "t.gif");
                try
                {
                    using (var bmp = new System.Drawing.Bitmap(16, 16))
                    {
                        using var g = System.Drawing.Graphics.FromImage(bmp);
                        g.Clear(System.Drawing.Color.Green);
                        bmp.Save(gif, System.Drawing.Imaging.ImageFormat.Gif);
                    }
                    var img = new System.Windows.Controls.Image();
                    using var player = GifPlayer.TryCreate(gif, img);
                    if (player == null) throw new Exception("TryCreate вернул null");
                    player.Start(); // неанимированная — no-op, но не должен упасть
                    player.Stop();
                    if (img.Source == null) throw new Exception("первый кадр не отрисовался");
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch { /* ignore */ }
                }
            });

            Check("Диалоги конструируются", () =>
            {
                var tune = new ColorTuneWindow(new LauncherConfig(), () => { }, () => { });
                tune.Measure(new Size(360, 500));
                tune.Arrange(new Rect(0, 0, 360, 500));
                tune.Close();
            });

            Check("Музыка: сканирование папки (без воспроизведения)", () =>
            {
                var music = new NekoStrap.Media.MusicManager();
                try { music.Refresh(); }
                finally { try { music.Dispose(); } catch { /* ignore */ } }
            });

            Check("Профили/избранное: roundtrip в песочнице", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "nekostores");
                Directory.CreateDirectory(dir);
                string prev = NekoStrap.Roblox.RobloxPaths.BaseDir;
                try
                {
                    NekoStrap.Roblox.RobloxPaths.Configure(dir);
                    NekoStrap.Roblox.FlagProfiles.SaveProfile("FPS",
                        new Dictionary<string, string> { { "A", "1" } });
                    if (!NekoStrap.Roblox.FlagProfiles.List().Contains("FPS"))
                        throw new Exception("профиль не сохранился");
                    var got = NekoStrap.Roblox.FlagProfiles.Get("FPS");
                    if (got == null || got["A"] != "1")
                        throw new Exception("профиль не читается");
                    NekoStrap.Roblox.FlagProfiles.Delete("FPS");
                    if (NekoStrap.Roblox.FlagProfiles.List().Count != 0)
                        throw new Exception("профиль не удалился");
                    NekoStrap.Roblox.FavoritesStore.Add(123, "Тест");
                    if (!NekoStrap.Roblox.FavoritesStore.Contains(123))
                        throw new Exception("фаворит не добавился");
                    var favs = NekoStrap.Roblox.FavoritesStore.Load();
                    if (favs.Count != 1 || favs[0].Name != "Тест")
                        throw new Exception("фаворит не читается");
                    NekoStrap.Roblox.FavoritesStore.Remove(123);
                    if (NekoStrap.Roblox.FavoritesStore.Load().Count != 0)
                        throw new Exception("фаворит не удалился");
                    flags.SetProfiles(new[] { "FPS" });
                    home.SetFavorites(new List<(long, string)> { (123, "Тест") });
                    home.SetFavorites(new List<(long, string)>());
                    win.UpdateLayout();
                }
                finally
                {
                    NekoStrap.Roblox.RobloxPaths.Configure(prev);
                    try { Directory.Delete(dir, true); } catch { /* ignore */ }
                }
            });

            Check("Оформление: шрифты и цвета живьём", () =>
            {
                settings.SetAppearance("Segoe UI", "Verdana", "Consolas",
                    "#FFFFFF", "#888888", "#444444");
                var cfg = new NekoStrap.Roblox.LauncherConfig
                {
                    ThemeFontHeading = "Segoe UI",
                    ThemeFontBody = "Verdana",
                    ThemeFontMono = "Consolas",
                    ThemeFg = "#FFFFFF",
                    ThemeDim = "#888888",
                    ThemeDimmer = "#444444"
                };
                NekoStrap.Wpf.UiTheme.ApplyText(cfg);
                var res = Application.Current.Resources;
                var fg = (System.Windows.Media.SolidColorBrush)res["FgBrush"];
                if (fg.Color != System.Windows.Media.Colors.White)
                    throw new Exception("цвет не применился");
                var ui = (System.Windows.Media.FontFamily)res["UiFont"];
                if (!ui.Source.Contains("Verdana"))
                    throw new Exception("шрифт не применился: " + ui.Source);
                // Мусор не роняет, падают дефолты.
                cfg.ThemeFg = "мусор";
                cfg.ThemeFontBody = "НетТакогоШрифта";
                NekoStrap.Wpf.UiTheme.ApplyText(cfg);
                fg = (System.Windows.Media.SolidColorBrush)res["FgBrush"];
                if (fg.Color != (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F2F2EF"))
                    throw new Exception("дефолт цвета не вернулся");
                NekoStrap.Wpf.UiTheme.ApplyText(new NekoStrap.Roblox.LauncherConfig());
            });

            Check("Апдейтер: сравнение версий (без сети)", () =>
            {
                string cur = NekoStrap.Roblox.AppUpdater.CurrentVersion;
                if (cur.Length == 0) throw new Exception("пустая версия");
                static bool newer(string c, string t) =>
                    NekoStrap.Roblox.AppUpdater.IsNewer(c, t);
                if (!newer("1.0.0", "v1.0.1")) throw new Exception("1.0.0 < v1.0.1");
                if (!newer("1.0.0", "1.1")) throw new Exception("короткий тег");
                if (newer("1.0.0", "1.0.0")) throw new Exception("равные — не новее");
                if (newer("1.0.1", "v1.0.0")) throw new Exception("старый тег — новее?");
                if (newer("1.0.0", "latest")) throw new Exception("мусор приняли");
                if (newer("abc", "v1.0.0")) throw new Exception("мусор текущей приняли");
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("FATAL: " + ex);
            _fail++;
        }
        return Report();
    }

    private static object? GetField(object o, string name)
    {
        return o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);
    }

    /// <summary>Минимальный валидный wav: синус 440 Гц, 100 мс, 44100/16/моно.</summary>
    private static byte[] BuildSineWav()
    {
        const int rate = 44100;
        int n = rate / 10;
        var samples = new short[n];
        for (int i = 0; i < n; i++)
            samples[i] = (short)(Math.Sin(2 * Math.PI * 440 * i / rate) * short.MaxValue * 0.5);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + n * 2);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(rate);
        bw.Write(rate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(n * 2);
        foreach (var s in samples)
            bw.Write(s);
        bw.Flush();
        return ms.ToArray();
    }

    private static void Check(string name, Action body)
    {
        try
        {
            body();
            _pass++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            _fail++;
            Console.WriteLine("FAIL " + name + " :: " + ex.GetType().Name + ": " + ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
    }

    private static int Report()
    {
        Console.WriteLine($"--- итог: PASS {_pass}, FAIL {_fail} ---");
        return _fail;
    }
}
