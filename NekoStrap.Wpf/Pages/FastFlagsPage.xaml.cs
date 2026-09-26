using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace NekoStrap.Wpf.Pages;

/// <summary>Строка таблицы флагов (редактируемая).</summary>
public sealed class FlagRow : INotifyPropertyChanged
{
    private string _name = "";
    private string _value = "";

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); } }
    }

    public string Value
    {
        get => _value;
        set { if (_value != value) { _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record FlagPreset(string Title, string TitleEn, string Flag, string Value);

/// <summary>
/// FastFlags: таблица + живой поиск, кликабельные пресеты, импорт/экспорт.
/// </summary>
public partial class FastFlagsPage : UserControl
{
    public static readonly FlagPreset[] BuiltinPresets =
    {
        new("FPS 60 (сток)", "FPS 60 (stock)", "DFIntTaskSchedulerTargetFps", "60"),
        new("FPS 144", "FPS 144", "DFIntTaskSchedulerTargetFps", "144"),
        new("FPS 240", "FPS 240", "DFIntTaskSchedulerTargetFps", "240"),
        new("Свет: Future", "Light: Future", "FFlagDebugForceFutureIsBrightPhase3", "true"),
        new("Свет: ShadowMap", "Light: ShadowMap", "FFlagDebugForceFutureIsBrightPhase2", "true"),
        new("Свет: Voxel (быстро)", "Light: Voxel (fast)", "DFFlagDebugRenderForceTechnologyVoxel", "true"),
        new("Без блюма", "No bloom", "FFlagRenderNoLowFrmBloom", "true"),
        new("Без пост-эффектов", "No post FX", "FFlagDisablePostFx", "true"),
        new("MSAA x4", "MSAA x4", "FIntDebugForceMSAASamples", "4"),
        new("MSAA x8", "MSAA x8", "FIntDebugForceMSAASamples", "8"),
        new("Детали далеко", "Far details", "DFIntCSGLevelOfDetailSwitchingDistance", "5000"),
        new("Без травы", "No grass", "FIntFRMMaxGrassDistance", "0"),
        new("Дальний зум", "Far zoom", "FIntCameraMaxZoomDistance", "1000"),
        new("Без DPI-скейла", "No DPI scale", "DFFlagDisableDPIScale", "true"),
        new("Рендер: Vulkan", "Render: Vulkan", "FFlagDebugGraphicsPreferVulkan", "true"),
        new("Пинг в лог", "Ping to log", "DFFlagDebugPrintDataPingBreakDown", "true"),
    };

    public event EventHandler? AddFlagClicked;
    public event EventHandler? SaveClicked;
    public event EventHandler? ImportClicked;
    public event EventHandler? ExportClicked;
    public event Action<string>? ProfileApplyClicked;
    public event EventHandler? ProfileSaveClicked;
    public event EventHandler? ProfileDeleteClicked;
    public event EventHandler? ProfileBindRequested;
    public event EventHandler? ProfileUnbindRequested;
    public event EventHandler? DeleteAllClicked;

    private readonly ObservableCollection<FlagRow> _rows = new();
    private readonly ICollectionView _view;
    private string _filter = "";
    private readonly List<Button> _presetButtons = new();

    // --- Drag & Drop ---------------------------------------------------
    private const string PresetDropFormat = "NekoStrap.FastFlags.Preset";
    private const string ProfileDropFormat = "NekoStrap.FastFlags.Profile";
    private const string RowDropFormat = "NekoStrap.FastFlags.Row";

    private bool _chipDragged;      // клик, случившийся после драга, гасим
    private Point? _chipDown;
    private FlagRow? _rowDragSource;
    private Point _rowDownPos;
    private bool _zoneHot;          // карточка таблицы подсвечена как зона сброса
    private readonly Popup _ghost;
    private readonly TextBlock _ghostText;

    public FastFlagsPage()
    {
        InitializeComponent();
        FlagsGrid.ItemsSource = _rows;
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = o => o is FlagRow r &&
            (_filter.Length == 0 || r.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        _rows.CollectionChanged += (_, _) => RefreshPresetStates();

        _ghostText = new TextBlock { Margin = new Thickness(0) };
        _ghostText.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        _ghostText.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        _ghostText.FontSize = 11;
        _ghostText.FontWeight = FontWeights.Bold;
        var ghostBorder = new Border
        {
            Child = _ghostText,
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
        };
        ghostBorder.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        ghostBorder.SetResourceReference(Border.BorderBrushProperty, "GoodBrush");
        _ghost = new Popup
        {
            Placement = PlacementMode.RelativePoint,
            PlacementTarget = this,
            AllowsTransparency = true,
            IsHitTestVisible = false,
            StaysOpen = true,
            Child = ghostBorder,
        };

        BuildPresetButtons();
    }

    /// <summary>Перестроить кнопки пресетов (смена языка). Состояния
    /// подсветки восстанавливает RefreshPresetStates следом.</summary>
    public void RefreshPresetButtons()
    {
        BuildPresetButtons();
        RefreshPresetStates();
    }

    private void BuildPresetButtons()
    {
        PresetsPanel.Children.Clear();
        _presetButtons.Clear();
        bool en = Lang.Current == "en";
        foreach (var p in BuiltinPresets)
        {
            var btn = new Button
            {
                Content = en ? p.TitleEn : p.Title,
                Style = (Style)FindResource("OutlineButton"),
                Width = 165,
                Height = 34,
                Margin = new Thickness(0, 0, 10, 10),
                Tag = p
            };
            btn.Click += (_, _) =>
            {
                if (_chipDragged) return;
                TogglePreset(p);
            };
            WireChip(btn);
            _presetButtons.Add(btn);
            PresetsPanel.Children.Add(btn);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filter = SearchBox.Text.Trim();
        _view.Refresh();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        AddFlagClicked?.Invoke(this, EventArgs.Empty);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveClicked?.Invoke(this, EventArgs.Empty);
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        ImportClicked?.Invoke(this, EventArgs.Empty);
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        ExportClicked?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedRows();
    }

    private void ProfileSaveButton_Click(object sender, RoutedEventArgs e)
    {
        ProfileSaveClicked?.Invoke(this, EventArgs.Empty);
    }

    private void ProfileDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        ProfileDeleteClicked?.Invoke(this, EventArgs.Empty);
    }

    private void ProfileBindButton_Click(object sender, RoutedEventArgs e) =>
        ProfileBindRequested?.Invoke(this, EventArgs.Empty);

    private void ProfileUnbindButton_Click(object sender, RoutedEventArgs e) =>
        ProfileUnbindRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Какой профиль флагов привязан к последней игре.</summary>
    public void SetBindStatus(string text) => ProfileBindStatus.Text = text;

    public string ProfileNameText
    {
        get => ProfileNameBox.Text.Trim();
        set => ProfileNameBox.Text = value;
    }

    public void ClearProfileName()
    {
        ProfileNameBox.Text = "";
    }

    /// <summary>Кнопки профилей (клик = применить).</summary>
    public void SetProfiles(IEnumerable<string> names)
    {
        ProfilesPanel.Children.Clear();
        var list = names.ToList();
        ProfilesEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var style = (Style)FindResource("OutlineButton");
        foreach (var name in list)
        {
            var btn = new Button
            {
                Content = name,
                Style = style,
                MinWidth = 120,
                Height = 34,
                Margin = new Thickness(0, 0, 10, 10),
                Tag = name
            };
            btn.Click += (_, _) =>
            {
                if (_chipDragged) return;
                ProfileApplyClicked?.Invoke(name);
            };
            WireChip(btn);
            ProfilesPanel.Children.Add(btn);
        }
    }

    private void DeleteAllButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteAllClicked?.Invoke(this, EventArgs.Empty);
    }

    private void FlagsGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Delete)
        {
            DeleteSelectedRows();
            e.Handled = true;
        }
    }

    private void FlagsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        // Коммит ещё не применён — обновляем состояния после него.
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            _view.Refresh();
            RefreshPresetStates();
        }));
    }

    /// <summary>Удаляет выделенные строки (кнопка и клавиша Delete).</summary>
    public void DeleteSelectedRows()
    {
        var doomed = FlagsGrid.SelectedItems.Cast<FlagRow>().ToList();
        if (doomed.Count == 0) return;
        foreach (var r in doomed)
            _rows.Remove(r);
    }

    public void AddNewFlag()
    {
        _rows.Add(new FlagRow { Name = "NewFlag", Value = "true" });
    }

    /// <summary>Удалить все флаги из таблицы (в файл попадёт по «Сохранить»).</summary>
    public void ClearFlags()
    {
        _rows.Clear();
    }

    private void TogglePreset(FlagPreset p)
    {
        foreach (var row in _rows)
        {
            if (string.Equals(row.Name.Trim(), p.Flag, StringComparison.OrdinalIgnoreCase))
            {
                if (row.Value.Trim() == p.Value)
                    _rows.Remove(row); // повторный клик — убрать
                else
                    row.Value = p.Value;
                RefreshPresetStates();
                return;
            }
        }
        _rows.Add(new FlagRow { Name = p.Flag, Value = p.Value });
        RefreshPresetStates();
    }

    private void RefreshPresetStates()
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows)
        {
            string name = row.Name.Trim();
            if (name.Length == 0) continue;
            active.Add(name + "\n" + row.Value.Trim());
        }
        var on = (Style)FindResource("PrimaryButton");
        var off = (Style)FindResource("OutlineButton");
        foreach (var btn in _presetButtons)
        {
            if (btn.Tag is FlagPreset p)
                btn.Style = active.Contains(p.Flag + "\n" + p.Value) ? on : off;
        }
    }

    // ===================== Drag & Drop =====================
    //
    // Три источника: чип пресета (PresetDropFormat), чип профиля
    // (ProfileDropFormat) и строка таблицы (RowDropFormat). Зона сброса —
    // карточка таблицы: рамка загорается акцентом, сверху висит подсказка,
    // для строк рисуется линия места вставки.

    private void WireChip(Button btn)
    {
        btn.PreviewMouseLeftButtonDown += Chip_MouseDown;
        btn.PreviewMouseMove += Chip_MouseMove;
    }

    private void Chip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _chipDragged = false;
        _chipDown = e.GetPosition((IInputElement)sender);
    }

    private void Chip_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _chipDown is not Point start) return;
        if (sender is not Button btn || btn.Tag is null) return;
        if (!MovedFarEnough(e.GetPosition(btn), start)) return;
        _chipDown = null;

        if (btn.Tag is FlagPreset fp)
        {
            StartChipDrag(btn, PresetDropFormat, fp,
                Lang.Current == "en" ? fp.TitleEn : fp.Title, DragDropEffects.Copy);
        }
        else if (btn.Tag is string prof)
        {
            StartChipDrag(btn, ProfileDropFormat, prof, prof, DragDropEffects.Copy);
        }
    }

    private static bool MovedFarEnough(Point now, Point start) =>
        Math.Abs(now.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance &&
        Math.Abs(now.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;

    private void StartChipDrag(DependencyObject source, string format, object payload,
        string label, DragDropEffects effects)
    {
        _chipDragged = true;
        var data = new DataObject();
        data.SetData(format, payload);

        Point at = source is FrameworkElement fe
            ? fe.TranslatePoint(new Point(fe.ActualWidth / 2, fe.ActualHeight + 6), this)
            : new Point(14, 18);
        ShowGhost(label, at);
        try { DragDrop.DoDragDrop(source, data, effects); }
        finally { HideGhost(); HideDropFeedback(); }
    }

    private void FlagsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _rowDragSource = null;
        if (IsGridEditing()) return;
        if (FindAncestor<DataGridRow>(FlagsGrid.InputHitTest(e.GetPosition(FlagsGrid)) as DependencyObject)
                is { Item: FlagRow row })
        {
            _rowDragSource = row;
            _rowDownPos = e.GetPosition(FlagsGrid);
        }
    }

    private void FlagsGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_rowDragSource == null || e.LeftButton != MouseButtonState.Pressed) return;
        if (!MovedFarEnough(e.GetPosition(FlagsGrid), _rowDownPos)) return;
        var row = _rowDragSource;
        _rowDragSource = null;
        if (IsGridEditing()) return;

        var data = new DataObject();
        data.SetData(RowDropFormat, row);
        ShowGhost(row.Name, FlagsGrid.TranslatePoint(
            new Point(FlagsGrid.ActualWidth / 2, FlagsGrid.ActualHeight / 2), this));
        try { DragDrop.DoDragDrop(FlagsGrid, data, DragDropEffects.Move); }
        finally { HideGhost(); HideDropFeedback(); }
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        string? format = PickFormat(e.Data);
        if (format == null) return;                 // чужие перетаскивания не трогаем
        e.Handled = true;
        MoveGhost(e.GetPosition(this));

        if (!IsOver(e, FlagsCard))
        {
            e.Effects = DragDropEffects.None;
            HideDropFeedback();
            return;
        }

        e.Effects = format == RowDropFormat ? DragDropEffects.Move : DragDropEffects.Copy;
        ShowZoneHighlight();
        if (format == RowDropFormat)
        {
            ComputeInsertIndex(e.GetPosition(FlagsGrid), out double lineY);
            ShowInsertLine(lineY);
            SetHint("Flags_DropRow");
        }
        else
        {
            HideInsertLine();
            SetHint(format == PresetDropFormat ? "Flags_DropPreset" : "Flags_DropProfile");
        }
    }

    private void Root_DragLeave(object sender, DragEventArgs e)
    {
        var p = e.GetPosition(this);
        if (p.X < 0 || p.Y < 0 || p.X > ActualWidth || p.Y > ActualHeight)
            HideDropFeedback();
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        string? format = PickFormat(e.Data);
        HideDropFeedback();
        if (format == null) return;
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (!IsOver(e, FlagsCard)) return;

        switch (format)
        {
            case PresetDropFormat when e.Data.GetData(PresetDropFormat) is FlagPreset p:
                e.Effects = DragDropEffects.Copy;
                ApplyPresetDrop(p);
                break;
            case ProfileDropFormat when e.Data.GetData(ProfileDropFormat) is string name:
                e.Effects = DragDropEffects.Copy;
                ProfileApplyClicked?.Invoke(name);
                break;
            case RowDropFormat when e.Data.GetData(RowDropFormat) is FlagRow row:
                e.Effects = DragDropEffects.Move;
                int to = ComputeInsertIndex(e.GetPosition(FlagsGrid), out _);
                ReorderRow(_rows.IndexOf(row), to);
                break;
        }
    }

    private static string? PickFormat(IDataObject data)
    {
        if (data.GetDataPresent(RowDropFormat)) return RowDropFormat;
        if (data.GetDataPresent(PresetDropFormat)) return PresetDropFormat;
        if (data.GetDataPresent(ProfileDropFormat)) return ProfileDropFormat;
        return null;
    }

    private static bool IsOver(DragEventArgs e, FrameworkElement target)
    {
        var p = e.GetPosition(target);
        return p.X >= 0 && p.Y >= 0 && p.X <= target.ActualWidth && p.Y <= target.ActualHeight;
    }

    private static T? FindAncestor<T>(DependencyObject? from) where T : DependencyObject
    {
        while (from != null)
        {
            if (from is T t) return t;
            from = from is Visual ? VisualTreeHelper.GetParent(from) : null;
        }
        return null;
    }

    /// <summary>Идёт ли сейчас правка ячейки (фокус на внутри-табличном TextBox) —
    /// в этом режиме перетаскивание строк не начинаем.</summary>
    private bool IsGridEditing()
    {
        if (Keyboard.FocusedElement is not DependencyObject start) return false;
        bool editing = false;
        DependencyObject? d = start;
        while (d != null)
        {
            if (ReferenceEquals(d, FlagsGrid)) return editing;
            if (d is TextBox) editing = true;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : null;
        }
        return false;
    }

    /// <summary>Куда вставить перетаскиваемую строку: индекс в _rows (0..Count)
    /// и Y линии вставки в координатах FlagsGrid.</summary>
    private int ComputeInsertIndex(Point pos, out double lineY)
    {
        lineY = 0;
        if (_rows.Count == 0) { lineY = 4; return 0; }

        if (FindAncestor<DataGridRow>(FlagsGrid.InputHitTest(pos) as DependencyObject) is { } hitRow
            && hitRow.Item is FlagRow hit)
        {
            var top = hitRow.TranslatePoint(new Point(0, 0), FlagsGrid).Y;
            bool before = pos.Y < top + hitRow.ActualHeight * 0.5;
            lineY = before ? top : top + hitRow.ActualHeight;
            int idx = _rows.IndexOf(hit);
            if (idx < 0) return _rows.Count;
            return before ? idx : idx + 1;
        }

        // Мимо строк (шапка таблицы либо пустой хвост) — в начало или в конец.
        double firstTop = double.PositiveInfinity, lastBottom = double.NegativeInfinity;
        foreach (var row in _rows)
        {
            if (FlagsGrid.ItemContainerGenerator.ContainerFromItem(row) is not DataGridRow c) continue;
            var t = c.TranslatePoint(new Point(0, 0), FlagsGrid).Y;
            firstTop = Math.Min(firstTop, t);
            lastBottom = Math.Max(lastBottom, t + c.ActualHeight);
        }
        if (double.IsPositiveInfinity(firstTop)) { lineY = 40; return 0; }
        if (pos.Y >= lastBottom) { lineY = lastBottom; return _rows.Count; }
        lineY = firstTop;
        return 0;
    }

    private void ShowZoneHighlight()
    {
        if (_zoneHot) return;
        _zoneHot = true;
        FlagsCard.SetCurrentValue(Border.BorderBrushProperty, FindResource("GoodBrush"));
        FlagsCard.SetCurrentValue(Border.BackgroundProperty, FindResource("LineSoftBrush"));
    }

    private void HideDropFeedback()
    {
        if (_zoneHot)
        {
            _zoneHot = false;
            FlagsCard.ClearValue(Border.BorderBrushProperty);
            FlagsCard.ClearValue(Border.BackgroundProperty);
        }
        HideInsertLine();
        ResetHint();
    }

    private void ShowInsertLine(double y)
    {
        double max = Math.Max(0, DropOverlay.ActualHeight - 2);
        InsertLine.Width = Math.Max(0, DropOverlay.ActualWidth);
        Canvas.SetTop(InsertLine, Math.Clamp(y, 0, max));
        InsertLine.Visibility = Visibility.Visible;
    }

    private void HideInsertLine() => InsertLine.Visibility = Visibility.Collapsed;

    private void SetHint(string key) => DropHint.Text = Lang.Get(key);

    private void ResetHint() => DropHint.SetBinding(TextBlock.TextProperty,
        new Binding("[Flags_DragHint]") { Source = Lang.Instance, Mode = BindingMode.OneWay });

    private void ShowGhost(string label, Point at)
    {
        _ghostText.Text = label;
        _ghost.IsOpen = true;
        MoveGhost(at);
    }

    private void MoveGhost(Point inPage)
    {
        if (!_ghost.IsOpen) return;
        _ghost.HorizontalOffset = inPage.X + 16;
        _ghost.VerticalOffset = inPage.Y + 20;
    }

    private void HideGhost() => _ghost.IsOpen = false;

    /// <summary>Сброс на таблицу из пресета: добавить флаг или обновить значение
    /// (в отличие от клика, драг никогда не удаляет).</summary>
    public void ApplyPresetDrop(FlagPreset p)
    {
        foreach (var row in _rows)
        {
            if (!string.Equals(row.Name.Trim(), p.Flag, StringComparison.OrdinalIgnoreCase)) continue;
            if (row.Value.Trim() == p.Value) return;
            row.Value = p.Value;
            _view.Refresh();
            RefreshPresetStates();
            return;
        }
        _rows.Add(new FlagRow { Name = p.Flag, Value = p.Value });
    }

    /// <summary>Переместить строку fromIndex на позицию toIndex
    /// (toIndex считается до удаления fromIndex).</summary>
    public void ReorderRow(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _rows.Count) return;
        toIndex = Math.Clamp(toIndex, 0, _rows.Count);
        if (toIndex > fromIndex) toIndex--;
        if (toIndex == fromIndex) return;
        _rows.Move(fromIndex, toIndex);
        FlagsGrid.SelectedItem = _rows[toIndex];
        FlagsGrid.ScrollIntoView(_rows[toIndex]);
    }

    /// <summary>Имена флагов в порядке строк таблицы (для проверок).</summary>
    public List<string> RowNames() => _rows.Select(r => r.Name).ToList();

    public void SetFlags(Dictionary<string, string> flags)
    {
        _rows.Clear();
        foreach (var (k, v) in flags.OrderBy(x => x.Key))
            _rows.Add(new FlagRow { Name = k, Value = v });
        _view.Refresh();
        RefreshPresetStates();
    }

    public Dictionary<string, string> CollectFlags()
    {
        var dict = new Dictionary<string, string>();
        foreach (var row in _rows)
        {
            string name = row.Name.Trim();
            if (name.Length == 0) continue;
            dict[name] = row.Value.Trim();
        }
        return dict;
    }
}
