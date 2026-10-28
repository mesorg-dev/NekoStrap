using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NekoStrap.Wpf.Controls;

/// <summary>
/// Компактная «фича»: строка с заголовком, тумблером и шестерёнкой.
/// Включение тумблера раскрывает опции (Content); выключение — сворачивает.
/// Шестерёнка/клик по шапке сворачивает опции, не выключая функцию.
/// </summary>
public partial class FeatureExpander : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(FeatureExpander),
        new PropertyMetadata("", (_, _) => { }));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(FeatureExpander),
        new PropertyMetadata("", OnHintChanged));

    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool), typeof(FeatureExpander),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnIsOnChanged));

    public event EventHandler? IsOnChanged;

    public FeatureExpander()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _sync = true;
            Toggle.IsChecked = IsOn;
            _sync = false;
            UpdateExpanded();
        };
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

    /// <summary>Внутренний тумблер — для доступа по имени из страниц (см. SettingsPage).</summary>
    public CheckBox ToggleCheck => Toggle;

    /// <summary>Опции свёрнуты (при включённой функции).</summary>
    public bool IsExpanded { get; private set; } = true;

    private bool _sync;
    private static bool _updating;

    private static void OnHintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FeatureExpander fe) return;
        bool empty = string.IsNullOrEmpty(e.NewValue as string);
        fe.HintText.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FeatureExpander fe) return;
        // Если изменение пришло от тумблера — не трогаем Toggle снова
        if (!_updating)
        {
            fe._sync = true;
            fe.Toggle.IsChecked = e.NewValue is true;
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
            IsOn = Toggle.IsChecked == true;
        }
        finally { _updating = false; }
    }

    private void Head_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Клик по тумблеру/шестерёнке обрабатывается отдельно
        if (e.OriginalSource is DependencyObject src &&
            (IsChildOf(src, Toggle) || IsChildOf(src, GearText)))
            return;
        if (!IsOn) return; // сворачивать можно только включённую фичу
        IsExpanded = !IsExpanded;
        UpdateExpanded();
    }

    private static bool IsChildOf(DependencyObject? node, DependencyObject expected)
    {
        while (node != null)
        {
            if (node == expected) return true;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    private void UpdateExpanded()
    {
        bool show = IsOn && IsExpanded;
        OptsHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        GearText.Opacity = IsOn ? (IsExpanded ? 1.0 : 0.7) : 0.35;
    }
}
