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

    private bool _sync;
    private readonly Dictionary<string, TextBlock> _soundFiles = new();
    private readonly Dictionary<string, TextBlock> _soundValues = new();
    private readonly Dictionary<string, Slider> _soundBars = new();

    public SettingsPage()
    {
        InitializeComponent();
        SoundsBox.Checked += (_, _) => ClickSound.Enabled = SoundsBox.IsChecked == true;
        SoundsBox.Unchecked += (_, _) => ClickSound.Enabled = SoundsBox.IsChecked == true;
        BuildSoundRows();
        BuildFontBoxes();
        BuildColorRows();
    }

    // ================= Внешний вид =================

    public static readonly string[] HeadingFonts = { "Unbounded", "Segoe UI", "Verdana", "Trebuchet MS" };
    public static readonly string[] BodyFonts = { "Inter", "Segoe UI", "Verdana" };
    public static readonly string[] MonoFonts = { "JetBrains Mono", "Consolas", "Courier New" };

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
    public CheckBox DiscordRpcCheck => DiscordRpcBox;
    public CheckBox SoundsCheck => SoundsBox;
    public CheckBox AutoUpdateCheck => AutoUpdateBox;
    public CheckBox FpsCheck => FpsBox2;
    public CheckBox MinimizeToTrayCheck => MinimizeToTrayBox;
    public CheckBox CloseToTrayCheck => CloseToTrayBox;
    public CheckBox NotificationsCheck => NotificationsBox;
    public CheckBox TrackPlaytimeCheck => TrackPlaytimeBox;
    public CheckBox RobloxNoTrayCheck => RobloxNoTrayBox;
    public CheckBox RobloxNoStartupCheck => RobloxNoStartupBox;
    public CheckBox GlassCheck => GlassCheckBox;

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

    public string DiscordAppIdText
    {
        get => DiscordAppBox.Text.Trim();
        set => DiscordAppBox.Text = value;
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
            GlassCheckBox.IsChecked = enabled;
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
