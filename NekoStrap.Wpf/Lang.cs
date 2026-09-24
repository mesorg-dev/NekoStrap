using System.ComponentModel;
using System.Globalization;
using System.Windows.Markup;

namespace NekoStrap.Wpf;

/// <summary>
/// Язык интерфейса: ru/en + авто. Словари лежат в LangStrings.
/// Переключение живое: все XAML-тексты висят на {L Key} (биндинг к Instance),
/// смена языка поднимает PropertyChanged(null) — обновляется всё сразу.
/// Code-behind берёт строки через Lang.Get() в момент показа.
/// </summary>
internal sealed class Lang : INotifyPropertyChanged
{
    public static readonly Lang Instance = new();

    public static event PropertyChangedEventHandler? StaticChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => StaticChanged += value;
        remove => StaticChanged -= value;
    }

    private static string _current = "ru";

    /// <summary>ru или en. Авто резолвится в Resolve() при старте.</summary>
    public static string Current => _current;

    public static CultureInfo Culture =>
        _current == "en" ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("ru-RU");

    public static void Set(string lang)
    {
        lang = lang == "en" ? "en" : "ru";
        if (_current == lang) return;
        _current = lang;
        StaticChanged?.Invoke(Instance, new PropertyChangedEventArgs(null));
    }

    /// <summary>Авто: русская система — ru, иначе en.</summary>
    public static string Resolve(string? saved)
    {
        if (saved == "en") return "en";
        if (saved == "ru") return "ru";
        try
        {
            string sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return sys == "ru" ? "ru" : "en";
        }
        catch { return "ru"; }
    }

    /// <summary>Индексер для биндингов {L Key}.</summary>
    public string this[string key] => Get(key);

    public static string Get(string key)
    {
        if (LangStrings.En.TryGetValue(key, out var en) && _current == "en")
            return en;
        if (LangStrings.Ru.TryGetValue(key, out var ru))
            return ru;
        if (en != null) return en;
        return key; // ключа нет вообще — видно в UI, легко найти
    }

    public static string Format(string key, params object?[] args)
    {
        try { return string.Format(Culture, Get(key), args); }
        catch { return Get(key); }
    }

    /// <summary>Подпись звука по ключу движка (click/hover/open/close/on/off).</summary>
    public static string SoundTitle(string key) => key switch
    {
        "click" => Get("Sound_Click"),
        "hover" => Get("Sound_Hover"),
        "open" => Get("Sound_Open"),
        "close" => Get("Sound_Close"),
        "on" => Get("Sound_On"),
        "off" => Get("Sound_Off"),
        _ => key,
    };
}

/// <summary>Разметка для XAML: Text="{L Main_Title}".</summary>
[MarkupExtensionReturnType(typeof(string))]
internal sealed class LExtension : MarkupExtension
{
    public string Key { get; }

    public LExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new System.Windows.Data.Binding($"[{Key}]")
        {
            Source = Lang.Instance,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}
