using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace NekoStrap.Wpf;

/// <summary>
/// Контекстное меню трея в живой палитре лаунчера.
///
/// Цвета читаются из ресурсов WPF прямо в момент отрисовки, поэтому смена
/// темы/шрифтов применяется к уже открытому меню — пересоздавать иконку
/// не нужно. Палитра та же, что на страницах: панель #BgPanel, линии #Line,
/// текст #Fg / #FgDim, подсветка = середина между ними.
/// </summary>
internal sealed class TrayMenuRenderer : WinForms.ToolStripProfessionalRenderer
{
    public TrayMenuRenderer() : base(new TrayPalette()) { }

    /// <summary>Маркер «шапка»: яркий текст, которым выделяем заголовок меню.</summary>
    public const string AccentTag = "accent";

    protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
    {
        // Disabled-пункты (заголовки секций) гасим, шапку — подсвечиваем.
        e.TextColor = e.Item.Tag as string == AccentTag
            ? TrayPalette.Res("FgBrush", 0xF2, 0xF2, 0xEF)
            : e.Item.Enabled ? TrayPalette.Res("FgBrush", 0xF2, 0xF2, 0xEF)
                             : TrayPalette.Res("FgDimBrush", 0x98, 0x98, 0x9E);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(WinForms.ToolStripSeparatorRenderEventArgs e)
    {
        var line = TrayPalette.Res("LineBrush", 0x2A, 0x2A, 0x2E);
        using var dark = new Pen(line);
        using var light = new Pen(line);
        int mid = e.Item.Height / 2;
        e.Graphics.DrawLine(dark, 0, mid, e.Item.Width, mid);
        e.Graphics.DrawLine(light, 0, mid + 1, e.Item.Width, mid + 1);
    }
}

/// <summary>
/// Цветовая таблица для ToolStripProfessionalRenderer. Всё считается на лету
/// из WPF-ресурсов — статичные константы WinForms тему не знают.
/// </summary>
internal sealed class TrayPalette : WinForms.ProfessionalColorTable
{
    public TrayPalette()
    {
        // Системные цвета Windows не совпадают с палитрой лаунчера.
        UseSystemColors = false;
    }

    private static Color Panel => Res("BgPanelBrush", 0x12, 0x12, 0x14);
    private static Color Line => Res("LineBrush", 0x2A, 0x2A, 0x2E);

    /// <summary>Подсветка/выбор: 45% от линии к тексту — видно на любой теме.</summary>
    private static Color Hover
    {
        get
        {
            var a = Res("LineBrush", 0x2A, 0x2A, 0x2E);
            var b = Res("FgDimBrush", 0x98, 0x98, 0x9E);
            return Color.FromArgb(
                (byte)(a.R + (b.R - a.R) * 0.45),
                (byte)(a.G + (b.G - a.G) * 0.45),
                (byte)(a.B + (b.B - a.B) * 0.45));
        }
    }

    // Фон выпадающего списка и поля вокруг него (вместо градиента по умолчанию).
    public override Color ToolStripDropDownBackground => Panel;
    public override Color ImageMarginGradientBegin => Panel;
    public override Color ImageMarginGradientMiddle => Panel;
    public override Color ImageMarginGradientEnd => Panel;
    public override Color ImageMarginRevealedGradientBegin => Panel;
    public override Color ImageMarginRevealedGradientMiddle => Panel;
    public override Color ImageMarginRevealedGradientEnd => Panel;
    public override Color MenuStripGradientBegin => Panel;
    public override Color MenuStripGradientEnd => Panel;
    public override Color RaftingContainerGradientBegin => Panel;
    public override Color RaftingContainerGradientEnd => Panel;
    public override Color ToolStripPanelGradientBegin => Panel;
    public override Color ToolStripPanelGradientEnd => Panel;
    public override Color ToolStripGradientBegin => Panel;
    public override Color ToolStripGradientMiddle => Panel;
    public override Color ToolStripGradientEnd => Panel;
    public override Color OverflowButtonGradientBegin => Panel;
    public override Color OverflowButtonGradientMiddle => Panel;
    public override Color OverflowButtonGradientEnd => Panel;

    // Границы: рамка меню и разделители — линия темы.
    public override Color ToolStripBorder => Line;
    public override Color MenuBorder => Line;
    public override Color SeparatorDark => Line;
    public override Color SeparatorLight => Line;
    public override Color MenuItemBorder => Hover;

    // Наведение и выбор пункта.
    public override Color MenuItemSelected => Hover;
    public override Color MenuItemSelectedGradientBegin => Hover;
    public override Color MenuItemSelectedGradientEnd => Hover;
    public override Color ButtonSelectedHighlight => Hover;
    public override Color ButtonSelectedHighlightBorder => Line;
    public override Color ButtonPressedHighlight => Hover;
    public override Color ButtonPressedHighlightBorder => Line;
    public override Color ButtonCheckedHighlight => Hover;
    public override Color ButtonCheckedHighlightBorder => Line;

    // Галочки у включённых тумблеров.
    public override Color CheckBackground => Hover;
    public override Color CheckSelectedBackground => Hover;
    public override Color CheckPressedBackground => Hover;

    /// <summary>Цвет кисти из ресурсов WPF; недоступно — дефолт темы.</summary>
    internal static Color Res(string key, byte r, byte g, byte b)
    {
        try
        {
            if (Application.Current?.Resources?[key] is System.Windows.Media.SolidColorBrush brush)
            {
                var c = brush.Color;
                return Color.FromArgb(c.R, c.G, c.B);
            }
        }
        catch { /* ресурс ещё не готов — отрисуем дефолтом */ }
        return Color.FromArgb(r, g, b);
    }
}

