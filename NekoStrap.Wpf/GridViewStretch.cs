using System.Windows;
using System.Windows.Controls;

namespace NekoStrap.Wpf;

/// <summary>
/// Растяжение последней колонки GridView под ширину окна. Штатный WPF
/// колонки «*» не умеет — фиксированные ширины оставляют пустую полосу
/// справа на широких окнах и горизонтальный скролл на узких.
/// </summary>
public static class GridViewStretch
{
    /// <summary>Минимальная ширина растягиваемой колонки.</summary>
    private const double MinWidth = 90;

    /// <summary>Внутренние отступы строки (Padding карточки DarkListRow) + запас.</summary>
    private const double RowPadding = 16 + 4;

    /// <summary>Подписаться на ресайз списка: колонка тянется сразу и дальше.</summary>
    public static void Attach(ListView list)
    {
        list.SizeChanged += (_, _) => Stretch(list);
        list.Loaded += (_, _) => Stretch(list);
    }

    private static void Stretch(ListView list)
    {
        if (list.View is not GridView gv || gv.Columns.Count < 2) return;
        try
        {
            double others = 0;
            for (int i = 0; i < gv.Columns.Count - 1; i++)
                others += gv.Columns[i].ActualWidth;
            double avail = list.ActualWidth - RowPadding - others;
            gv.Columns[^1].Width = Math.Max(MinWidth, avail);
        }
        catch { /* layout может дёрнуться до готовности колонок */ }
    }
}
