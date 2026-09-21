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

    public event Action<string>? SoundPickClicked;
    public event Action<string>? SoundResetClicked;
    public event Action<string, int>? SoundVolumeChanged;

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
    }

    /// <summary>
    /// Ряды звуков строим кодом по списку движка: подпись + громкость +
    /// файл + кнопки. Новый звук в движке = новый ряд без правок XAML.
    /// </summary>
    private void BuildSoundRows()
    {
        var uiFont = (System.Windows.Media.FontFamily)FindResource("UiFont");
        var fg = (System.Windows.Media.Brush)FindResource("FgBrush");
        foreach (var (key, title) in ClickSound.Sounds)
        {
            var wrap = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            var head = new DockPanel { LastChildFill = true };
            var name = new TextBlock
            {
                Text = title,
                FontFamily = uiFont,
                FontSize = 12.5,
                Foreground = fg,
                VerticalAlignment = VerticalAlignment.Center
            };
            var val = new TextBlock
            {
                Text = "100%",
                FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = fg,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
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
                Text = "встроенный",
                FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"),
                FontSize = 11,
                Foreground = (System.Windows.Media.Brush)FindResource("FgDimmerBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            var pick = new Button
            {
                Content = "Выбрать",
                Style = (Style)FindResource("OutlineButton"),
                Width = 110,
                Height = 30,
                Tag = key
            };
            pick.Click += (_, _) => SoundPickClicked?.Invoke(key);
            var reset = new Button
            {
                Content = "Сброс",
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
            label.Text = display.Length > 0 ? display : "встроенный";
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
            Title = "Где хранить Roblox и моды NekoStrap"
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

    private void GlassBlurBar_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)GlassBlurBar.Value;
        GlassBlurValue.Text = v + "%";
        if (!_sync)
            GlassBlurChanged?.Invoke(v);
    }

    public void SetCdnStatus(bool active, string detail)
    {
        CdnStatus.Text = "Статус: " + (active ? "включён" : "выключен")
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
            WallpaperPathLabel.Text = fileName.Length > 0 ? fileName : "Не выбран — сплошной фон";
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
    public void SetGlassInfo(bool enabled, int opacity, int blur, int dim)
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
        }
        finally { _sync = false; }
    }
}
