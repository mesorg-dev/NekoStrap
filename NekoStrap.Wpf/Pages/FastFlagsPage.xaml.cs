using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

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

public sealed record FlagPreset(string Title, string Flag, string Value);

/// <summary>
/// FastFlags: таблица + живой поиск, кликабельные пресеты, импорт/экспорт.
/// </summary>
public partial class FastFlagsPage : UserControl
{
    public static readonly FlagPreset[] BuiltinPresets =
    {
        new("FPS 60 (сток)", "DFIntTaskSchedulerTargetFps", "60"),
        new("FPS 144", "DFIntTaskSchedulerTargetFps", "144"),
        new("FPS 240", "DFIntTaskSchedulerTargetFps", "240"),
        new("Свет: Future", "FFlagDebugForceFutureIsBrightPhase3", "true"),
        new("Свет: ShadowMap", "FFlagDebugForceFutureIsBrightPhase2", "true"),
        new("Свет: Voxel (быстро)", "DFFlagDebugRenderForceTechnologyVoxel", "true"),
        new("Без блюма", "FFlagRenderNoLowFrmBloom", "true"),
        new("Без пост-эффектов", "FFlagDisablePostFx", "true"),
        new("MSAA x4", "FIntDebugForceMSAASamples", "4"),
        new("MSAA x8", "FIntDebugForceMSAASamples", "8"),
        new("Детали далеко", "DFIntCSGLevelOfDetailSwitchingDistance", "5000"),
        new("Без травы", "FIntFRMMaxGrassDistance", "0"),
        new("Дальний зум", "FIntCameraMaxZoomDistance", "1000"),
        new("Без DPI-скейла", "DFFlagDisableDPIScale", "true"),
        new("Рендер: Vulkan", "FFlagDebugGraphicsPreferVulkan", "true"),
        new("Пинг в лог", "DFFlagDebugPrintDataPingBreakDown", "true"),
    };

    public event EventHandler? AddFlagClicked;
    public event EventHandler? SaveClicked;
    public event EventHandler? ImportClicked;
    public event EventHandler? ExportClicked;
    public event EventHandler? DeleteAllClicked;

    private readonly ObservableCollection<FlagRow> _rows = new();
    private readonly ICollectionView _view;
    private string _filter = "";
    private readonly List<Button> _presetButtons = new();

    public FastFlagsPage()
    {
        InitializeComponent();
        FlagsGrid.ItemsSource = _rows;
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = o => o is FlagRow r &&
            (_filter.Length == 0 || r.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        _rows.CollectionChanged += (_, _) => RefreshPresetStates();

        foreach (var p in BuiltinPresets)
        {
            var btn = new Button
            {
                Content = p.Title,
                Style = (Style)FindResource("OutlineButton"),
                Width = 165,
                Height = 34,
                Margin = new Thickness(0, 0, 10, 10),
                Tag = p
            };
            btn.Click += (_, _) => TogglePreset(p);
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
