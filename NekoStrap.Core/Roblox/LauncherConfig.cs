using System.Text.Json;

namespace NekoStrap.Roblox
{
    /// <summary>
    /// Конфиг лаунчера (JSON): тумблеры настроек + путь + запомненная версия.
    /// </summary>
    internal sealed class LauncherConfig
    {
        public bool CloseOnLaunch { get; set; }
        public bool DiscordRpc { get; set; }
        public bool Sounds { get; set; } = true;
        public bool AutoUpdate { get; set; }
        public bool Animations { get; set; } = true;
        public string RobloxPath { get; set; } = "";
        public string InstalledVersion { get; set; } = "";
        public string PreviousVersion { get; set; } = "";

        // FPS-анлокер: 0 = не трогать флаг, иначе DFIntTaskSchedulerTargetFps.
        public int FpsLimit { get; set; } = 0;

        // Discord: свой Application ID из discord.com/developers (без него RPC молчит).
        public string DiscordAppId { get; set; } = "";
        public bool DiscordAllowJoin { get; set; } = true;

        // Трей лаунчера.
        public bool MinimizeToTray { get; set; }
        public bool CloseToTray { get; set; }

        // Фиксы поведения самого Roblox (appStorage.json).
        public bool RobloxNoTray { get; set; } = true;
        public bool RobloxNoStartup { get; set; } = true;

        // Расширения.
        public bool FleasionEnabled { get; set; }

        // Studio (ставится по желанию, живёт рядом в Versions).
        public string StudioVersion { get; set; } = "";

        // Мастер-выключатель всех уведомлений лаунчера. По умолчанию ВЫКЛ.
        public bool NotificationsEnabled { get; set; }

        // Последняя игра (показываем на главной).
        public string LastGameName { get; set; } = "";
        public long LastGamePlaceId { get; set; }
        public DateTime LastGameAt { get; set; }

        // Геометрия окна (-1 = нет сохранения).
        public int WinX { get; set; } = -1;
        public int WinY { get; set; } = -1;
        public int WinW { get; set; } = -1;
        public int WinH { get; set; } = -1;
        public bool WinMax { get; set; }

        // Цветокоррекция экрана (Magnification API, без инъекций).
        public bool ColorFxEnabled { get; set; }
        public bool ColorFxAuto { get; set; } = true;
        public float ColorFxSat { get; set; } = 1f;
        public float ColorFxBri { get; set; }
        public float ColorFxCon { get; set; } = 1f;
        public float ColorFxTemp { get; set; }

        // Счётчик игрового времени.
        public bool TrackPlaytime { get; set; } = true;

        // Фон лаунчера («Обои»): путь к файлу + сила блюра 0..100.
        // Пусто — дефолтный сплошной Theme.Bg.
        public string WallpaperPath { get; set; } = "";
        public int WallpaperBlur { get; set; } = 0;

        // Затемнение фона 0..100 (тёмный оверлей поверх картинки/гифки).
        // 78 = как было исторически (~78% Theme.Bg), 0 = картинка как есть.
        public int WallpaperDim { get; set; } = 78;

        // Кастомные звуки интерфейса: ключ звука (click/hover/open/close/on/off)
        // → путь к wav. Пусто/битый файл — встроенный звук.
        public Dictionary<string, string> CustomSounds { get; set; } = new();

        // Громкость каждого звука 0..1 (множитель поверх мастер-громкости).
        // Нет записи — 1 (как раньше).
        public Dictionary<string, float> SoundVolumes { get; set; } = new();

        // Внешний вид: шрифты по ролям (пусто = встроенные по умолчанию).
        public string ThemeFontHeading { get; set; } = "Unbounded";
        public string ThemeFontBody { get; set; } = "Inter";
        public string ThemeFontMono { get; set; } = "JetBrains Mono";
        // Цвета текста hex (#RRGGBB, пусто = дефолт палитры).
        public string ThemeFg { get; set; } = "";
        public string ThemeDim { get; set; } = "";
        public string ThemeDimmer { get; set; } = "";

        // Язык интерфейса: auto (по системе), ru, en.
        public string Language { get; set; } = "auto";

        // «Стекло»: прозрачные панели с блюром подложки (liquid glass).
        // Выкл — вид как раньше, без единого лишнего действия на кадр.
        public bool GlassEnabled { get; set; }
        public int GlassOpacity { get; set; } = 85; // % непрозрачности тинта
        public int GlassBlur { get; set; } = 50; // % блюра подложки

        // Затемнение элементов 0..100: насколько тёмный тинт лежит на стекле.
        // 100 = как раньше (полный тинт), 0 = чистое размытие без затемнения.
        // Итоговая альфа тинта = непрозрачность × затемнение. Фон затемняется
        // отдельно (WallpaperDim) — можно светлый фон + тёмные панели и наоборот.
        public int GlassDim { get; set; } = 100;

        // Чистое стекло: блюр без тинта вообще (рамки остаются для чёткости).
        // false = тонированное (тинт + блюр).
        public bool GlassPure { get; set; }

        public static LauncherConfig Load(string path)
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(path)) ?? new LauncherConfig();
            }
            catch { /* битый конфиг — начинаем с чистого */ }
            return new LauncherConfig();
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
