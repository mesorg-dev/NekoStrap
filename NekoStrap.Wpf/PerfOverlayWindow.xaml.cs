using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace NekoStrap.Wpf;

/// <summary>
/// Летающий оверлей производительности поверх игры: пинг, CPU, RAM, GPU,
/// сервер, время сессии. TopMost без таскбара и без активации — фокус
/// остаётся у игры; шапка перетаскивается зажатием; каждая строка включается
/// отдельно в настройках. Liquid glass — акрил Win10/11 через
/// SetWindowCompositionAttribute c гарантированным fallback'ом на
/// полупрозрачный тинт (любая ошибка акрила окно не роняет).
/// </summary>
public partial class PerfOverlayWindow : Window
{
    private sealed class Row
    {
        public Border Box = null!;
        public TextBlock Label = null!;
        public TextBlock Value = null!;
        public string LabelKey = "";
        public string BrushKey = "FgBrush";
    }

    private readonly Dictionary<string, Row> _rows = new();
    private bool _glass = true;
    private int _opacity = 90;
    private bool _showPing = true;
    private bool _showCpu = true;
    private bool _showRam = true;
    private bool _showGpu = true;
    private bool _showServer = true;
    private bool _showTime = true;
    private bool _gpuAvailable = true;

    internal PerfOverlayWindow()
    {
        InitializeComponent();
        BuildRow("ping", "Perf_Ping");
        BuildRow("cpu", "Perf_Cpu");
        BuildRow("ram", "Perf_Ram");
        BuildRow("gpu", "Perf_Gpu");
        BuildRow("server", "Perf_Server");
        BuildRow("time", "Perf_Time");
    }

    private void BuildRow(string key, string labelKey)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock { Text = Lang.Get(labelKey), FontSize = 11 };
        label.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        label.SetResourceReference(TextBlock.ForegroundProperty, "FgDimBrush");

        var value = new TextBlock
        {
            Text = "—",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 0, 0),
        };
        value.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        value.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");

        Grid.SetColumn(label, 0);
        Grid.SetColumn(value, 1);
        grid.Children.Add(label);
        grid.Children.Add(value);

        var box = new Border { Child = grid };
        Rows.Children.Add(box);
        _rows[key] = new Row
        {
            Box = box,
            Label = label,
            Value = value,
            LabelKey = labelKey,
        };
    }

    // ================= Значения =================

    /// <summary>
    /// Обновить все значения разом. -1 = нет данных (показываем «—»);
    /// pingFallback — подсказка с главной (ICMP), если клиентский замер нет.
    /// </summary>
    public void SetValues(int pingMs, string pingFallback, double cpuPct,
        double ramMb, double gpuPct, string server, string time)
    {
        if (pingMs >= 0)
        {
            SetCell("ping", pingMs + " " + Lang.Get("Perf_Ms"),
                Tint(pingMs, 80, 160, lowerIsBetter: true));
        }
        else
        {
            bool known = pingFallback.Length > 0 && pingFallback != "—";
            SetCell("ping", known ? pingFallback : "—", known ? "FgBrush" : "FgDimmerBrush");
        }

        SetCell("cpu", cpuPct < 0 ? "—" : Math.Round(cpuPct) + "%",
            cpuPct < 0 ? "FgDimmerBrush" : Tint(cpuPct, 70, 90, lowerIsBetter: false));

        SetCell("ram", ramMb > 0 ? FormatRam(ramMb) : "—",
            ramMb >= 6144 ? "WarnBrush" : "FgBrush");

        _gpuAvailable = gpuPct >= 0;
        SetCell("gpu", _gpuAvailable ? Math.Round(gpuPct) + "%" : "—",
            _gpuAvailable ? Tint(gpuPct, 70, 90, lowerIsBetter: false) : "FgDimmerBrush");

        SetCell("server", server.Length > 0 ? server : "—",
            server.Length > 0 ? "FgBrush" : "FgDimmerBrush");
        SetCell("time", time.Length > 0 ? time : "—",
            time.Length > 0 ? "FgBrush" : "FgDimmerBrush");

        RefreshVisibility();
    }

    /// <summary>Цвет по порогу: хорошо/внимание/плохо.</summary>
    private static string Tint(double v, double good, double bad, bool lowerIsBetter)
    {
        bool isGood = lowerIsBetter ? v <= good : v < good;
        bool isWarn = lowerIsBetter ? v <= bad : v < bad;
        return isGood ? "GoodBrush" : isWarn ? "WarnBrush" : "BadBrush";
    }

    private static string FormatRam(double mb) =>
        mb >= 1024
            ? (mb / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
            : Math.Round(mb) + " MB";

    private void SetCell(string key, string text, string brushKey)
    {
        if (!_rows.TryGetValue(key, out var row)) return;
        if (row.Value.Text != text) row.Value.Text = text;
        // Переставляем кисть только при смене — иначе каждый тик дёргает
        // перерисовку даже при неизменном цвете.
        if (row.BrushKey != brushKey)
        {
            row.BrushKey = brushKey;
            row.Value.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        }
    }

    // ================= Конфиг =================

    /// <summary>Какие строки показывать (сразу, без пересоздания окна).</summary>
    public void ApplyRows(bool ping, bool cpu, bool ram, bool gpu, bool server, bool time)
    {
        _showPing = ping;
        _showCpu = cpu;
        _showRam = ram;
        _showGpu = gpu;
        _showServer = server;
        _showTime = time;
        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        Vis("ping", _showPing);
        Vis("cpu", _showCpu);
        Vis("ram", _showRam);
        // GPU-счётчик недоступен — строка не мусорит «—», а схлопывается.
        Vis("gpu", _showGpu && _gpuAvailable);
        Vis("server", _showServer);
        Vis("time", _showTime);
    }

    private void Vis(string key, bool on)
    {
        if (_rows.TryGetValue(key, out var row))
            row.Box.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Оформление: тинт по непрозрачности + (опционально) акрил.</summary>
    public void ApplyStyle(bool glass, int opacity)
    {
        _glass = glass;
        _opacity = Math.Clamp(opacity, 20, 100);
        byte alpha = (byte)(_opacity * 255 / 100);
        // Стекло: половинный тинт — акрил и так затемняет/размывает подложку.
        if (_glass) alpha = (byte)(alpha / 2);
        Root.Background = new SolidColorBrush(Color.FromArgb(alpha, 10, 10, 11));
        Header.Background = _glass
            ? Brushes.Transparent
            : TryFindResource("ChromeBrush") as Brush ?? Brushes.Transparent;
        ApplyAccent();
    }

    /// <summary>Смена языка: переписать подписи (значения обновит тик).</summary>
    public void RefreshLabels()
    {
        TitleLabel.Text = Lang.Get("Perf_Header");
        foreach (var row in _rows.Values)
            row.Label.Text = Lang.Get(row.LabelKey);
    }

    // ================= Drag / закрытие =================

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.OriginalSource is Button) return; // крестик не должен тащить окно
        try
        {
            if (WindowState == WindowState.Normal)
                DragMove();
        }
        catch { /* гонка отпускания кнопки */ }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    // ================= Liquid glass (акрил) =================

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyAccent(); // handle появился — акрил применим только теперь
    }

    /// <summary>
    /// Акрил Win10/11 под окном. Любая ошибка (старая система, запрет —
    /// что угодно) молча оставляет baseline-тинт из ApplyStyle.
    /// </summary>
    private void ApplyAccent()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return; // до Show хэндла ещё нет
            var accent = new AccentPolicy
            {
                AccentState = _glass ? AccentEnableAcrylicBlurBehind : AccentDisabled,
                AccentFlags = 2,
                GradientColor = _glass ? 0x660E0E10u : 0u, // ABGR: тинт акрила
            };
            int size = Marshal.SizeOf<AccentPolicy>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WcaAccentedPolicy,
                    Data = ptr,
                    SizeOfData = (nuint)size,
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch { /* без акрила — просто тинт */ }
    }

    private const int AccentDisabled = 0;
    private const int AccentEnableAcrylicBlurBehind = 3;
    private const int WcaAccentedPolicy = 19;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public nuint SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        IntPtr hwnd, ref WindowCompositionAttributeData data);
}
