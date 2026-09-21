using System.Windows;
using System.Windows.Media;

namespace NekoStrap.Wpf;

/// <summary>
/// Живая тема: перекрашивает тинты стекла и дёргает Glass-хосты.
/// Сами панели (CardBrush/ChromeBrush) всегда сплошные — стеклянный вид
/// дают сэмплы блюра под контентом (см. Glass), а не прозрачность кистей.
/// Поэтому никакого Transparent-каскада и перерисовок всего окна.
/// </summary>
internal static class UiTheme
{
    public static void Apply(bool hasWallpaper, bool glassEnabled, int glassOpacity)
    {
        if (Application.Current?.Resources == null) return;
        bool glass = hasWallpaper && glassEnabled && glassOpacity < 100;
        // Затемнение элементов отдельно от фона: итоговая альфа тинта =
        // непрозрачность × затемнение. Хочешь светлые панели — убавь затемнение,
        // хочешь тёмные — подними (фон при этом крутится своим слайдером).
        double alphaF = 255 * Math.Clamp(glassOpacity, 0, 100) / 100.0
            * Math.Clamp(_glassDim, 0, 100) / 100.0;
        byte alpha = (byte)Math.Clamp(alphaF, 0, 255);

        var res = Application.Current.Resources;
        Color cardBase = (Color)res["BgPanel"];
        Color chromeBase = (Color)res["Bg"];
        // Панели сплошные всегда (сэмплы лежат поверх фона, под контентом).
        res["CardBrush"] = new SolidColorBrush(cardBase);
        res["ChromeBrush"] = new SolidColorBrush(chromeBase);

        Glass.Active = glass;
        Glass.BlurRadius = _glassBlur * 0.3; // 0..100% → 0..30px
        Glass.TintCard = new SolidColorBrush(Color.FromArgb(alpha, cardBase.R, cardBase.G, cardBase.B));
        Glass.TintChrome = new SolidColorBrush(Color.FromArgb(alpha, chromeBase.R, chromeBase.G, chromeBase.B));
        Glass.NotifySettingsChanged();
    }

    private static int _glassBlur = 50;
    private static int _glassDim = 100;

    /// <summary>Сила блюра подложки 0..100 (пишет MainWindow из конфига).</summary>
    internal static int GlassBlur
    {
        get => _glassBlur;
        set => _glassBlur = Math.Clamp(value, 0, 100);
    }

    /// <summary>Затемнение элементов 0..100 (пишет MainWindow из конфига).</summary>
    internal static int GlassDim
    {
        get => _glassDim;
        set => _glassDim = Math.Clamp(value, 0, 100);
    }
}
