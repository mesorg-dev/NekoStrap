using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NekoStrap.Display;
using NekoStrap.Roblox;

namespace NekoStrap.Wpf;

/// <summary>
/// Окно настройки цветокора: живые ползунки поверх игры (TopMost),
/// пресеты и тумблеры. Всё применяется мгновенно — удобно сравнивать.
/// </summary>
public partial class ColorTuneWindow : Window
{
    private readonly LauncherConfig _config;
    private readonly Action _onChanged;
    private readonly Action _onClosed;
    private bool _sync;

    internal ColorTuneWindow(LauncherConfig config, Action onChanged, Action onClosed)
    {
        _config = config;
        _onChanged = onChanged;
        _onClosed = onClosed;

        InitializeComponent();

        _sync = true;
        try
        {
            SatBar.Value = Math.Clamp(Math.Round(_config.ColorFxSat * 100), 0, 200);
            BriBar.Value = Math.Clamp(Math.Round(_config.ColorFxBri * 100), -50, 50);
            ConBar.Value = Math.Clamp(Math.Round(_config.ColorFxCon * 100), 0, 200);
            TempBar.Value = Math.Clamp(Math.Round(_config.ColorFxTemp * 100), -100, 100);
            RefreshLabels();
            OnToggle.IsChecked = _config.ColorFxEnabled;
            AutoToggle.IsChecked = _config.ColorFxAuto;
        }
        finally { _sync = false; }

        OnToggle.Checked += (_, _) => { _config.ColorFxEnabled = true; Changed(); };
        OnToggle.Unchecked += (_, _) => { _config.ColorFxEnabled = false; Changed(); };
        AutoToggle.Checked += (_, _) => { _config.ColorFxAuto = true; Changed(); };
        AutoToggle.Unchecked += (_, _) => { _config.ColorFxAuto = false; Changed(); };

        Closed += (_, _) =>
        {
            try { _onClosed(); } catch { /* ignore */ }
        };
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SatBar_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SatValue.Text = ((int)SatBar.Value / 100f).ToString("F2");
        if (_sync) return;
        _config.ColorFxSat = (int)SatBar.Value / 100f;
        Changed();
    }

    private void BriBar_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)BriBar.Value;
        BriValue.Text = (v >= 0 ? "+" : "") + (v / 100f).ToString("F2");
        if (_sync) return;
        _config.ColorFxBri = v / 100f;
        Changed();
    }

    private void ConBar_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ConValue.Text = ((int)ConBar.Value / 100f).ToString("F2");
        if (_sync) return;
        _config.ColorFxCon = (int)ConBar.Value / 100f;
        Changed();
    }

    private void TempBar_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)TempBar.Value;
        TempValue.Text = (v >= 0 ? "+" : "") + (v / 100f).ToString("F2");
        if (_sync) return;
        _config.ColorFxTemp = v / 100f;
        Changed();
    }

    private void RefreshLabels()
    {
        SatValue.Text = ((int)SatBar.Value / 100f).ToString("F2");
        int b = (int)BriBar.Value;
        BriValue.Text = (b >= 0 ? "+" : "") + (b / 100f).ToString("F2");
        ConValue.Text = ((int)ConBar.Value / 100f).ToString("F2");
        int t = (int)TempBar.Value;
        TempValue.Text = (t >= 0 ? "+" : "") + (t / 100f).ToString("F2");
    }

    private void ApplyPreset(ColorSettings s)
    {
        _config.ColorFxSat = s.Saturation;
        _config.ColorFxBri = s.Brightness;
        _config.ColorFxCon = s.Contrast;
        _config.ColorFxTemp = s.Temperature;
        _config.ColorFxEnabled = true;
        _sync = true;
        try
        {
            SatBar.Value = Math.Clamp(Math.Round(s.Saturation * 100), 0, 200);
            BriBar.Value = Math.Clamp(Math.Round(s.Brightness * 100), -50, 50);
            ConBar.Value = Math.Clamp(Math.Round(s.Contrast * 100), 0, 200);
            TempBar.Value = Math.Clamp(Math.Round(s.Temperature * 100), -100, 100);
            RefreshLabels();
            OnToggle.IsChecked = true;
        }
        finally { _sync = false; }
        Changed();
    }

    private void PresetStandard_Click(object sender, RoutedEventArgs e) => ApplyPreset(ColorSettings.Default);
    private void PresetCinema_Click(object sender, RoutedEventArgs e) => ApplyPreset(ColorSettings.Cinema);
    private void PresetVivid_Click(object sender, RoutedEventArgs e) => ApplyPreset(ColorSettings.Vivid);
    private void PresetMono_Click(object sender, RoutedEventArgs e) => ApplyPreset(ColorSettings.Mono);

    private void Changed()
    {
        try { _onChanged(); } catch { /* ignore */ }
    }
}
