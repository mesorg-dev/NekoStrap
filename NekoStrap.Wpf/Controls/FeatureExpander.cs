using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NekoStrap.Wpf.Controls;

/// <summary>
/// Компактная «фича»: строка с заголовком, тумблером и шестерёнкой.
/// Включение тумблера раскрывает опции; выключение — сворачивает.
/// Шестерёнка/клик по шапке сворачивает, не выключая.
/// Decorator (без UserControl.xaml и без дефолтного шаблона ContentControl) —
/// x:Name внутри Content остаётся в namescope страницы, WPF-компилятор не падает.
/// </summary>
public class FeatureExpander : Decorator
{
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
    private readonly TextBlock _gear;
    private readonly Border _optsHost;
    private readonly Border _chrome;
    private bool _built;
    private bool _sync;
    private static bool _updating;

    public FeatureExpander()
    {
        _titleText = new TextBlock
        {
            FontFamily = TryFindResource("UiFont") as FontFamily ?? new FontFamily("Segoe UI"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TryFindResource("FgBrush") as Brush ?? Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _titleText.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding(nameof(Title)) { Source = this });

        _hintText = new TextBlock
        {
            Style = TryFindResource("Mono") as Style,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        _hintText.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding(nameof(Hint)) { Source = this });
        UpdateHintVisibility(Hint);

        _gear = new TextBlock
        {
            Text = "⚙",
            FontSize = 12,
            Foreground = TryFindResource("FgDimmerBrush") as Brush ?? Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
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
        right.Children.Add(_gear);
        right.Children.Add(_toggle);

        var head = new Grid { Margin = new Thickness(0, 5, 0, 5), Cursor = Cursors.Hand };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(titles, 0);
        Grid.SetColumn(right, 1);
        head.Children.Add(titles);
        head.Children.Add(right);
        head.MouseLeftButtonUp += Head_MouseLeftButtonUp;

        _optsHost = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = TryFindResource("LineSoftBrush") as Brush ?? Brushes.Gray,
            Background = TryFindResource("LineSoftBrush") as Brush ?? Brushes.DimGray,
            Margin = new Thickness(-10, 0, -10, 0),
            Padding = new Thickness(10, 5, 10, 8),
            Visibility = Visibility.Collapsed,
        };

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(_optsHost);

        _chrome = new Border
        {
            BorderBrush = TryFindResource("LineSoftBrush") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = TryFindResource("CardBrush") as Brush ?? Brushes.Black,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 0, 6),
            Child = stack,
        };

        // Поменять Child во время Measure (Loaded) нельзя — переносим контент
        // страницы в chrome на Background, когда layout уже отпустил дерево.
        Loaded += (_, _) =>
        {
            if (_built) return;
            Dispatcher.BeginInvoke(new Action(EnsureBuilt), DispatcherPriority.Background);
        };
    }

    /// <summary>Переносим content страницы внутрь chrome и ставим его как Child декора.</summary>
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
        UpdateExpanded();
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

    /// <summary>Включена ли функция (двусторонняя привязка к тумблеру).</summary>
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
        fe.UpdateExpanded();
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
            (IsChildOf(src, _toggle) || IsChildOf(src, _gear)))
            return;
        if (!IsOn) return;
        IsExpanded = !IsExpanded;
        UpdateExpanded();
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

    private void UpdateExpanded()
    {
        if (!_built) return;
        bool show = IsOn && IsExpanded;
        _optsHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _gear.Opacity = IsOn ? (IsExpanded ? 1.0 : 0.7) : 0.35;
    }
}
