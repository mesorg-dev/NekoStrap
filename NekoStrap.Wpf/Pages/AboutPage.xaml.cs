using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace NekoStrap.Wpf.Pages;

/// <summary>О программе: hero-карточка + обновления + факты.</summary>
public partial class AboutPage : UserControl
{
    public event EventHandler? CheckUpdatesClicked;
    public event EventHandler? DownloadUpdateClicked;

    public AboutPage()
    {
        InitializeComponent();
        VersionPill.Text = "v" + AppInfo.Version;
        UpdateStatus.Text = Lang.Format("About_CurFmt", AppInfo.Version);
    }

    private void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesClicked?.Invoke(this, EventArgs.Empty);
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadUpdateClicked?.Invoke(this, EventArgs.Empty);
    }

    public void SetUpdateStatus(string text)
    {
        UpdateStatus.Text = text;
    }

    public void ShowDownloadButton(bool show)
    {
        DownloadButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetReleaseNotes(string notes)
    {
        if (notes.Length == 0)
        {
            ReleaseNotes.Visibility = Visibility.Collapsed;
            return;
        }
        string short_ = notes.Length > 400 ? notes[..400] + "…" : notes;
        ReleaseNotes.Text = short_;
        ReleaseNotes.Visibility = Visibility.Visible;
    }

    private void GithubButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLink("https://github.com/mesorg-dev/NekoStrap");
    }

    private void DiscordButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLink("https://discord.com/");
    }

    private void TelegramButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLink("https://t.me/mesorgdev");
    }

    private static void OpenLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }
}
