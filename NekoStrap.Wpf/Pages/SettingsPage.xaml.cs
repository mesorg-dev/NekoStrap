using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NekoStrap.Utils;

namespace NekoStrap.Wpf.Pages;

/// <summary>
/// Настройки: тумблеры, путь Roblox, CDN, обои, стекло. API повторяет WinForms-версию
/// (минус тумблер анимаций: в WPF переходы всегда мгновенные by design).
/// </summary>
public partial class SettingsPage : UserControl
{
    public event EventHandler? SaveClicked;
    public event EventHandler? CdnApplyClicked;
    public event EventHandler? CdnRollbackClicked;
    public event EventHandler? WallpaperPickClicked;
    public event EventHandler? WallpaperResetClicked;
    public event Action<int>? WallpaperBlurChanged;
    public event Action<int>? WallpaperDimChanged;
    public event Action<int>? GlassOpacityChanged;
    public event Action<int>? GlassBlurChanged;
    public event Action<int>? GlassDimChanged;
    public event Action<bool>? GlassPureChanged;

    public event Action<string>? SoundPickClicked;
    public event Action<string>? SoundResetClicked;
    public event Action<string, int>? SoundVolumeChanged;
    public event Action? AppearanceChanged;
    public event Action? AppearanceResetClicked;
    public event Action<string>? LanguagePicked;
    public event Action<bool>? TelemetryBlockChanged;

    // Оверлей производительности поверх игры
    public event Action<bool>? PerfOverlayChanged;
    public event Action<bool>? PerfGlassChanged;
    public event Action<int>? PerfOpacityChanged;
    public event Action? PerfMetricsChanged;
    public event Action? PerfShowClicked;

    private bool _sync;
    private readonly Dictionary<string, TextBlock> _soundFiles = new();
    private readonly Dictionary<string, TextBlock> _soundValues = new();
    private readonly Dictionary<string, Slider> _soundBars = new();

    public SettingsPage()
    {
        InitializeComponent();
        // Тумблеры-фичи живут внутри FeatureExpander — вешаемся на их Toggle
        SoundsFeature.ToggleCheck.Checked += (_, _) => ClickSound.Enabled = SoundsFeature.IsOn;
        SoundsFeature.ToggleCheck.Unchecked += (_, _) => ClickSound.Enabled = SoundsFeature.IsOn;
        TelemetryFeature.ToggleCheck.Checked += (_, _) => { if (!_sync) TelemetryBlockChanged?.Invoke(true); };
        TelemetryFeature.ToggleCheck.Unchecked += (_, _) => { if (!_sync) TelemetryBlockChanged?.Invoke(false); };
        PerfFeature.ToggleCheck.Checked += (_, _) => { if (!_sync) PerfOverlayChanged?.Invoke(true); };
        PerfFeature.ToggleCheck.Unchecked += (_, _) => { if (!_sync) PerfOverlayChanged?.Invoke(false); };
        PerfGlassBox.Checked += (_, _) => { if (!_sync) PerfGlassChanged?.Invoke(true); };
        PerfGlassBox.Unchecked += (_, _) => { if (!_sync) PerfGlassChanged?.Invoke(false); };
        Action perfMetrics = () => { if (!_sync) PerfMetricsChanged?.Invoke(); };
        PerfPingBox.Checked += (_, _) => perfMetrics();
        PerfPingBox.Unchecked += (_, _) => perfMetrics();
        PerfCpuBox.Checked += (_, _) => perfMetrics();
        PerfCpuBox.Unchecked += (_, _) => perfMetrics();
        PerfRamBox.Checked += (_, _) => perfMetrics();
        PerfRamBox.Unchecked += (_, _) => perfMetrics();
        PerfGpuBox.Checked += (_, _) => perfMetrics();
        PerfGpuBox.Unchecked += (_, _) => perfMetrics();
        PerfServerBox.Checked += (_, _) => perfMetrics();
        PerfServerBox.Unchecked += (_, _) => perfMetrics();
        PerfTimeBox.Checked += (_, _) => perfMetrics();
        PerfTimeBox.Unchecked += (_, _) => perfMetrics();
        BuildSoundRows();
        BuildFontBoxes();
        BuildColorRows();
        BuildThreadsBox();
        BuildPriorityBox();
    }

    // ================= Потоки загрузки клиента =================

    /// <summary>Допустимые значения (движок клампит 1..8).</summary>
    private static readonly int[] ThreadOptions = { 1, 2, 4, 8 };

    public event Action<int>? DownloadThreadsChanged;

    private void BuildThreadsBox()
    {
        foreach (int n in ThreadOptions)
            ThreadsCombo.Items.Add(new ComboBoxItem { Content = n.ToString(), Tag = n });
        _sync = true;
        ThreadsCombo.SelectedIndex = 2; // 4 — дефолт
        _sync = false;
    }

    public int DownloadThreadsValue
    {
        get => ThreadsCombo.SelectedItem is ComboBoxItem it && it.Tag is int n ? n : 4;
        set
        {
            int idx = Array.IndexOf(ThreadOptions, value);
            if (idx < 0) idx = Array.IndexOf(ThreadOptions, 4);
            _sync = true;
            ThreadsCombo.SelectedIndex = idx;
            _sync = false;
        }
    }

    private void ThreadsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sync) return;
        if (ThreadsCombo.SelectedItem is ComboBoxItem it && it.Tag is int n)
            DownloadThreadsChanged?.Invoke(n);
    }

    // ================= Приоритет процесса клиента =================

    /// <summary>Каноничная метка (tag) + ключ подписи. Порядок = порядок в UI.</summary>
    private static readonly (string Tag, string Key)[] PriorityLabels =
    {
        ("", "Set_Prio_Default"),
        ("idle", "Set_Prio_Idle"),
        ("belownormal", "Set_Prio_Below"),
        ("abovenormal", "Set_Prio_Above"),
        ("high", "Set_Prio_High"),
    };

    /// <summary>Пользователь выбрал приоритет — применяется сразу, без «Сохранить».</summary>
    public event Action<string>? PriorityChanged;

    private void BuildPriorityBox()
    {
        foreach (var (tag, key) in PriorityLabels)
            PriorityCombo.Items.Add(new ComboBoxItem { Content = Lang.Get(key), Tag = tag });
        _sync = true;
        PriorityCombo.SelectedIndex = 0;
        _sync = false;
    }

    /// <summary>Пересобрать подписи при смене языка; выбранный пункт сохраняется.</summary>
    public void RefreshPriorityBox()
    {
        string current = RobloxPriorityValue;
        PriorityCombo.Items.Clear();
        foreach (var (tag, key) in PriorityLabels)
            PriorityCombo.Items.Add(new ComboBoxItem { Content = Lang.Get(key), Tag = tag });
        RobloxPriorityValue = current;
    }

    /// <summary>Каноничная метка из комбобокса ("" = системный приоритет).</summary>
    public string RobloxPriorityValue
    {
        get => PriorityCombo.SelectedItem is ComboBoxItem it && it.Tag is string s ? s : "";
        set
        {
            int idx = Array.FindIndex(PriorityLabels, p => p.Tag == value);
            if (idx < 0) idx = 0;
            _sync = true;
            PriorityCombo.SelectedIndex = idx;
            _sync = false;
        }
    }

    private void PriorityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sync) return;
        if (PriorityCombo.SelectedItem is ComboBoxItem it && it.Tag is string s)
            PriorityChanged?.Invoke(s);
    }

    // ================= Внешний вид =================

    // Индекс 0 = дефолт (кнопка «Сбросить»): Verdana — широкий и жирный
    // на маленьких кеглях, читается лучше тонкого Inter.
    public static readonly string[] HeadingFonts =
        { "Unbounded", "Segoe UI", "Verdana", "Trebuchet MS", "Arial Black", "Impact", "Georgia" };
    public static readonly string[] BodyFonts =
        { "Verdana", "Inter", "Segoe UI", "Tahoma", "Trebuchet MS", "Arial" };
    public static readonly string[] MonoFonts =
        { "JetBrains Mono", "Consolas", "Courier New", "Lucida Console" };

    private static readonly string[] Palette =
    {
        "#F2F2EF", "#FFFFFF", "#98989E", "#5C5C62", "#78DC82",
        "#E06C5B", "#6BA8E0", "#E8C85B", "#E09A5B", "#A78BFA"
    };

    private readonly Dictionary<string, TextBox> _colorBoxes = new();

    private void BuildFontBoxes()
    {
        FillFontBox(HeadingFontCombo, HeadingFonts);
        FillFontBox(BodyFontCombo, BodyFonts);
        FillFontBox(MonoFontCombo, MonoFonts);
    }

    private static void FillFontBox(ComboBox box, string[] families)
    {
        box.Items.Clear();
        foreach (var f in families)
            box.Items.Add(new ComboBoxItem { Content = f, Tag = f });
        box.SelectedIndex = 0;
    }

    private static string SelectedFont(ComboBox box, string[] known, string fallback)
    {
        string name = (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        foreach (var k in known)
            if (k.Equals(name, StringComparison.OrdinalIgnoreCase))
                return k;
        return fallback;
    }

    public string HeadingFontName => SelectedFont(HeadingFontCombo, HeadingFonts, HeadingFonts[0]);
    public string BodyFontName => SelectedFont(BodyFontCombo, BodyFonts, BodyFonts[0]);
    public string MonoFontName => SelectedFont(MonoFontCombo, MonoFonts, MonoFonts[0]);

    public string FgHex => _colorBoxes.TryGetValue("fg", out var b) ? b.Text.Trim() : "";
    public string DimHex => _colorBoxes.TryGetValue("dim", out var b2) ? b2.Text.Trim() : "";
    public string DimmerHex => _colorBoxes.TryGetValue("dimmer", out var b3) ? b3.Text.Trim() : "";

    private void AppearanceControl_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_sync)
            AppearanceChanged?.Invoke();
    }

    private void AppearanceResetButton_Click(object sender, RoutedEventArgs e)
    {
        AppearanceResetClicked?.Invoke();
    }

    private void LangAutoButton_Click(object sender, RoutedEventArgs e)
    {
        LanguagePicked?.Invoke("auto");
    }

    private void LangRuButton_Click(object sender, RoutedEventArgs e)
    {
        LanguagePicked?.Invoke("ru");
    }

    private void LangEnButton_Click(object sender, RoutedEventArgs e)
    {
        LanguagePicked?.Invoke("en");
    }

    /// <summary>Подсветка активной кнопки языка (auto/ru/en).</summary>
    public void SetLanguage(string lang)
    {
        var on = (Style)FindResource("PrimaryButton");
        var off = (Style)FindResource("OutlineButton");
        LangAutoButton.Style = lang == "auto" ? on : off;
        LangRuButton.Style = lang == "ru" ? on : off;
        LangEnButton.Style = lang == "en" ? on : off;
    }

    private void BuildColorRows()
    {
        ColorsPanel.Children.Clear();
        var roles = new (string key, string langKey)[]
        {
            ("fg", "Set_ColFg"),
            ("dim", "Set_ColDim"),
            ("dimmer", "Set_ColDimmer"),
        };
        foreach (var (key, langKey) in roles)
        {
            var wrap = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var caption = new TextBlock { Text = Lang.Get(langKey), Margin = new Thickness(0, 0, 0, 4) };
            caption.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
            caption.SetResourceReference(TextBlock.ForegroundProperty, "FgDimBrush");
            caption.FontSize = 12;
            wrap.Children.Add(caption);

            var line = new DockPanel { LastChildFill = true };
            var swatches = new WrapPanel { MaxWidth = 340 };
            foreach (var hex in Palette)
            {
                var sw = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(0, 0, 8, 8),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = (key, hex)
                };
                sw.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
                sw.BorderThickness = new Thickness(1);
                try
                {
                    sw.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
                }
                catch { /* ignore */ }
                sw.MouseLeftButtonUp += (_, _) =>
                {
                    if (_colorBoxes.TryGetValue(key, out var box))
                        box.Text = hex;
                    // TextChanged дёрнет AppearanceChanged сам.
                };
                swatches.Children.Add(sw);
            }
            var hexBox = new TextBox
            {
                Style = (Style)FindResource("DarkInput"),
                Width = 100,
                Height = 34,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8),
                Tag = key
            };
            hexBox.TextChanged += (_, _) =>
            {
                MarkBox(hexBox, IsHexText(hexBox.Text), Lang.Get("Set_Hex_Bad"));
                if (!_sync)
                    AppearanceChanged?.Invoke();
            };
            DockPanel.SetDock(hexBox, Dock.Right);
            line.Children.Add(hexBox);
            line.Children.Add(swatches);
            wrap.Children.Add(line);

            _colorBoxes[key] = hexBox;
            ColorsPanel.Children.Add(wrap);
        }
    }

    /// <summary>Выставить оформление из конфига (без срабатывания события).</summary>
    public void SetAppearance(string heading, string body, string mono,
        string fg, string dim, string dimmer)
    {
        _sync = true;
        try
        {
            SelectFont(HeadingFontCombo, HeadingFonts, heading);
            SelectFont(BodyFontCombo, BodyFonts, body);
            SelectFont(MonoFontCombo, MonoFonts, mono);
            if (_colorBoxes.TryGetValue("fg", out var f)) f.Text = fg;
            if (_colorBoxes.TryGetValue("dim", out var d)) d.Text = dim;
            if (_colorBoxes.TryGetValue("dimmer", out var m)) m.Text = dimmer;
        }
        finally { _sync = false; }
    }

    private static void SelectFont(ComboBox box, string[] known, string want)
    {
        for (int i = 0; i < box.Items.Count; i++)
        {
            if ((box.Items[i] as ComboBoxItem)?.Tag as string == want)
            {
                box.SelectedIndex = i;
                return;
            }
        }
        box.SelectedIndex = 0;
    }

    // ================= Валидация полей =================

    /// <summary>Шесть hex-цифр (#RRGGBB, решётка необязательна).</summary>
    private static bool IsHexText(string text)
    {
        string hex = text.Trim().TrimStart('#');
        if (hex.Length != 6) return false;
        return hex.All(c =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
    }

    /// <summary>Поле подсвечивается красным с причиной в подсказке.</summary>
    private static void MarkBox(TextBox box, bool ok, string hint)
    {
        if (ok)
        {
            box.ClearValue(Control.BorderBrushProperty);
            box.ClearValue(FrameworkElement.ToolTipProperty);
        }
        else
        {
            box.SetResourceReference(Control.BorderBrushProperty, "BadBrush");
            box.ToolTip = hint;
        }
    }

    private void FpsValueBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        bool ok = FpsValid;
        MarkBox(FpsValueBox, ok, Lang.Get("Set_Fps_Bad"));
        FpsRangeHint.SetResourceReference(TextBlock.ForegroundProperty, ok ? "FgDimmerBrush" : "BadBrush");
    }

    /// <summary>Пусто (лимита нет) либо целое 5..10000.</summary>
    public bool FpsValid
    {
        get
        {
            string t = FpsValueBox.Text.Trim();
            if (t.Length == 0) return true;
            return int.TryParse(t, out int fps) && fps >= 5 && fps <= 10000;
        }
    }

    /// <summary>Все hex-поля валидны (пустое = палитра по умолчанию).</summary>
    public bool ColorsValid =>
        _colorBoxes.Values.All(b => b.Text.Trim().Length == 0 || IsHexText(b.Text));

    /// <summary>
    /// Ряды звуков строим кодом по списку движка: подпись + громкость +
    /// файл + кнопки. Новый звук в движке = новый ряд без правок XAML.
    /// </summary>
    /// <summary>Перестроить ряды звуков (смена языка). Значения/файлы
    /// восстанавливает MainWindow.RefreshSoundsSettings следом.</summary>
    public void RefreshSoundRows()
    {
        BuildSoundRows();
    }

    /// <summary>Перестроить ряды цветов (смена языка). Hex восстанавливает
    /// MainWindow.RefreshAppearanceSettings следом.</summary>
    public void RefreshColorRows()
    {
        BuildColorRows();
    }

    private void BuildSoundRows()
    {
        SoundsPanel.Children.Clear();
        foreach (var (key, _) in ClickSound.Sounds)
        {
            var wrap = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            var head = new DockPanel { LastChildFill = true };
            var name = new TextBlock
            {
                Text = Lang.SoundTitle(key),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            name.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
            name.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
            var val = new TextBlock
            {
                Text = "100%",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            val.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            val.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
            DockPanel.SetDock(val, Dock.Right);
            head.Children.Add(val);
            head.Children.Add(name);
            wrap.Children.Add(head);

            var bar = new Slider
            {
                Style = (Style)FindResource("ThemedSlider"),
                Minimum = 0,
                Maximum = 100,
                Value = 100,
                Tag = key
            };
            bar.ValueChanged += (_, _) =>
            {
                int v = (int)bar.Value;
                val.Text = v + "%";
                if (!_sync)
                    SoundVolumeChanged?.Invoke(key, v);
            };
            wrap.Children.Add(bar);

            var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 4, 0, 0) };
            var file = new TextBlock
            {
                Text = Lang.Get("Sound_Builtin"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            file.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            file.SetResourceReference(TextBlock.ForegroundProperty, "FgDimmerBrush");
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            var pick = new Button
            {
                Content = Lang.Get("Btn_Pick"),
                Style = (Style)FindResource("OutlineButton"),
                Width = 110,
                Height = 30,
                Tag = key
            };
            pick.Click += (_, _) => SoundPickClicked?.Invoke(key);
            var reset = new Button
            {
                Content = Lang.Get("Btn_Reset"),
                Style = (Style)FindResource("OutlineButton"),
                Width = 80,
                Height = 30,
                Margin = new Thickness(8, 0, 0, 0),
                Tag = key
            };
            reset.Click += (_, _) => SoundResetClicked?.Invoke(key);
            btns.Children.Add(pick);
            btns.Children.Add(reset);
            DockPanel.SetDock(btns, Dock.Right);
            row.Children.Add(btns);
            row.Children.Add(file);
            wrap.Children.Add(row);

            _soundFiles[key] = file;
            _soundValues[key] = val;
            _soundBars[key] = bar;
            SoundsPanel.Children.Add(wrap);
        }
    }

    /// <summary>Показать файл звука (пусто = встроенный). Без срабатываний.</summary>
    public void SetSoundFile(string key, string display)
    {
        if (_soundFiles.TryGetValue(key, out var label))
            label.Text = display.Length > 0 ? display : Lang.Get("Sound_Builtin");
    }

    /// <summary>Громкость звука 0..100 (без срабатывания события).</summary>
    public void SetSoundVolume(string key, int pct)
    {
        if (!_soundBars.TryGetValue(key, out var bar)) return;
        _sync = true;
        try
        {
            bar.Value = Math.Clamp(pct, 0, 100);
            _soundValues[key].Text = ((int)bar.Value) + "%";
        }
        finally { _sync = false; }
    }

    public CheckBox CloseOnLaunchCheck => CloseOnLaunchBox;
    public CheckBox DiscordRpcCheck => DiscordFeature.ToggleCheck;
    public CheckBox DiscordNameCheck => DiscordNameBox;
    public CheckBox DiscordServerCheck => DiscordServerBox;
    public CheckBox DiscordIconCheck => DiscordIconBox;
    public CheckBox DiscordTimeCheck => DiscordTimeBox;
    public CheckBox DiscordBtnCheck => DiscordBtnFeature.ToggleCheck;
    public CheckBox DiscordAllowJoinCheck => DiscordAllowJoinBox;
    public CheckBox SoundsCheck => SoundsFeature.ToggleCheck;
    public CheckBox AutoUpdateCheck => AutoUpdateBox;
    public CheckBox RobloxBgUpdateCheck => RobloxBgUpdateBox;
    public CheckBox FpsCheck => FpsFeature.ToggleCheck;
    public CheckBox MinimizeToTrayCheck => TrayFeature.ToggleCheck;
    public CheckBox CloseToTrayCheck => CloseToTrayBox;
    public CheckBox NotificationsCheck => NotificationsBox;
    public CheckBox RobloxNoTrayCheck => RobloxNoTrayBox;
    public CheckBox RobloxNoStartupCheck => RobloxNoStartupBox;
    public CheckBox GlassCheck => GlassFeature.ToggleCheck;
    public CheckBox TelemetryBlockCheck => TelemetryFeature.ToggleCheck;
    public CheckBox PerfOverlayCheck => PerfFeature.ToggleCheck;
    public CheckBox PerfPingCheck => PerfPingBox;
    public CheckBox PerfCpuCheck => PerfCpuBox;
    public CheckBox PerfRamCheck => PerfRamBox;
    public CheckBox PerfGpuCheck => PerfGpuBox;
    public CheckBox PerfServerCheck => PerfServerBox;
    public CheckBox PerfTimeCheck => PerfTimeBox;
    public CheckBox PerfGlassCheck => PerfGlassBox;

    /// <summary>Непрозрачность оверлея, % (со слайдера, 20..100).</summary>
    public int PerfOpacity => Math.Clamp((int)PerfOpacityBar.Value, 20, 100);

    /// <summary>Статус блокировки телеметрии (без срабатывания события).</summary>
    public void SetTelemetryStatus(string text, bool ok)
    {
        TelemetryStatus.Text = text;
        TelemetryStatus.Foreground = ok
            ? (Brush)FindResource("GoodBrush")
            : (Brush)FindResource("FgDimBrush");
    }

    /// <summary>Выставить состояние тумблера без срабатывания события.</summary>
    public void SetTelemetryBlock(bool on)
    {
        _sync = true;
        TelemetryFeature.IsOn = on;
        _sync = false;
    }

    // ================= Оверлей производительности =================

    /// <summary>Всё по конфигу разом, без срабатывания событий.</summary>
    public void SetPerfState(bool overlay, bool ping, bool cpu, bool ram, bool gpu,
        bool server, bool time, bool glass, int opacity)
    {
        _sync = true;
        try
        {
            PerfFeature.IsOn = overlay;
            PerfPingBox.IsChecked = ping;
            PerfCpuBox.IsChecked = cpu;
            PerfRamBox.IsChecked = ram;
            PerfGpuBox.IsChecked = gpu;
            PerfServerBox.IsChecked = server;
            PerfTimeBox.IsChecked = time;
            PerfGlassBox.IsChecked = glass;
            int o = Math.Clamp(opacity, 20, 100);
            PerfOpacityBar.Value = o;
            PerfOpacityValue.Text = o + "%";
        }
        finally { _sync = false; }
    }

    private void PerfOpacityBar_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)Math.Clamp(e.NewValue, 20, 100);
        PerfOpacityValue.Text = v + "%";
        if (!_sync)
            PerfOpacityChanged?.Invoke(v);
    }

    private void PerfShowButton_Click(object sender, RoutedEventArgs e)
    {
        PerfShowClicked?.Invoke();
    }

    public string RobloxPathText
    {
        get => PathBox.Text.Trim();
        set => PathBox.Text = value;
    }

    public string FpsValueText
    {
        get => FpsValueBox.Text.Trim();
        set => FpsValueBox.Text = value;
    }

    /// <summary>Ключ картинки (Rich Presence Assets) — по желанию.</summary>
    public string DiscordAssetKeyText
    {
        get => DiscordAssetBox.Text.Trim();
        set => DiscordAssetBox.Text = value;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Lang.Get("Set_BrowseTitle")
        };
        if (dlg.ShowDialog() == true)
            PathBox.Text = dlg.FolderName;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveClicked?.Invoke(this, EventArgs.Empty);
    }

    private void CdnOnButton_Click(object sender, RoutedEventArgs e)
    {
        CdnApplyClicked?.Invoke(this, EventArgs.Empty);
    }

    private void CdnOffButton_Click(object sender, RoutedEventArgs e)
    {
        CdnRollbackClicked?.Invoke(this, EventArgs.Empty);
    }

    private void WallpaperPickButton_Click(object sender, RoutedEventArgs e)
    {
        WallpaperPickClicked?.Invoke(this, EventArgs.Empty);
    }

    private void WallpaperResetButton_Click(object sender, RoutedEventArgs e)
    {
        WallpaperResetClicked?.Invoke(this, EventArgs.Empty);
    }

    private void WallpaperBlurBar_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)WallpaperBlurBar.Value;
        WallpaperBlurValue.Text = v + "%";
        if (!_sync)
            WallpaperBlurChanged?.Invoke(v);
    }

    private void WallpaperDimBar_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)WallpaperDimBar.Value;
        WallpaperDimValue.Text = v + "%";
        if (!_sync)
            WallpaperDimChanged?.Invoke(v);
    }

    private void GlassOpacityBar_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)GlassOpacityBar.Value;
        GlassOpacityValue.Text = v + "%";
        if (!_sync)
            GlassOpacityChanged?.Invoke(v);
    }

    private void GlassDimBar_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)GlassDimBar.Value;
        GlassDimValue.Text = v + "%";
        if (!_sync)
            GlassDimChanged?.Invoke(v);
    }

    private void GlassTintedButton_Click(object sender, RoutedEventArgs e)
    {
        SetGlassPure(false);
        GlassPureChanged?.Invoke(false);
    }

    private void GlassClearButton_Click(object sender, RoutedEventArgs e)
    {
        SetGlassPure(true);
        GlassPureChanged?.Invoke(true);
    }

    /// <summary>Режим стекла + доступность слайдеров тинта (в чистом не нужны).</summary>
    public void SetGlassPure(bool pure)
    {
        var on = (Style)FindResource("PrimaryButton");
        var off = (Style)FindResource("OutlineButton");
        GlassTintedButton.Style = pure ? off : on;
        GlassClearButton.Style = pure ? on : off;
        GlassOpacityBar.IsEnabled = !pure;
        GlassDimBar.IsEnabled = !pure;
    }

    private void GlassBlurBar_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)GlassBlurBar.Value;
        GlassBlurValue.Text = v + "%";
        if (!_sync)
            GlassBlurChanged?.Invoke(v);
    }

    public void SetCdnStatus(bool active, string detail)
    {
        CdnStatus.Text = Lang.Format("Common_StatusFmt",
            Lang.Get(active ? "Cdn_On" : "Cdn_Off"))
            + (detail.Length > 0 ? "  •  " + detail : "");
        CdnStatus.Foreground = active
            ? (Brush)FindResource("GoodBrush")
            : (Brush)FindResource("FgDimBrush");
    }

    /// <summary>Текущий фон в превью: миниатюра (null = нет файла) + имя + блюр.</summary>
    public void SetWallpaperInfo(string fileName, ImageSource? thumb, int blur)
    {
        _sync = true;
        try
        {
            WallpaperImage.Source = thumb;
            WallpaperFallbackBg.Visibility = thumb == null && fileName.Length > 0
                ? Visibility.Visible : Visibility.Collapsed;
            WallpaperPathLabel.Text = fileName.Length > 0 ? fileName : Lang.Get("Set_WallEmpty");
            WallpaperBlurBar.Value = Math.Clamp(blur, 0, 100);
            WallpaperBlurValue.Text = ((int)WallpaperBlurBar.Value) + "%";
        }
        finally { _sync = false; }
    }

    /// <summary>Затемнение фона 0..100 (без срабатывания события).</summary>
    public void SetWallpaperDim(int dim)
    {
        _sync = true;
        try
        {
            WallpaperDimBar.Value = Math.Clamp(dim, 0, 100);
            WallpaperDimValue.Text = ((int)WallpaperDimBar.Value) + "%";
        }
        finally { _sync = false; }
    }

    /// <summary>Состояние стекла: тумблер + слайдеры (без срабатывания событий).</summary>
    public void SetGlassInfo(bool enabled, int opacity, int blur, int dim, bool pure)
    {
        _sync = true;
        try
        {
            GlassFeature.IsOn = enabled;
            GlassOpacityBar.Value = Math.Clamp(opacity, 0, 100);
            GlassOpacityValue.Text = ((int)GlassOpacityBar.Value) + "%";
            GlassBlurBar.Value = Math.Clamp(blur, 0, 100);
            GlassBlurValue.Text = ((int)GlassBlurBar.Value) + "%";
            GlassDimBar.Value = Math.Clamp(dim, 0, 100);
            GlassDimValue.Text = ((int)GlassDimBar.Value) + "%";
            SetGlassPure(pure);
        }
        finally { _sync = false; }
    }
}
