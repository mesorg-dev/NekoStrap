using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
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
/// Моды: файлы из папки Mods и Fleasion. API повторяет WinForms-версию.
/// </summary>
public partial class ModsPage : UserControl
{
    public event EventHandler? AddModClicked;
    public event EventHandler? RefreshClicked;
    public event EventHandler? ToggleModClicked;
    public event EventHandler? DeleteModClicked;
    public event EventHandler? FleasionDownloadClicked;

    private readonly ObservableCollection<ModRow> _rows = new();

    public ModsPage()
    {
        InitializeComponent();
        ModsList.ItemsSource = _rows;
    }

    public CheckBox FleasionCheck => FleasionCheckBox;

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
                ? $"{m.Size / 1048576.0:F1} МБ"
                : $"{Math.Max(1, m.Size / 1024)} КБ";
            _rows.Add(new ModRow
            {
                Path = m.RelativePath,
                Status = m.Active ? "Активен" : "Выключен",
                SizeText = size,
                Active = m.Active
            });
        }
    }

    public void SetFleasionStatus(string text)
    {
        FleasionStatus.Text = "Статус: " + text;
    }

    public List<string> SelectedModPaths()
    {
        return ModsList.SelectedItems.Cast<ModRow>().Select(r => r.Path).ToList();
    }
}
