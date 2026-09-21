using System.Windows;
using System.Windows.Controls;

namespace NekoStrap.Wpf.Pages;

/// <summary>Диалог добавления: кука (скрыта) + необязательная подпись.</summary>
public partial class AddAccountDialog : Window
{
    public string CookieText => (ShowCookie.IsChecked == true ? CookieOpenBox.Text : CookieBox.Password).Trim();
    public string AliasText => AliasBox.Text.Trim();

    public AddAccountDialog()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        };
    }

    private void ShowCookie_Changed(object sender, RoutedEventArgs e)
    {
        bool show = ShowCookie.IsChecked == true;
        if (show)
            CookieOpenBox.Text = CookieBox.Password;
        else
            CookieBox.Password = CookieOpenBox.Text;
        CookieBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        CookieOpenBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (CookieText.Length == 0)
        {
            MessageBox.Show(this, "Вставь куку.", "NekoStrap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
