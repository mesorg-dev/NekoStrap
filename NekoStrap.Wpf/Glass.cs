using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace NekoStrap.Wpf;

/// <summary>
/// Настоящее жидкое стекло: панель показывает блюр фона ровно под собой.
/// Механика: за контентом панели лежат два прямоугольника — нижний семплирует
/// картинку обоев через VisualBrush (ровно свой прямоугольник в координатах
/// картинки, 1:1) + BlurEffect, верхний кладёт тинт. Контент поверх — чёткий.
/// Стекло выкл / нет обоев — прямоугольники прячутся, панель сплошная.
/// Позиция отслеживается через LayoutUpdated + ScrollChanged, поэтому блюр
/// стоит как вкопанный при скролле и ресайзе.
/// </summary>
internal static class Glass
{
    public enum TintKind
    {
        Card,
        Chrome
    }

    public static readonly DependencyProperty IsGlassProperty = DependencyProperty.RegisterAttached(
        "IsGlass", typeof(bool), typeof(Glass),
        new PropertyMetadata(false, OnIsGlassChanged));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(double), typeof(Glass),
        new PropertyMetadata(6.0, OnGlassVisualChanged));

    public static readonly DependencyProperty TintProperty = DependencyProperty.RegisterAttached(
        "Tint", typeof(TintKind), typeof(Glass),
        new PropertyMetadata(TintKind.Card, OnGlassVisualChanged));

    public static bool GetIsGlass(DependencyObject d) => (bool)d.GetValue(IsGlassProperty);
    public static void SetIsGlass(DependencyObject d, bool v) => d.SetValue(IsGlassProperty, v);
    public static double GetCornerRadius(DependencyObject d) => (double)d.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject d, double v) => d.SetValue(CornerRadiusProperty, v);
    public static TintKind GetTint(DependencyObject d) => (TintKind)d.GetValue(TintProperty);
    public static void SetTint(DependencyObject d, TintKind v) => d.SetValue(TintProperty, v);

    /// <summary>Картинка обоев (источник семплов). Выставляет MainWindow.</summary>
    internal static Image? Wallpaper { get; set; }

    /// <summary>Временная трассировка для дымового теста (см. Smoke).</summary>
    internal static bool Trace { get; set; }

    /// <summary>Стекло реально рисуется (обои + вкл + непрозрачность &lt; 100%). Выставляет UiTheme.</summary>
    internal static bool Active { get; set; }

    /// <summary>Радиус блюра подложки в px. Выставляет UiTheme.</summary>
    internal static double BlurRadius { get; set; }

    internal static Brush TintCard { get; set; } = Brushes.Transparent;
    internal static Brush TintChrome { get; set; } = Brushes.Transparent;

    /// <summary>UiTheme дёргает после смены настроек — все хосты обновляются.</summary>
    internal static void NotifySettingsChanged()
    {
        try
        {
            Host[] all;
            lock (Host.All) all = Host.All.ToArray();
            foreach (var h in all)
                h.Update();
        }
        catch { /* ignore */ }
    }

    private static void OnIsGlassChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            if (d is Border border)
                Host.For(border).Update();
        }
        catch { /* ignore */ }
    }

    private static void OnGlassVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            var host = Host.Find(d as Border);
            host?.Update();
        }
        catch { /* ignore */ }
    }

    private sealed class Host
    {
        internal static readonly List<Host> All = new();

        private readonly Border _border;
        private readonly Rectangle _blurRect;
        private readonly Rectangle _tintRect;
        private readonly VisualBrush _brush;
        private readonly BlurEffect _blur;
        private Grid? _grid;
        private ScrollViewer? _scroller;

        // Change-awareness: не дёргаем визуал без изменений, иначе каждый
        // тик/скролл давал бы полную перерисовку блюра.
        private Rect _lastSample = Rect.Empty;
        private double _lastR = -1;
        private Brush? _lastTint;
        private bool _lastVis;

        private Host(Border border)
        {
            _border = border;
            _brush = new VisualBrush
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill,
                TileMode = TileMode.None
            };
            _blur = new BlurEffect { Radius = BlurRadius, KernelType = KernelType.Gaussian };
            _blurRect = new Rectangle
            {
                Fill = _brush,
                Effect = _blur,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Collapsed
            };
            _tintRect = new Rectangle
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Collapsed
            };
            // Хук на самом Border: контент-панель может появиться позже
            // применения стиля — тогда EnsurePanel подхватит её здесь.
            _border.LayoutUpdated += (_, _) => OnLayout();
        }

        internal static Host For(Border border)
        {
            lock (All)
            {
                foreach (var h in All)
                    if (ReferenceEquals(h._border, border))
                        return h;
                var host = new Host(border);
                All.Add(host);
                return host;
            }
        }

        internal static Host? Find(Border? border)
        {
            if (border == null) return null;
            lock (All)
            {
                foreach (var h in All)
                    if (ReferenceEquals(h._border, border))
                        return h;
            }
            return null;
        }

        /// <summary>
        /// Перестройка внутренностей Border: контент заворачиваем в Grid,
        /// под него кладём блюр+тинт. Grid-ячейка одна — оверлеи тянутся на
        /// весь интерьер и НЕ участвуют в раскладке (в StackPanel/DockPanel
        /// Stretch-дети схлопывались в ноль — панели оставались сплошными).
        /// Паддинг переносим на контент, чтобы стекло покрывало и его зону.
        /// </summary>
        private bool EnsurePanel()
        {
            if (_grid != null) return true;
            if (_border.Child is not UIElement content)
            {
                if (Trace) System.Console.WriteLine($"GLASS ensure: no child");
                return false;
            }
            if (Trace) System.Console.WriteLine($"GLASS ensure: wrapping {content.GetType().Name}, border={_border.ActualWidth:F0}x{_border.ActualHeight:F0}");
            if (ReferenceEquals(content, _grid)) return false;
            var pad = _border.Padding;
            _border.Child = null;
            _border.Padding = new Thickness(0);
            if (content is FrameworkElement fe)
            {
                var m = fe.Margin;
                fe.Margin = new Thickness(
                    m.Left + pad.Left, m.Top + pad.Top,
                    m.Right + pad.Right, m.Bottom + pad.Bottom);
            }
            _grid = new Grid();
            _grid.Children.Add(_blurRect);
            _grid.Children.Add(_tintRect);
            _grid.Children.Add(content);
            _border.Child = _grid;
            return true;
        }

        private void OnLayout()
        {
            try
            {
                if (_scroller == null)
                {
                    // Скролл едет трансформом без layout — подписываемся отдельно.
                    DependencyObject? cur = _border;
                    while (cur != null && cur is not ScrollViewer)
                        cur = VisualTreeHelper.GetParent(cur);
                    if (cur is ScrollViewer sv)
                    {
                        _scroller = sv;
                        _scroller.ScrollChanged += (_, _) => Update();
                    }
                }
                Update();
            }
            catch { /* ignore */ }
        }

        internal void Update()
        {
            try
            {
                if (!GetIsGlass(_border) || !EnsurePanel() || _grid == null)
                {
                    SetVisible(false);
                    return;
                }
                var img = Wallpaper;
                // Размер берём с Grid (он всегда Visible и всегда измеряется),
                // а не с самих rect'ов: схлопнутый rect имеет размер 0 навсегда
                // и прятался бы навечно (самоблокировка — панели сплошные).
                bool on = Active && img != null && img.Source != null
                    && _grid.ActualWidth > 0 && _grid.ActualHeight > 0;
                if (Trace) System.Console.WriteLine(
                    $"GLASS update: active={Active} src={(img?.Source != null)} border={_border.ActualWidth:F1}x{_border.ActualHeight:F1} loaded={_border.IsLoaded} grid={_grid.ActualWidth:F1}x{_grid.ActualHeight:F1} kids={_grid.Children.Count} on={on}");
                if (!on)
                {
                    SetVisible(false);
                    return;
                }

                // Прямоугольник панели в координатах картинки (DIP) — семпл 1:1.
                Rect sample;
                try
                {
                    var t = _grid.TransformToVisual(img);
                    sample = t.TransformBounds(new Rect(0, 0,
                        _grid.ActualWidth, _grid.ActualHeight));
                }
                catch
                {
                    SetVisible(false);
                    return;
                }
                if (sample.IsEmpty || sample.Width <= 0 || sample.Height <= 0)
                {
                    SetVisible(false);
                    return;
                }

                double r = Math.Max(0, GetCornerRadius(_border));
                Brush tint = GetTint(_border) == TintKind.Chrome ? TintChrome : TintCard;
                // Округляем семпл: субпиксельное дрожание при скролле иначе
                // перерисовывало бы блюр на каждый тик.
                var key = new Rect(
                    Math.Round(sample.X * 2) / 2, Math.Round(sample.Y * 2) / 2,
                    Math.Round(sample.Width * 2) / 2, Math.Round(sample.Height * 2) / 2);
                if (_lastVis && key == _lastSample && r == _lastR
                    && ReferenceEquals(tint, _lastTint) && BlurRadius == _lastBlur)
                    return; // ничего не изменилось — визуал не трогаем

                _brush.Visual = img;
                _brush.Viewbox = key;
                _blur.Radius = BlurRadius;
                _lastBlur = BlurRadius;

                var clip = new RectangleGeometry(
                    new Rect(0, 0, _grid.ActualWidth, _grid.ActualHeight), r, r);
                clip.Freeze();
                _blurRect.Clip = clip;
                _tintRect.Clip = clip;
                _tintRect.Fill = tint;

                _lastSample = key;
                _lastR = r;
                _lastTint = tint;
                SetVisible(true);
            }
            catch { /* ignore */ }
        }

        private double _lastBlur = -1;

        private void SetVisible(bool vis)
        {
            if (_lastVis == vis && _blurRect.Visibility == (vis ? Visibility.Visible : Visibility.Collapsed))
                return;
            _lastVis = vis;
            // Сброс кэша: следующее появление пересчитает всё.
            _lastSample = Rect.Empty;
            _blurRect.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
            _tintRect.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
