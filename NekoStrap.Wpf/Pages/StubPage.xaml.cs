using System.Windows.Controls;

namespace NekoStrap.Wpf.Pages;

/// <summary>
/// Временная заглушка раздела, пока страница не переехала с WinForms.
/// </summary>
public partial class StubPage : UserControl
{
    public StubPage()
    {
        InitializeComponent();
    }

    public string PageTitle
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    public string PageSub
    {
        get => SubText.Text;
        set => SubText.Text = value;
    }
}
