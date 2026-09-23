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
        // Чистое стекло: тинт не нужен вообще, но блюр рисуем всегда.
        bool glass = hasWallpaper && glassEnabled && (GlassPure || glassOpacity < 100);
        // Затемнение элементов отдельно от фона: итоговая альфа тинта =
        // непрозрачность × затемнение. Хочешь светлые панели — убавь затемнение,
        // хочешь тёмные — подними (фон при этом крутится своим слайдером).
        double alphaF = GlassPure ? 0 : 255 * Math.Clamp(glassOpacity, 0, 100) / 100.0
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

    /// <summary>Чистое стекло без тинта (пишет MainWindow из конфига).</summary>
    internal static bool GlassPure { get; set; }

    private static readonly HashSet<string> EmbeddedFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unbounded", "Inter", "JetBrains Mono"
    };

    /// <summary>
    /// Текст живьём: шрифты по ролям + цвета. Все Foreground/FontFamily
    /// в XAML смотрят через DynamicResource — замена объектов мгновенно
    /// перекрашивает весь интерфейс, перезапуск не нужен.
    /// </summary>
    internal static void ApplyText(NekoStrap.Roblox.LauncherConfig cfg)
    {
        if (Application.Current?.Resources == null) return;
        var res = Application.Current.Resources;
        res["UiFont"] = MakeFont(cfg.ThemeFontBody, "Inter", "Segoe UI");
        res["HeadingFont"] = MakeFont(cfg.ThemeFontHeading, "Unbounded", "Segoe UI");
        res["MonoFont"] = MakeFont(cfg.ThemeFontMono, "JetBrains Mono", "Consolas");
        res["FgBrush"] = new SolidColorBrush(ParseColor(cfg.ThemeFg, "#F2F2EF"));
        res["FgDimBrush"] = new SolidColorBrush(ParseColor(cfg.ThemeDim, "#98989E"));
        res["FgDimmerBrush"] = new SolidColorBrush(ParseColor(cfg.ThemeDimmer, "#5C5C62"));
    }

    private static FontFamily MakeFont(string name, string embeddedDefault, string fallback)
    {
        if (name.Length == 0) name = embeddedDefault;
        if (EmbeddedFonts.Contains(name))
        {
            return new FontFamily(new Uri("pack://application:,,,/"),
                $"./Fonts/#{name}, {fallback}");
        }
        // Системный шрифт (неизвестное имя не роняет — WPF подставит дефолт).
        return new FontFamily(name);
    }

    internal static Color ParseColor(string hex, string fallback)
    {
        try
        {
            hex = hex.Trim().TrimStart('#');
            if (hex.Length == 6)
            {
                return Color.FromRgb(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16));
            }
        }
        catch { /* ignore — упадём на дефолт */ }
        return (Color)ColorConverter.ConvertFromString(fallback);
    }
}
