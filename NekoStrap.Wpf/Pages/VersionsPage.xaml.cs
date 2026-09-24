using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using NekoStrap.Roblox;

namespace NekoStrap.Wpf.Pages;

/// <summary>Строка таблицы версий.</summary>
public sealed class VersionRow
{
    public required string Guid { get; init; }
    public required string SizeText { get; init; }
    public required string Status { get; init; }
    public required string Studio { get; init; }
    public required bool IsActive { get; init; }
}

/// <summary>Версии клиента: откат, удаление, Studio.</summary>
public partial class VersionsPage : UserControl
{
    public event EventHandler? ActivateClicked;
    public event EventHandler? DeleteClicked;
    public event EventHandler? StudioInstallClicked;
    public event EventHandler? StudioLaunchClicked;

    private readonly ObservableCollection<VersionRow> _rows = new();

    public VersionsPage()
    {
        InitializeComponent();
        VersionsList.ItemsSource = _rows;
    }

    private void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        ActivateClicked?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteClicked?.Invoke(this, EventArgs.Empty);
    }

    private void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        StudioInstallClicked?.Invoke(this, EventArgs.Empty);
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        StudioLaunchClicked?.Invoke(this, EventArgs.Empty);
    }

    public void SetVersions(List<InstalledVersionInfo> versions, string? activeGuid, string? previousGuid)
    {
        _rows.Clear();
        foreach (var v in versions)
        {
            string status = v.Guid == activeGuid ? Lang.Get("Ver_Active")
                : v.Guid == previousGuid ? Lang.Get("Ver_Previous") : "—";
            _rows.Add(new VersionRow
            {
                Guid = v.Guid,
                SizeText = VersionManager.FormatSize(v.SizeBytes,
                    Lang.Get("Size_Gb"), Lang.Get("Size_Mb"), Lang.Get("Size_Kb")),
                Status = status,
                Studio = v.HasStudio ? Lang.Get("Ver_Yes") : Lang.Get("Ver_No"),
                IsActive = v.Guid == activeGuid
            });
        }
    }

    public string? SelectedVersion()
    {
        return VersionsList.SelectedItem is VersionRow r ? r.Guid : null;
    }

    public void SetStudioStatus(string text)
    {
        StudioStatus.Text = Lang.Format("Common_StatusFmt", text);
    }
}
