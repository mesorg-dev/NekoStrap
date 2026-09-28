using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NekoStrap.Wpf;

/// <summary>Строка списка выбора: подпись в строке и её значение.</summary>
public sealed record PickItem(string Label, string Value);

/// <summary>
/// Выбор одной позиции из списка: игра для привязки авто-профиля,
/// набор флагов для удаления. allowManual — показать поле для ввода
/// значения с клавиатуры (PlaceId, если игры ещё нет в истории).
/// </summary>
public partial class PickDialog : Window
{
    /// <summary>Что выбрал пользователь (null — отмена).</summary>
    public PickItem? Picked { get; private set; }

    public PickDialog(string title, string hint, IEnumerable<PickItem> items,
        bool allowManual = false)
    {
        InitializeComponent();
        TitleText.Text = title;
        HintText.Text = hint;
        HintText.Visibility = hint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ManualRow.Visibility = allowManual ? Visibility.Visible : Visibility.Collapsed;
        ItemsList.ItemsSource = items.ToList();
        UpdateOk();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancel();
        else if (e.Key == Key.Enter && OkButton.IsEnabled) Accept();
    }

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateOk();

    private void ManualBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateOk();

    private void ItemsList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsList.SelectedItem is PickItem && OkButton.IsEnabled) Accept();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => Accept();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Cancel();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Cancel();

    private void UpdateOk()
    {
        OkButton.IsEnabled = ItemsList.SelectedItem is PickItem ||
            (ManualRow.Visibility == Visibility.Visible && ManualBox.Text.Trim().Length > 0);
    }

    private void Accept()
    {
        // Введённое вручную важнее выбранного пункта: так можно
        // быстро править PlaceId поверх подсветки списка.
        string manual = ManualBox.Text.Trim();
        if (ManualRow.Visibility == Visibility.Visible && manual.Length > 0)
            Picked = new PickItem(manual, manual);
        else if (ItemsList.SelectedItem is PickItem item)
            Picked = item;
        else
            return;
        DialogResult = true;
    }

    private void Cancel()
    {
        Picked = null;
        DialogResult = false;
    }
}
