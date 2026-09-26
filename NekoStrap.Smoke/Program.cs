using System.Net.Sockets;
using System.Reflection;
using System.Text;
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
/// буфер обмена и внешнюю сеть не трогает (загрузку пакетов крутит на
/// локальном HTTP-сервере 127.0.0.1). Код возврата = число провалов.
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
                mods.SetProfiles(new List<string> { "Default", "Pack" }, "Pack", 2);
                mods.BuildQuickRows();
                host.Content = mods;
                mods.UpdateLayout();
                win.UpdateLayout();
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

            Check("Flags: drag&drop логика", () =>
            {
                flags.SetFlags(new Dictionary<string, string> { { "AFlag", "1" }, { "BFlag", "2" } });
                flags.ApplyPresetDrop(FastFlagsPage.BuiltinPresets[1]);      // FPS 144
                if (flags.CollectFlags().Count != 3 ||
                    flags.CollectFlags()["DFIntTaskSchedulerTargetFps"] != "144")
                    throw new Exception("ApplyPresetDrop не добавил флаг");
                flags.ApplyPresetDrop(FastFlagsPage.BuiltinPresets[2]);      // FPS 240
                if (flags.CollectFlags()["DFIntTaskSchedulerTargetFps"] != "240")
                    throw new Exception("ApplyPresetDrop не обновил значение");
                if (flags.CollectFlags().Count != 3) throw new Exception("ApplyPresetDrop задублировал");
                flags.ReorderRow(2, 0);
                if (flags.RowNames()[0] != "DFIntTaskSchedulerTargetFps")
                    throw new Exception("ReorderRow не поднял строку наверх");
                flags.ReorderRow(0, 99);                                    // краевые значения
                if (flags.RowNames()[^1] != "DFIntTaskSchedulerTargetFps")
                    throw new Exception("ReorderRow не увёл строку в конец");
                flags.SetFlags(new Dictionary<string, string>());
            });

            Check("Flags: импорт — разбор JSON и конфликты", () =>
            {
                const string json = """
                    {"DFIntTaskSchedulerTargetFps":240,"FFlagEnableShaders":"true","Same":"x"}
                    """;
                var incoming = NekoStrap.Roblox.FastFlagStore.ParseImportJson(json);
                if (incoming.Count != 3 ||
                    incoming["DFIntTaskSchedulerTargetFps"] != "240" ||
                    incoming["FFlagEnableShaders"] != "true")
                    throw new Exception("ParseImportJson разобрал неверно: " + incoming.Count);

                var current = new Dictionary<string, string>
                {
                    ["DFIntTaskSchedulerTargetFps"] = "144",
                    ["Same"] = "x"
                };
                var conflicts = NekoStrap.Roblox.FastFlagStore.FindConflicts(current, incoming);
                if (conflicts.Count != 1 ||
                    conflicts[0].Key != "DFIntTaskSchedulerTargetFps" ||
                    conflicts[0].Value != "240")
                    throw new Exception("FindConflicts насчитал не то: " + conflicts.Count);

                bool rejected = false;
                try { NekoStrap.Roblox.FastFlagStore.ParseImportJson("[1,2]"); }
                catch (FormatException) { rejected = true; }
                if (!rejected) throw new Exception("JSON-массив не отвергнут");

                var dialog = new NekoStrap.Wpf.FlagConflictDialog(
                    "DFIntTaskSchedulerTargetFps", "144", "240");
                if (dialog.Choice != NekoStrap.Wpf.FlagConflictChoice.Keep)
                    throw new Exception("диалог по умолчанию не «Не заменять»");
                dialog.Close();
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
                ov.SetHint("подсказка");
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

            Check("Моды: профили, откат, быстрая замена (песочница)", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "nekomods");
                Directory.CreateDirectory(dir);
                string prev = NekoStrap.Roblox.RobloxPaths.BaseDir;
                string vdir = "";
                try
                {
                    NekoStrap.Roblox.RobloxPaths.Configure(dir);
                    vdir = Path.Combine(NekoStrap.Roblox.RobloxPaths.VersionsDir,
                        "version-0123456789abcdef");

                    // Старая папка Mods мигрирует в профиль Default.
                    string legacy = NekoStrap.Roblox.RobloxPaths.ModsDir;
                    Directory.CreateDirectory(Path.Combine(legacy, "content", "sounds"));
                    File.WriteAllText(Path.Combine(legacy, "content", "sounds", "ouch.ogg"), "oof");
                    if (!NekoStrap.Roblox.ModManager.Profiles().Contains("Default"))
                        throw new Exception("миграция Mods не создала Default");
                    if (NekoStrap.Roblox.ModManager.List().Count != 1)
                        throw new Exception("файл из Mods не переехал в профиль");

                    // Быстрая замена живёт в активном профиле.
                    string font = Path.Combine(dir, "test.ttf");
                    File.WriteAllText(font, "ttf");
                    if (NekoStrap.Roblox.QuickAssets.Install("fonts", new[] { font }) != 1)
                        throw new Exception("шрифт не встал в профиль");
                    if (!NekoStrap.Roblox.QuickAssets.IsInstalled("fonts"))
                        throw new Exception("слот шрифтов не видит файл");
                    if (!NekoStrap.Roblox.QuickAssets.Reset("fonts"))
                        throw new Exception("слот шрифтов не сбросился");
                    if (NekoStrap.Roblox.QuickAssets.IsInstalled("fonts"))
                        throw new Exception("слот шрифтов остался после сброса");

                    // Фальшивая установка: оригинал в клиенте, мод в профиле.
                    Directory.CreateDirectory(Path.Combine(vdir, "content", "sounds"));
                    File.WriteAllText(Path.Combine(vdir, "content", "sounds", "ouch.ogg"), "stock");
                    NekoStrap.Roblox.ModManager.ApplyTo(vdir);
                    if (File.ReadAllText(Path.Combine(vdir, "content", "sounds", "ouch.ogg")) != "oof")
                        throw new Exception("профиль не наложился");

                    // Тот же набор второй раз — подпись сходится, файл цел.
                    if (NekoStrap.Roblox.ModManager.ApplyTo(vdir) != 1)
                        throw new Exception("повторное применение не сработало");
                    if (File.ReadAllText(Path.Combine(vdir, "content", "sounds", "ouch.ogg")) != "oof")
                        throw new Exception("повторное применение испортило файл");

                    // Порча файла в клиенте (тот же размер!) — накладываем
                    // заново, а бэкап-оригинал перезаписывать нельзя.
                    string ouch = Path.Combine(vdir, "content", "sounds", "ouch.ogg");
                    File.WriteAllText(ouch, "bad");
                    if (NekoStrap.Roblox.ModManager.ApplyTo(vdir) != 1)
                        throw new Exception("порченый файл не переналожился");
                    if (File.ReadAllText(ouch) != "oof")
                        throw new Exception("файл после порчи не вернулся");

                    // Пустой профиль = ваниль (оригинал вернулся).
                    if (!NekoStrap.Roblox.ModManager.Create("Pack", copyActive: false))
                        throw new Exception("профиль Pack не создался");
                    NekoStrap.Roblox.ModManager.SetActive("Pack");
                    NekoStrap.Roblox.ModManager.ApplyTo(vdir);
                    if (File.ReadAllText(Path.Combine(vdir, "content", "sounds", "ouch.ogg")) != "stock")
                        throw new Exception("откат не вернул оригинал");

                    // Обратно — мод снова поверх.
                    NekoStrap.Roblox.ModManager.SetActive("Default");
                    NekoStrap.Roblox.ModManager.ApplyTo(vdir);
                    if (File.ReadAllText(Path.Combine(vdir, "content", "sounds", "ouch.ogg")) != "oof")
                        throw new Exception("повторное применение не сработало");

                    // Удаление неактивного профиля, активный остаётся.
                    NekoStrap.Roblox.ModManager.SetActive("Pack");
                    if (!NekoStrap.Roblox.ModManager.Delete("Default"))
                        throw new Exception("профиль Default не удалился");
                    if (!NekoStrap.Roblox.ModManager.Profiles().Contains("Pack") ||
                        NekoStrap.Roblox.ModManager.ActiveProfile() != "Pack")
                        throw new Exception("после удаления активный профиль потерялся");
                    mods.SetProfiles(NekoStrap.Roblox.ModManager.Profiles(),
                        NekoStrap.Roblox.ModManager.ActiveProfile(), 0);

                    // Чистый запуск: RevertTo откатывает и ничего не накладывает.
                    if (NekoStrap.Roblox.ModManager.RevertTo(vdir) < 1)
                        throw new Exception("RevertTo ничего не откатил");
                    if (File.ReadAllText(Path.Combine(vdir, "content", "sounds", "ouch.ogg")) != "stock")
                        throw new Exception("RevertTo не вернул оригинал");
                    if (NekoStrap.Roblox.ModManager.RevertTo(vdir) != 0)
                        throw new Exception("повторный RevertTo должен быть пустым");

                    // Экспорт/импорт: профиль → zip → новый профиль с теми же файлами
                    NekoStrap.Roblox.ModManager.SetActive("Pack");
                    NekoStrap.Roblox.ModManager.AddFiles(new[] { font });
                    string zip = Path.Combine(dir, "Pack.zip");
                    NekoStrap.Roblox.ModManager.ExportProfile("Pack", zip);
                    if (!File.Exists(zip) || new FileInfo(zip).Length == 0)
                        throw new Exception("экспорт не создал zip");
                    string imported = NekoStrap.Roblox.ModManager.ImportProfile(zip);
                    if (imported != "Pack (2)")
                        throw new Exception("импорт не подобрал свободное имя: " + imported);
                    if (!File.Exists(Path.Combine(
                            NekoStrap.Roblox.ModManager.ProfileDir(imported), "test.ttf")))
                        throw new Exception("файл не переехал при импорте");
                    if (NekoStrap.Roblox.ModManager.Profiles().Count != 2)
                        throw new Exception("после импорта профилей: " +
                            NekoStrap.Roblox.ModManager.Profiles().Count);
                }
                finally
                {
                    NekoStrap.Roblox.RobloxPaths.Configure(prev);
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

                    // Авто-профиль на игру: привязка, частичная отвязка, пусто = удалить
                    NekoStrap.Roblox.GameProfiles.Set(new NekoStrap.Roblox.GameBinding
                    {
                        PlaceId = 999, GameName = "Тестовая",
                        ModProfile = "Anime", FlagProfile = "FPS"
                    });
                    var gb = NekoStrap.Roblox.GameProfiles.Get(999);
                    if (gb == null || gb.ModProfile != "Anime" || gb.FlagProfile != "FPS" ||
                        gb.GameName != "Тестовая")
                        throw new Exception("привязка не сохранилась");
                    gb.ModProfile = "";
                    NekoStrap.Roblox.GameProfiles.Set(gb);
                    var gb2 = NekoStrap.Roblox.GameProfiles.Get(999);
                    if (gb2 == null || gb2.FlagProfile != "FPS" || gb2.ModProfile != "")
                        throw new Exception("частичная отвязка сломалась");
                    NekoStrap.Roblox.GameProfiles.Set(new NekoStrap.Roblox.GameBinding { PlaceId = 999 });
                    if (NekoStrap.Roblox.GameProfiles.Get(999) != null ||
                        NekoStrap.Roblox.GameProfiles.List().Count != 0)
                        throw new Exception("пустая привязка не удалилась");
                    mods.SetBindStatus("не привязано");
                    flags.SetBindStatus("не привязано");

                    // Чистый запуск: снимок «до» переживает перезапуск
                    NekoStrap.Roblox.VanillaStore.Save(new NekoStrap.Roblox.VanillaState
                    {
                        VersionGuid = "version-0123456789abcdef",
                        ModProfile = "Anime",
                        FlagProfile = "FPS",
                        Flags = new Dictionary<string, string> { { "DFIntTaskSchedulerTargetFps", "60" } }
                    });
                    var vs = NekoStrap.Roblox.VanillaStore.Load();
                    if (vs == null || vs.ModProfile != "Anime" || vs.FlagProfile != "FPS" ||
                        vs.Flags.Count != 1 || vs.Flags["DFIntTaskSchedulerTargetFps"] != "60")
                        throw new Exception("снимок чистого запуска не сохранился");
                    NekoStrap.Roblox.VanillaStore.Clear();
                    if (NekoStrap.Roblox.VanillaStore.Exists ||
                        NekoStrap.Roblox.VanillaStore.Load() != null)
                        throw new Exception("снимок не удалился");

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

            Check("Целостность: снимок, порча, правка модом", () =>
            {
                // Песочница со своими Versions/ModState/ModProfiles: реальные
                // каталоги не трогаем, и PruneStates не рвёт состояние — он
                // держит только те папки, что лежат в Versions\<guid>.
                string sandbox = Path.Combine(Path.GetTempPath(), "neko-integrity");
                Directory.CreateDirectory(sandbox);
                string prevBase = NekoStrap.Roblox.RobloxPaths.BaseDir;
                try
                {
                    NekoStrap.Roblox.RobloxPaths.Configure(sandbox);
                    string dir = Path.Combine(NekoStrap.Roblox.RobloxPaths.VersionsDir,
                        "version-0123456789abcdef");
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, NekoStrap.Roblox.RobloxPaths.PlayerExe), "exe");
                    File.WriteAllText(Path.Combine(dir, "AppSettings.xml"), "<x/>");
                    File.WriteAllText(Path.Combine(dir, "content.txt"), "hello");

                    NekoStrap.Roblox.Integrity.WriteBaseline(dir);
                    if (!File.Exists(NekoStrap.Roblox.Integrity.BaselinePath(dir)))
                        throw new Exception("снимок не записался");

                    var r = NekoStrap.Roblox.Integrity.Check(dir);
                    if (!r.Ok) throw new Exception("чистая версия не прошла: " + r.Issues[0].Kind);
                    if (r.Checked != 3) throw new Exception("в снимке не 3 файла: " + r.Checked);

                    // Подмена на файл другого размера — ловим.
                    File.WriteAllText(Path.Combine(dir, "content.txt"), "changed!!");
                    r = NekoStrap.Roblox.Integrity.Check(dir);
                    if (r.Ok) throw new Exception("подмена размера не поймана");
                    if (r.Issues.Count != 1 || r.Issues[0].Kind != NekoStrap.Roblox.Integrity.KindSize)
                        throw new Exception("не тот вид проблемы: " + r.Issues[0].Kind);

                    // Удаление обязательного файла — ловим.
                    File.Delete(Path.Combine(dir, NekoStrap.Roblox.RobloxPaths.PlayerExe));
                    r = NekoStrap.Roblox.Integrity.Check(dir);
                    if (r.Issues.All(i => i.Kind != NekoStrap.Roblox.Integrity.KindMissing))
                        throw new Exception("удаление exe не поймано");

                    // Файл, положенный модом, — не порча: его размер осознанно другой.
                    File.WriteAllText(Path.Combine(dir, NekoStrap.Roblox.RobloxPaths.PlayerExe), "exe");
                    File.WriteAllText(Path.Combine(dir, "content.txt"), "hello");
                    string modSrc = Path.Combine(sandbox, "content.txt");
                    File.WriteAllText(modSrc, "much longer than hello!");
                    NekoStrap.Roblox.ModManager.AddFiles(new[] { modSrc });
                    if (NekoStrap.Roblox.ModManager.ApplyTo(dir) == 0)
                        throw new Exception("мод не наложился на фейковую версию");
                    if (NekoStrap.Roblox.ModManager.AppliedPaths(dir).Count == 0)
                        throw new Exception("manifest правок не записался");
                    r = NekoStrap.Roblox.Integrity.Check(dir);
                    if (!r.Ok)
                        throw new Exception("правка модом сочтена за порчу: "
                            + string.Join(",", r.Issues.Select(i => i.Kind + "=" + i.Path)));
                }
                finally
                {
                    NekoStrap.Roblox.RobloxPaths.Configure(prevBase);
                    try { Directory.Delete(sandbox, true); } catch { /* ignore */ }
                }
            });

            Check("Поиск игр: разбор ответа API", () =>
            {
                // Срез настоящего ответа omni-search: игра с rootPlaceId,
                // дубль того же плейса, объект только с каноническим путём,
                // объект без идентификатора и группа не-игр.
                const string json = """
                    {"searchResults":[
                      {"contentGroupType":"Game","contents":[
                        {"universeId":383310974,"name":"Adopt Me!","playerCount":163113,
                         "creatorName":"Uplift Games","rootPlaceId":920587237},
                        {"universeId":383310974,"name":"Dup","rootPlaceId":920587237},
                        {"universeId":456,"name":"Path Only",
                         "canonicalUrlPath":"/games/777777/Something-Else"}
                      ]},
                      {"contentGroupType":"User","contents":[{"name":"someone"}]},
                      {"contentGroupType":"Game","contents":[{"universeId":789,"name":"NoIds"}]}
                    ]}
                    """;
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var hits = NekoStrap.Roblox.GameSearch.Parse(doc.RootElement);
                if (hits.Count != 2)
                    throw new Exception("ожидалось 2 игры, получено " + hits.Count);
                var h = hits[0];
                if (h.PlaceId != 920587237 || h.UniverseId != 383310974 ||
                    h.Players != 163113 || h.Creator != "Uplift Games" ||
                    h.Name != "Adopt Me!")
                    throw new Exception("первая игра разобрана неверно: " + h);
                if (hits[1].PlaceId != 777777 || hits[1].UniverseId != 456 ||
                    hits[1].Players != 0)
                    throw new Exception("фолбэк по canonicalUrlPath не сработал: " + hits[1]);

                const string iconsJson = """
                    {"data":[
                      {"targetId":383310974,"state":"Completed","imageUrl":"https://t0.rbxcdn.com/abc"},
                      {"targetId":2,"state":"Pending"}
                    ]}
                    """;
                var icons = NekoStrap.Roblox.GameSearch.ParseIcons(iconsJson);
                if (icons.Count != 1 ||
                    icons[383310974] != "https://t0.rbxcdn.com/abc")
                    throw new Exception("иконки разобраны неверно: " + icons.Count);
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

            RunLoadChecks();
        }
        catch (Exception ex)
        {
            Console.WriteLine("FATAL: " + ex);
            _fail++;
        }
        return Report();
    }

    // ================= Загрузка пакетов =================

    /// <summary>
    /// Проверки PackageBatch/Deployment: параллель с лимитом потоков,
    /// возобновление готового, перекачка битого, живой Range по локальному
    /// HTTP (никакой внешней сети).
    /// </summary>
    private static void RunLoadChecks()
    {
        // await'ы продолжаются в SynchronizationContext, а он в smoke —
        // диспетчер WPF, который не крутится: GetResult() на главном потоке
        // с ним виснет насовсем. Гасим контекст на всё время проверок.
        var prevCtx = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            RunLoadChecksCore();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevCtx);
        }
    }

    private static void RunLoadChecksCore()
    {
        Check("Загрузка: параллельно и в лимите потоков", () =>
        {
            using var tmp = new TempDir();
            const int n = 8, limit = 4;
            var items = new List<PackageBatchItem>();
            for (int i = 0; i < n; i++)
                items.Add(new PackageBatchItem($"p{i}.zip", 4096,
                    Path.Combine(tmp.Full, $"p{i}.zip"), Path.Combine(tmp.Full, "out")));

            int cur = 0, max = 0;
            Func<PackageBatchItem, IProgress<(long, long)>?, CancellationToken, Task> fetch =
                async (it, prog, token) =>
                {
                    int now = Interlocked.Increment(ref cur);
                    InterlockedMax(ref max, now);
                    try
                    {
                        await Task.Delay(40, token);
                        var buf = new byte[it.PackedSize];
                        new Random(it.Name.GetHashCode()).NextBytes(buf);
                        await File.WriteAllBytesAsync(it.ZipPath, buf, token);
                        prog?.Report((buf.Length, buf.Length));
                    }
                    finally { Interlocked.Decrement(ref cur); }
                };

            int extracted = 0;
            var stages = new Rec<PackageBatchStage>();
            var stats = PackageBatch.RunAsync(items, fetch, (it, token) =>
            {
                if (!File.Exists(it.ZipPath)) throw new InvalidDataException("нет зипа " + it.Name);
                if (new FileInfo(it.ZipPath).Length != it.PackedSize)
                    throw new InvalidDataException("битый зип " + it.Name);
                Interlocked.Increment(ref extracted);
            }, limit, stages, CancellationToken.None).GetAwaiter().GetResult();

            if (stats.Fetched != n) throw new Exception($"скачано {stats.Fetched} из {n}");
            if (stats.Extracted != n || extracted != n) throw new Exception("распаковано " + stats.Extracted);
            if (max > limit) throw new Exception("лимит потоков не сработал: " + max);
            if (max < 2) throw new Exception("качалось по очереди, не параллельно: " + max);

            var list = stages.Snapshot();
            var dl = list.Where(s => s.Phase == "download").ToArray();
            var ex = list.Where(s => s.Phase == "extract").ToArray();
            if (dl.Length == 0 || ex.Length == 0) throw new Exception("нет стадий download/extract");
            if (dl.Max(s => s.Fraction ?? 0) > 0.851) throw new Exception("загрузка вылезла за 0.85");
            if (dl.Max(s => s.Fraction ?? 0) <= 0) throw new Exception("прогресс загрузки не рос");
            if (ex.Min(s => s.Fraction ?? 1) < 0.85) throw new Exception("распаковка ушла назад за 0.85");
            if (ex.Max(s => s.Fraction ?? 0) < 0.999) throw new Exception("финал распаковки не 1.0");
        });

        Check("Загрузка: возобновление не качает готовое", () =>
        {
            using var tmp = new TempDir();
            var items = new List<PackageBatchItem>();
            for (int i = 0; i < 3; i++)
                items.Add(new PackageBatchItem($"r{i}.zip", 2048,
                    Path.Combine(tmp.Full, $"r{i}.zip"), Path.Combine(tmp.Full, "out")));
            File.WriteAllBytes(items[1].ZipPath, new byte[2048]); // уже целый

            Func<PackageBatchItem, IProgress<(long, long)>?, CancellationToken, Task> fetch =
                (it, prog, token) =>
                {
                    var buf = new byte[it.PackedSize];
                    File.WriteAllBytes(it.ZipPath, buf);
                    prog?.Report((buf.Length, buf.Length));
                    return Task.CompletedTask;
                };

            var stats = PackageBatch.RunAsync(items, fetch,
                (it, token) =>
                {
                    if (new FileInfo(it.ZipPath).Length != it.PackedSize)
                        throw new InvalidDataException("битый " + it.Name);
                    Directory.CreateDirectory(it.DestDir);
                }, 4, null, CancellationToken.None).GetAwaiter().GetResult();

            if (stats.Resumed != 1) throw new Exception("возобновлений " + stats.Resumed);
            if (stats.Fetched != 2) throw new Exception("скачано " + stats.Fetched);
            if (stats.Extracted != 3) throw new Exception("распаковано " + stats.Extracted);
        });

        Check("Загрузка: битый размер перекачивается", () =>
        {
            using var tmp = new TempDir();
            var item = new PackageBatchItem("bad.zip", 3000,
                Path.Combine(tmp.Full, "bad.zip"), Path.Combine(tmp.Full, "out"));
            int calls = 0;
            Func<PackageBatchItem, IProgress<(long, long)>?, CancellationToken, Task> fetch =
                (it, prog, token) =>
                {
                    // первый раз — обрыв (на 7 байт меньше), второй — норма
                    int n = Interlocked.Increment(ref calls);
                    File.WriteAllBytes(it.ZipPath, new byte[n == 1 ? it.PackedSize - 7 : it.PackedSize]);
                    return Task.CompletedTask;
                };

            var stats = PackageBatch.RunAsync(new[] { item }, fetch,
                (it, token) =>
                {
                    if (new FileInfo(it.ZipPath).Length != it.PackedSize)
                        throw new InvalidDataException("битый");
                    Directory.CreateDirectory(it.DestDir);
                }, 2, null, CancellationToken.None).GetAwaiter().GetResult();

            if (stats.Fetched != 2) throw new Exception("попыток " + stats.Fetched);
            if (stats.Retries != 1) throw new Exception("перекачек " + stats.Retries);
            if (new FileInfo(item.ZipPath).Length != 3000) throw new Exception("файл не докачался");
            if (stats.Extracted != 1) throw new Exception("распаковано " + stats.Extracted);
        });

        Check("Загрузка: живой HTTP, Range, битый зип", () =>
        {
            var prevCtx = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                using var server = new MiniHttpServer();
                using var tmp = new TempDir();
                var rnd = new Random(11);
                var a = new byte[40_000]; rnd.NextBytes(a);
                var b = new byte[60_000]; rnd.NextBytes(b);
                var c = new byte[30_000]; rnd.NextBytes(c);
                server.Put("/a.zip", a);
                server.Put("/b.zip", b);
                server.Put("/c.zip", c);

                var items = new List<PackageBatchItem>
                {
                    new("a.zip", a.Length, Path.Combine(tmp.Full, "a.zip"), Path.Combine(tmp.Full, "out")),
                    new("b.zip", b.Length, Path.Combine(tmp.Full, "b.zip"), Path.Combine(tmp.Full, "out")),
                    new("c.zip", c.Length, Path.Combine(tmp.Full, "c.zip"), Path.Combine(tmp.Full, "out")),
                };
                string host = $"http://127.0.0.1:{server.Port}";

                Func<PackageBatchItem, IProgress<(long, long)>?, CancellationToken, Task> fetch =
                    (it, prog, token) => Deployment.DownloadPackageAsync(
                        $"{host}/{it.Name}", it.ZipPath, prog, it.PackedSize, token);

                void Extract(PackageBatchItem it, CancellationToken token)
                {
                    if (!File.Exists(it.ZipPath)) throw new InvalidDataException("нет " + it.Name);
                    var got = File.ReadAllBytes(it.ZipPath);
                    byte[] want = it.Name switch
                    {
                        "a.zip" => a, "b.zip" => b, "c.zip" => c, _ => Array.Empty<byte>()
                    };
                    if (!got.AsSpan().SequenceEqual(want))
                        throw new InvalidDataException("битый зип " + it.Name);
                    Directory.CreateDirectory(it.DestDir);
                    File.Copy(it.ZipPath, Path.Combine(it.DestDir, it.Name), true);
                }

                // 1. полная закачка с нуля
                var stats = PackageBatch.RunAsync(items, fetch, Extract, 4, null, CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (server.Hits != 3) throw new Exception("хитов " + server.Hits);
                if (stats.Fetched != 3 || stats.Extracted != 3) throw new Exception(stats.ToString());
                SameBytes(items[0].ZipPath, a, "a.zip");
                SameBytes(items[1].ZipPath, b, "b.zip");
                SameBytes(items[2].ZipPath, c, "c.zip");

                // 2. всё уже на диске — ни одного запроса
                stats = PackageBatch.RunAsync(items, fetch, Extract, 4, null, CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (server.Hits != 3) throw new Exception("качали готовое, хитов " + server.Hits);
                if (stats.Resumed != 3 || stats.Fetched != 0) throw new Exception(stats.ToString());

                // 3. файл обрезан посередине — докачка через Range
                var half = new byte[c.Length / 2];
                c.AsSpan(0, half.Length).CopyTo(half);
                File.WriteAllBytes(items[2].ZipPath, half);
                stats = PackageBatch.RunAsync(items, fetch, Extract, 4, null, CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (server.RangeHits < 1) throw new Exception("не было Range-запроса");
                if (stats.Resumed != 2 || stats.Fetched != 1) throw new Exception(stats.ToString());
                SameBytes(items[2].ZipPath, c, "c.zip после Range");

                // 4. файл того же размера, но с мусором внутри — ловится на
                //    распаковке и перекачивается целиком
                File.WriteAllBytes(items[1].ZipPath, Mutate(b));
                int hitsBefore = server.Hits;
                stats = PackageBatch.RunAsync(items, fetch, Extract, 4, null, CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (stats.Retries != 1) throw new Exception("мусорный зип не перекачали");
                if (server.Hits != hitsBefore + 1) throw new Exception("хитов после сбоя: " + (server.Hits - hitsBefore));
                SameBytes(items[1].ZipPath, b, "b.zip после перекачки");
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(prevCtx);
            }
        });

        Check("Загрузка: отмена прерывает батч", () =>
        {
            var prevCtx = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                using var tmp = new TempDir();
                var items = new List<PackageBatchItem>
                {
                    new("x.zip", 100, Path.Combine(tmp.Full, "x.zip"), Path.Combine(tmp.Full, "out")),
                    new("y.zip", 100, Path.Combine(tmp.Full, "y.zip"), Path.Combine(tmp.Full, "out")),
                };
                using var cts = new CancellationTokenSource();
                Func<PackageBatchItem, IProgress<(long, long)>?, CancellationToken, Task> fetch =
                    async (it, prog, token) =>
                    {
                        await Task.Delay(5000, token);
                        File.WriteAllBytes(it.ZipPath, new byte[it.PackedSize]);
                    };

                var task = PackageBatch.RunAsync(items, fetch, (it, token) => { },
                    1, null, cts.Token);
                cts.CancelAfter(150);
                try
                {
                    task.GetAwaiter().GetResult();
                    throw new Exception("отмена не сработала");
                }
                catch (OperationCanceledException) { /* так и должно быть */ }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(prevCtx);
            }
        });
    }

    /// <summary>Тот же массив, но с подменённым байтом — «целый по размеру, битый по содержимому».</summary>
    private static byte[] Mutate(byte[] src)
    {
        var copy = (byte[])src.Clone();
        copy[copy.Length / 2] ^= 0xFF;
        return copy;
    }

    private static void SameBytes(string path, byte[] want, string what)
    {
        if (!File.Exists(path)) throw new Exception(what + ": файла нет");
        var got = File.ReadAllBytes(path);
        if (got.Length != want.Length || !got.AsSpan().SequenceEqual(want))
            throw new Exception($"{what}: байты не совпали ({got.Length} != {want.Length})");
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int cur;
        while (value > (cur = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, cur) == cur) break;
        }
    }

    /// <summary>Синхронный сборщик прогресса (Progress&lt;T&gt; отложенный — в тестах нельзя).</summary>
    private sealed class Rec<T> : IProgress<T>
    {
        private readonly List<T> _items = new();
        private readonly object _gate = new();
        public void Report(T value) { lock (_gate) _items.Add(value); }
        public T[] Snapshot() { lock (_gate) return _items.ToArray(); }
    }

    /// <summary>Временная папка под тесты (удаляется целиком).</summary>
    private sealed class TempDir : IDisposable
    {
        public readonly string Full;
        public TempDir()
        {
            Full = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "neko-smoke-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Full);
        }
        public void Dispose()
        {
            try { Directory.Delete(Full, true); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Мини HTTP-сервер на 127.0.0.1: отдаёт файлы и умеет Range — на нём
    /// проверяем докачку. TcpListener, а не http.sys: без прав администратора.
    /// </summary>
    private sealed class MiniHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();
        public readonly int Port;
        public int Hits;
        public int RangeHits;

        public MiniHttpServer()
        {
            _listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoopAsync);
        }

        public void Put(string path, byte[] data) { lock (_gate) _files[path] = data; }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { /* ignore */ }
        }

        private async Task AcceptLoopAsync()
        {
            while (true)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(); }
                catch { return; }
                _ = Task.Run(() => HandleAsync(client));
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            try
            {
                using (client)
                {
                    var stream = client.GetStream();
                    string head = await ReadHeadAsync(stream);
                    string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length == 0) return;
                    string[] parts = lines[0].Split(' ');
                    if (parts.Length < 2 || !parts[0].StartsWith("GET", StringComparison.Ordinal)) return;
                    string path = parts[1];

                    int start = 0;
                    for (int i = 1; i < lines.Length; i++)
                    {
                        if (!lines[i].StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) continue;
                        string v = lines[i].Substring(6).Trim();
                        if (v.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) v = v.Substring(6);
                        int dash = v.IndexOf('-');
                        if (dash > 0) int.TryParse(v.Substring(0, dash), out start);
                    }

                    byte[]? body;
                    lock (_gate)
                    {
                        Hits++;
                        if (start > 0) RangeHits++;
                        _files.TryGetValue(path, out body);
                    }
                    if (body == null)
                    {
                        await WriteAsciiAsync(stream,
                            "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                        return;
                    }

                    if (start > 0 && start < body.Length)
                    {
                        int len = body.Length - start;
                        await WriteAsciiAsync(stream,
                            "HTTP/1.1 206 Partial Content\r\n" +
                            $"Content-Length: {len}\r\n" +
                            $"Content-Range: bytes {start}-{body.Length - 1}/{body.Length}\r\n" +
                            "Content-Type: application/octet-stream\r\n" +
                            "Connection: close\r\n\r\n");
                        await stream.WriteAsync(body.AsMemory(start, len));
                    }
                    else
                    {
                        await WriteAsciiAsync(stream,
                            "HTTP/1.1 200 OK\r\n" +
                            $"Content-Length: {body.Length}\r\n" +
                            "Content-Type: application/octet-stream\r\n" +
                            "Connection: close\r\n\r\n");
                        await stream.WriteAsync(body);
                    }
                    stream.Flush();
                }
            }
            catch { /* обрыв клиента — норма */ }
        }

        private static async Task<string> ReadHeadAsync(NetworkStream stream)
        {
            var ms = new MemoryStream();
            var one = new byte[1];
            while (ms.Length < 16384)
            {
                int r = await stream.ReadAsync(one, 0, 1);
                if (r == 0) break;
                ms.WriteByte(one[0]);
                byte[] arr = ms.ToArray();
                int n = arr.Length;
                if (n >= 4 && arr[n - 4] == (byte)'\r' && arr[n - 3] == (byte)'\n' &&
                    arr[n - 2] == (byte)'\r' && arr[n - 1] == (byte)'\n')
                    return Encoding.ASCII.GetString(arr, 0, n - 4);
            }
            return Encoding.ASCII.GetString(ms.ToArray());
        }

        private static Task WriteAsciiAsync(NetworkStream stream, string text) =>
            stream.WriteAsync(Encoding.ASCII.GetBytes(text)).AsTask();
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
