using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using NekoStrap.Roblox;

namespace NekoStrap.Wpf.Pages;

/// <summary>Строка таблицы аккаунтов.</summary>
public sealed class AccountRow
{
    public required string Label { get; init; }
    public required string Name { get; init; }
    public required string IdText { get; init; }
    public required string AddedText { get; init; }
    public required string Status { get; init; }
    public required bool IsActive { get; init; }
    public required AccountEntry Entry { get; init; }
}

/// <summary>Аккаунты: список, добавление по куке, активный для запуска, бэкап.</summary>
public partial class AccountsPage : UserControl
{
    public event EventHandler? AddClicked;
    public event EventHandler? RefreshClicked;
    public event EventHandler? ActivateClicked;
    public event EventHandler? DeleteClicked;
    public event EventHandler? ExportClicked;
    public event EventHandler? ImportClicked;
    public event EventHandler? PlayAsActiveClicked;
    public event EventHandler? ClearActiveClicked;

    private readonly ObservableCollection<AccountRow> _rows = new();

    public AccountsPage()
    {
        InitializeComponent();
        AccountsList.ItemsSource = _rows;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => AddClicked?.Invoke(this, EventArgs.Empty);
    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshClicked?.Invoke(this, EventArgs.Empty);
    private void ActivateButton_Click(object sender, RoutedEventArgs e) => ActivateClicked?.Invoke(this, EventArgs.Empty);
    private void DeleteButton_Click(object sender, RoutedEventArgs e) => DeleteClicked?.Invoke(this, EventArgs.Empty);
    private void ExportButton_Click(object sender, RoutedEventArgs e) => ExportClicked?.Invoke(this, EventArgs.Empty);
    private void ImportButton_Click(object sender, RoutedEventArgs e) => ImportClicked?.Invoke(this, EventArgs.Empty);
    private void PlayButton_Click(object sender, RoutedEventArgs e) => PlayAsActiveClicked?.Invoke(this, EventArgs.Empty);
    private void ClearButton_Click(object sender, RoutedEventArgs e) => ClearActiveClicked?.Invoke(this, EventArgs.Empty);

    public void SetActive(string text)
    {
        ActiveLabel.Text = text;
    }

    public void SetAccounts(IEnumerable<(AccountEntry entry, string status, bool isActive)> rows)
    {
        _rows.Clear();
        foreach (var (entry, status, isActive) in rows)
        {
            _rows.Add(new AccountRow
            {
                Label = (isActive ? "● " : "") + entry.Label,
                Name = entry.Name,
                IdText = entry.UserId > 0 ? entry.UserId.ToString() : "—",
                AddedText = entry.AddedAt == default ? "—"
                    : entry.AddedAt.ToLocalTime().ToString("d MMM yyyy"),
                Status = status,
                IsActive = isActive,
                Entry = entry
            });
        }
    }

    public AccountEntry? SelectedEntry()
    {
        return AccountsList.SelectedItem is AccountRow r ? r.Entry : null;
    }
}
