using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NekoStrap.Roblox;

namespace NekoStrap.Wpf.Pages;

/// <summary>Строка таблицы модов.</summary>
public sealed class ModRow
{
    public required string Path { get; init; }
    public required string Status { get; init; }
    public required string SizeText { get; init; }
    public string Author { get; init; } = "—";
    public required bool Active { get; init; }
}

/// <summary>
/// Моды: профили (инстансы набора файлов), быстрая замена лёгких файлов,
/// файлы активного профиля и Fleasion. Логика — в MainWindow.
/// </summary>
public partial class ModsPage : UserControl
{
    public event EventHandler? AddModClicked;
    public event EventHandler? RefreshClicked;
    public event EventHandler? ToggleModClicked;
    public event EventHandler? DeleteModClicked;
    public event EventHandler? FleasionDownloadClicked;

    public event Action<string>? ProfileSelected;
    public event EventHandler? ProfileCreateClicked;
    public event EventHandler? ProfileDuplicateClicked;
    public event EventHandler? ProfileDeleteClicked;
    public event EventHandler? ProfileFolderClicked;
    public event EventHandler? ProfileBindRequested;
    public event EventHandler? ProfileUnbindRequested;
    public event EventHandler? ProfileExportRequested;
    public event EventHandler? ProfileImportRequested;

    public event Action<string>? QuickPickRequested;
    public event Action<string>? QuickResetRequested;
    public event Action<string, string>? QuickTargetEdited;

    private readonly ObservableCollection<ModRow> _rows = new();
    private bool _loadingProfiles;

    public ModsPage()
    {
        InitializeComponent();
        ModsList.ItemsSource = _rows;
    }

    public CheckBox FleasionCheck => FleasionCheckBox;

    /// <summary>Имя нового профиля из поля ввода (без лишних пробелов).</summary>
    public string ProfileNameText => ProfileNameBox.Text.Trim();

    public void ClearProfileName() => ProfileNameBox.Text = "";

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        AddModClicked?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshClicked?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleModClicked?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteModClicked?.Invoke(this, EventArgs.Empty);
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        FleasionDownloadClicked?.Invoke(this, EventArgs.Empty);
    }

    private void FolderButton_Click(object sender, RoutedEventArgs e)
    {
        try { FleasionManager.OpenFolder(); } catch { /* ignore */ }
    }

    public void SetMods(List<ModEntry> mods)
    {
        _rows.Clear();
        foreach (var m in mods)
        {
            string size = m.Size >= 1048576
                ? Lang.Format("Size_MbFmt", m.Size / 1048576.0)
                : Lang.Format("Size_KbFmt", Math.Max(1, m.Size / 1024));
            _rows.Add(new ModRow
            {
                Path = m.RelativePath,
                Status = m.Active ? Lang.Get("Mod_Active") : Lang.Get("Mod_Disabled"),
                SizeText = size,
                Active = m.Active
            });
        }
    }

    public void SetFleasionStatus(string text)
    {
        FleasionStatus.Text = Lang.Format("Common_StatusFmt", text);
    }

    public List<string> SelectedModPaths()
    {
        return ModsList.SelectedItems.Cast<ModRow>().Select(r => r.Path).ToList();
    }

    // ================= Профили =================

    /// <summary>Комбobox профилей + статус: сколько файлов лежит в активном.</summary>
    public void SetProfiles(List<string> names, string active, int fileCount)
    {
        _loadingProfiles = true;
        try
        {
            ProfilesCombo.ItemsSource = names;
            ProfilesCombo.SelectedItem = names.FirstOrDefault(n =>
                string.Equals(n, active, StringComparison.OrdinalIgnoreCase))
                ?? (names.Count > 0 ? names[0] : null);
        }
        finally
        {
            _loadingProfiles = false;
        }
        ProfileStatus.Text = Lang.Format("ModP_FilesFmt", fileCount);
    }

    private void ProfilesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingProfiles) return;
        if (ProfilesCombo.SelectedItem is string name) ProfileSelected?.Invoke(name);
    }

    private void ProfileNewButton_Click(object sender, RoutedEventArgs e) =>
        ProfileCreateClicked?.Invoke(this, EventArgs.Empty);

    private void ProfileDupButton_Click(object sender, RoutedEventArgs e) =>
        ProfileDuplicateClicked?.Invoke(this, EventArgs.Empty);

    private void ProfileDeleteButton_Click(object sender, RoutedEventArgs e) =>
        ProfileDeleteClicked?.Invoke(this, EventArgs.Empty);

    private void ProfileFolderButton_Click(object sender, RoutedEventArgs e) =>
        ProfileFolderClicked?.Invoke(this, EventArgs.Empty);

    private void ProfileBindButton_Click(object sender, RoutedEventArgs e) =>
        ProfileBindRequested?.Invoke(this, EventArgs.Empty);

    private void ProfileUnbindButton_Click(object sender, RoutedEventArgs e) =>
        ProfileUnbindRequested?.Invoke(this, EventArgs.Empty);

    private void ProfileExportButton_Click(object sender, RoutedEventArgs e) =>
        ProfileExportRequested?.Invoke(this, EventArgs.Empty);

    private void ProfileImportButton_Click(object sender, RoutedEventArgs e) =>
        ProfileImportRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Что привязано к последней игре (пусто — ничего).</summary>
    public void SetBindStatus(string text) => ProfileBindStatus.Text = text;

    // ================= Быстрая замена =================

    /// <summary>Строки слотов: цель в клиенте, статус, «Выбрать» и «Сброс».</summary>
    public void BuildQuickRows()
    {
        QuickPanel.Children.Clear();
        foreach (var slot in QuickAssets.Slots())
        {
            bool on = QuickAssets.IsInstalled(slot.Id);

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = Lang.Get(slot.TitleKey),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            title.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
            title.SetResourceReference(TextBlock.ForegroundProperty, "FgDimBrush");

            var path = new TextBox
            {
                Text = slot.Target,
                Height = 34,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = Lang.Get("Quick_PathTip"),
                Tag = slot.Id
            };
            path.SetResourceReference(TextBox.StyleProperty, "DarkInput");
            path.LostFocus += QuickPath_LostFocus;
            path.KeyDown += QuickPath_KeyDown;

            var status = new TextBlock
            {
                Text = on ? Lang.Get("Quick_On") : Lang.Get("Quick_Off"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            status.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            status.SetResourceReference(TextBlock.ForegroundProperty, on ? "GoodBrush" : "FgDimmerBrush");

            var pick = MakeQuickButton(Lang.Get("Btn_Pick"), slot.Id, QuickPick_Click);
            var reset = MakeQuickButton(Lang.Get("Btn_Reset"), slot.Id, QuickReset_Click);

            Grid.SetColumn(title, 0);
            Grid.SetColumn(path, 1);
            Grid.SetColumn(status, 2);
            Grid.SetColumn(pick, 3);
            Grid.SetColumn(reset, 4);
            grid.Children.Add(title);
            grid.Children.Add(path);
            grid.Children.Add(status);
            grid.Children.Add(pick);
            grid.Children.Add(reset);
            QuickPanel.Children.Add(grid);
        }
    }

    private static Button MakeQuickButton(string text, string slotId, RoutedEventHandler onClick)
    {
        var btn = new Button
        {
            Content = text,
            Height = 34,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(10, 0, 0, 0),
            Tag = slotId
        };
        btn.SetResourceReference(Button.StyleProperty, "OutlineButton");
        btn.Click += onClick;
        return btn;
    }

    private void QuickPick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) QuickPickRequested?.Invoke(id);
    }

    private void QuickReset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) QuickResetRequested?.Invoke(id);
    }

    private void QuickPath_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) CommitQuickPath(box);
    }

    private void QuickPath_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || e.Key != Key.Enter) return;
        CommitQuickPath(box);
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private string _lastAttemptId = "";
    private string _lastAttemptText = "";

    private void CommitQuickPath(TextBox box)
    {
        if (box.Tag is not string id) return;
        string text = box.Text.Trim();
        string current = QuickAssets.Get(id)?.Target ?? "";
        if (text == current) return;
        // Один и тот же (например, негодный) путь не дёргаем дважды:
        // Enter и LostFocus срабатывают подряд.
        if (id == _lastAttemptId && text == _lastAttemptText) return;
        _lastAttemptId = id;
        _lastAttemptText = text;
        QuickTargetEdited?.Invoke(id, text);
    }
}
