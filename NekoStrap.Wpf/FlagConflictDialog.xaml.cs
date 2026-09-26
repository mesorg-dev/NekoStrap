using System.Windows;
using System.Windows.Input;

namespace NekoStrap.Wpf;

/// <summary>Что делать с конфликтом при импорте флагов.</summary>
public enum FlagConflictChoice
{
    /// <summary>Заменить только этот флаг, дальше спросить снова.</summary>
    Replace,

    /// <summary>Заменить этот и все оставшиеся конфликты без вопросов.</summary>
    ReplaceAll,

    /// <summary>Не заменять: пропустить этот и все оставшиеся конфликты.</summary>
    Keep
}

/// <summary>
/// Спрос про флаг, который уже стоит с другим значением: заменить,
/// заменить все или не заменять. Закрытие крестиком = не заменять.
/// </summary>
public partial class FlagConflictDialog : Window
{
    public FlagConflictChoice Choice { get; private set; } = FlagConflictChoice.Keep;

    public FlagConflictDialog(string flagName, string currentValue, string incomingValue)
    {
        InitializeComponent();
        KeyText.Text = flagName;
        OldValueText.Text = Lang.Format("Dlg_FlagConflictNowFmt", currentValue);
        NewValueText.Text = Lang.Format("Dlg_FlagConflictNewFmt", incomingValue);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Finish(FlagConflictChoice.Keep);
    private void KeepButton_Click(object sender, RoutedEventArgs e) => Finish(FlagConflictChoice.Keep);
    private void ReplaceButton_Click(object sender, RoutedEventArgs e) => Finish(FlagConflictChoice.Replace);
    private void ReplaceAllButton_Click(object sender, RoutedEventArgs e) => Finish(FlagConflictChoice.ReplaceAll);

    private void Finish(FlagConflictChoice choice)
    {
        Choice = choice;
        DialogResult = true;
    }
}
