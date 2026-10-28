using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NekoStrap.Wpf.Controls;

/// <summary>
/// Компактная «фича»: строка с заголовком, тумблером и шестерёнкой.
/// Включение тумблера раскрывает опции (с анимацией); выключение сворачивает.
/// Шестерёнка/клик по шапке сворачивает, не выключая. Слева — accent-полоска.
/// Decorator: x:Name внутри Content остаётся в namescope страницы.
/// </summary>
public class FeatureExpander : Decorator
{
    private static readonly Duration AnimDur = TimeSpan.FromMilliseconds(180);

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(FeatureExpander),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(FeatureExpander),
        new FrameworkPropertyMetadata("", OnHintChanged));

    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool), typeof(FeatureExpander),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnIsOnChanged));

    public event EventHandler? IsOnChanged;

    private readonly CheckBox _toggle;
    private readonly TextBlock _titleText;
    private readonly TextBlock _hintText;
    private readonly Border _gearBox;
    private readonly Path _gearIcon;
    private readonly Border _optsHost;
    private readonly Border _accent;
    private readonly Border _chrome;
    private readonly Grid _root;
    private bool _built;
    private bool _sync;
    private bool _animating;
    private static bool _updating;

    public FeatureExpander()
    {
        _titleText = new TextBlock
        {
            FontFamily = TryFindResource("UiFont") as FontFamily ?? new FontFamily("Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = TryFindResource("FgBrush") as Brush ?? Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _titleText.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding(nameof(Title)) { Source = this });

        _hintText = new TextBlock
        {
            FontFamily = TryFindResource("MonoFont") as FontFamily ?? new FontFamily("Consolas"),
            FontSize = 9.5,
            Foreground = TryFindResource("FgDimBrush") as Brush ?? Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        _hintText.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding(nameof(Hint)) { Source = this });
        UpdateHintVisibility(Hint);

        var gearGeom = TryFindResource("IconSettings") as Geometry
                       ?? Geometry.Parse("M9,2 A7,7 0 1,0 9,16 A7,7 0 1,0 9,2");
        _gearIcon = new Path
        {
            Data = gearGeom,
            Stroke = TryFindResource("FgDimmerBrush") as Brush ?? Brushes.Gray,
            StrokeThickness = 1.3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _gearBox = new Border
        {
            Width = 22,
            Height = 22,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Child = _gearIcon,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };

        _toggle = new CheckBox
        {
            Style = TryFindResource("Switch") as Style,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _toggle.Checked += Toggle_Changed;
        _toggle.Unchecked += Toggle_Changed;

        var titles = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        titles.Children.Add(_titleText);
        titles.Children.Add(_hintText);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        right.Children.Add(_gearBox);
        right.Children.Add(_toggle);

        var head = new Grid { Margin = new Thickness(0, 6, 0, 6), Cursor = Cursors.Hand };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(titles, 0);
        Grid.SetColumn(right, 1);
        head.Children.Add(titles);
        head.Children.Add(right);
        head.MouseLeftButtonUp += Head_MouseLeftButtonUp;
        head.MouseEnter += (_, _) => _chrome.Background = TryFindResource("BgBrush") as Brush ?? Brushes.Black;
        head.MouseLeave += (_, _) => SyncChromeBg();

        _optsHost = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = TryFindResource("LineSoftBrush") as Brush ?? Brushes.Gray,
            Background = Brushes.Transparent,
            Margin = new Thickness(-10, 0, -10, 0),
            Padding = new Thickness(10, 5, 10, 8),
            Visibility = Visibility.Collapsed,
            MaxHeight = 0,
        };

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(_optsHost);

        _accent = new Border
        {
            Width = 0,
            Background = TryFindResource("FgBrush") as Brush ?? Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        _root = new Grid();
        _root.Children.Add(stack);
        _root.Children.Add(_accent); // поверх stack, слева

        _chrome = new Border
        {
            BorderBrush = TryFindResource("LineSoftBrush") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0), // как в веб-моно: острые углы
            Background = Brushes.Black,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 0, 6),
            Child = _root,
        };

        // Перенос content — не в Measure (иначе re-layout), а на Background.
        Loaded += (_, _) =>
        {
            if (_built) return;
            Dispatcher.BeginInvoke(new Action(EnsureBuilt), DispatcherPriority.Background);
        };
    }

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;
        var options = Child;
        Child = null;
        _optsHost.Child = options;
        Child = _chrome;
        _sync = true;
        _toggle.IsChecked = IsOn;
        _sync = false;
        UpdateExpanded(animate: false);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    /// <summary>Внутренний тумблер — для доступа со страниц (SettingsPage и т.д.).</summary>
    public CheckBox ToggleCheck => _toggle;

    /// <summary>Опции свёрнуты (при включённой функции).</summary>
    public bool IsExpanded { get; private set; } = true;

    private static void OnHintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FeatureExpander fe) return;
        fe.UpdateHintVisibility(e.NewValue as string);
    }

    private void UpdateHintVisibility(string? hint)
    {
        bool empty = string.IsNullOrEmpty(hint);
        _hintText.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        _hintText.Text = empty ? "" : hint.ToUpperInvariant();
    }

    private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FeatureExpander fe) return;
        if (!_updating)
        {
            fe._sync = true;
            fe._toggle.IsChecked = e.NewValue is true;
            fe._sync = false;
        }
        fe.IsExpanded = e.NewValue is true;
        fe.UpdateExpanded(animate: fe._built);
        fe.IsOnChanged?.Invoke(fe, EventArgs.Empty);
    }

    private void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_sync) return;
        _updating = true;
        try
        {
            IsOn = _toggle.IsChecked == true;
        }
        finally { _updating = false; }
    }

    private void Head_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src &&
            (IsChildOf(src, _toggle) || IsChildOf(src, _gearBox)))
            return;
        if (!IsOn) return;
        IsExpanded = !IsExpanded;
        UpdateExpanded(animate: true);
    }

    private static bool IsChildOf(DependencyObject? node, DependencyObject expected)
    {
        while (node != null)
        {
            if (node == expected) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    private void SyncChromeBg()
    {
        if (!_built) return;
        _chrome.Background = IsOn
            ? (TryFindResource("BgBrush") as Brush ?? Brushes.Black)
            : Brushes.Black;
    }

    private void UpdateExpanded(bool animate)
    {
        if (!_built) return;

        bool show = IsOn && IsExpanded;
        SyncChromeBg();

        // Accent-полоска слева: 2px когда раскрыто, 0 когда свёрнуто/выключено.
        AnimateWidth(_accent, show ? 2 : 0, animate);

        // Шестерёнка: ярче и повёрнута, когда раскрыто.
        _gearIcon.Stroke = show || IsOn
            ? (TryFindResource("FgBrush") as Brush ?? Brushes.White)
            : (TryFindResource("FgDimmerBrush") as Brush ?? Brushes.Gray);
        var rot = new RotateTransform(show ? 60 : 0, 8, 8);
        _gearIcon.RenderTransform = rot;
        _gearIcon.RenderTransformOrigin = new Point(0.5, 0.5);
        _gearBox.BorderBrush = IsOn
            ? (TryFindResource("LineBrush") as Brush ?? Brushes.Gray)
            : Brushes.Transparent;

        if (!animate || _animating)
        {
            if (show)
            {
                _optsHost.Visibility = Visibility.Visible;
                _optsHost.BeginAnimation(MaxHeightProperty, null);
                _optsHost.MaxHeight = double.PositiveInfinity;
            }
            else
            {
                _optsHost.BeginAnimation(MaxHeightProperty, null);
                _optsHost.MaxHeight = 0;
                _optsHost.Visibility = Visibility.Collapsed;
            }
            return;
        }

        if (show) ExpandAnimated();
        else CollapseAnimated();
    }

    private void ExpandAnimated()
    {
        _optsHost.BeginAnimation(MaxHeightProperty, null);
        _optsHost.Visibility = Visibility.Visible;
        // Сначала без MaxHeight — иначе DesiredSize обрезается до 0.
        _optsHost.MaxHeight = double.PositiveInfinity;
        _optsHost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double h = _optsHost.DesiredSize.Height;
        if (double.IsInfinity(h) || h <= 0)
        {
            _optsHost.MaxHeight = double.PositiveInfinity;
            return;
        }
        _optsHost.MaxHeight = 0;

        _animating = true;
        var anim = new DoubleAnimation(0, h, AnimDur)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        anim.Completed += (_, _) =>
        {
            _optsHost.BeginAnimation(MaxHeightProperty, null);
            _optsHost.MaxHeight = double.PositiveInfinity;
            _animating = false;
            _optsHost.InvalidateMeasure();
        };
        _optsHost.BeginAnimation(MaxHeightProperty, anim);
    }

    private void CollapseAnimated()
    {
        _optsHost.BeginAnimation(MaxHeightProperty, null);
        double from = _optsHost.ActualHeight;
        if (double.IsNaN(from) || from <= 0) from = _optsHost.DesiredSize.Height;
        if (double.IsNaN(from) || from <= 0)
        {
            _optsHost.MaxHeight = 0;
            _optsHost.Visibility = Visibility.Collapsed;
            return;
        }

        _animating = true;
        _optsHost.Visibility = Visibility.Visible;
        var anim = new DoubleAnimation(from, 0, AnimDur)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop,
        };
        anim.Completed += (_, _) =>
        {
            _optsHost.BeginAnimation(MaxHeightProperty, null);
            _optsHost.MaxHeight = 0;
            _optsHost.Visibility = Visibility.Collapsed;
            _animating = false;
        };
        _optsHost.BeginAnimation(MaxHeightProperty, anim);
    }

    private static void AnimateWidth(Border border, double to, bool animate)
    {
        if (!animate)
        {
            border.BeginAnimation(WidthProperty, null);
            border.Width = to;
            return;
        }
        double from = border.Width;
        if (Math.Abs(from - to) < 0.01)
        {
            border.Width = to;
            return;
        }
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        anim.Completed += (_, _) =>
        {
            border.BeginAnimation(WidthProperty, null);
            border.Width = to;
        };
        border.BeginAnimation(WidthProperty, anim);
    }
}
