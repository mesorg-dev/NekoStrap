using System.Text.Json;

namespace NekoStrap.Roblox
{
    /// <summary>Слот быстрой замены: цель внутри клиента + ключ подписи + фильтр файла.</summary>
    public sealed record QuickSlot(string Id, string Target, string TitleKey, string Filter);

    /// <summary>
    /// Быстрая замена «лёгких» файлов клиента: звук смерти, шрифты, курсор,
    /// текстуры. Правило слота: цель с расширением (ouch.ogg) = один файл,
    /// кладётся ровно под этим именем; цель без расширения (content\fonts) =
    /// папка, файлы кладутся с их собственными именами.
    /// Всё пишется в АКТИВНЫЙ профиль модов — дальше работает обычное
    /// применение модов, никакой магии поверх клиента.
    /// Свои (правленые) пути слотов лежат в quick_assets.json.
    /// </summary>
    internal static class QuickAssets
    {
        public static string FilePath => Path.Combine(RobloxPaths.BaseDir, "quick_assets.json");

        private static readonly QuickSlot[] BuiltIn =
        {
            new("death",  "content\\sounds\\ouch.ogg",                    "Quick_Death",    "audio"),
            new("fonts",  "content\\fonts",                               "Quick_Fonts",    "font"),
            new("cursor", "content\\textures\\Cursors\\KeyboardMouse",    "Quick_Cursor",   "image"),
            new("ui",     "content\\textures",                            "Quick_UI",       "image"),
            new("ground", "PlatformContent\\pc\\textures",                "Quick_Platform", "image"),
            new("custom", "",                                             "Quick_Custom",   "any"),
        };

        /// <summary>Все слоты: встроенные цели + сохранённые правки.</summary>
        public static List<QuickSlot> Slots()
        {
            var over = Load();
            var list = new List<QuickSlot>(BuiltIn.Length);
            foreach (var s in BuiltIn)
            {
                string target = s.Target;
                if (over.TryGetValue(s.Id, out var edited))
                {
                    string? norm = Normalize(edited);
                    if (norm != null) target = norm;
                }
                list.Add(s with { Target = target });
            }
            return list;
        }

        public static QuickSlot? Get(string id) =>
            Slots().FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Пустая строка = слот выключен. false = путь негодный.</summary>
        public static bool TrySetTarget(string id, string target)
        {
            target = (target ?? "").Trim();
            if (target.Length > 0 && Normalize(target) == null) return false;
            var over = Load();
            over[id] = target.Length == 0 ? "" : Normalize(target)!;
            Save(over);
            return true;
        }

        /// <summary>Копирует выбранные файлы в активный профиль. Возвращает число файлов.</summary>
        public static int Install(string id, string[] sourcePaths)
        {
            var slot = Get(id);
            if (slot == null || slot.Target.Length == 0 || sourcePaths.Length == 0) return 0;
            string root = ModManager.ActiveDir();
            string? dest = ModManager.SafePath(root, slot.Target);
            if (dest == null) return 0;

            int n = 0;
            if (IsFolderTarget(slot.Target))
            {
                Directory.CreateDirectory(dest);
                foreach (var src in sourcePaths)
                {
                    if (!File.Exists(src)) continue;
                    try
                    {
                        File.Copy(src, Path.Combine(dest, Path.GetFileName(src)), overwrite: true);
                        n++;
                    }
                    catch { /* пропускаем недоступный файл */ }
                }
            }
            else
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? ".");
                    File.Copy(sourcePaths[0], dest, overwrite: true);
                    n = 1;
                }
                catch { /* ignore */ }
            }
            return n;
        }

        /// <summary>Слот уже лежит в активном профиле?</summary>
        public static bool IsInstalled(string id)
        {
            var slot = Get(id);
            if (slot == null || slot.Target.Length == 0) return false;
            string? dest = ModManager.SafePath(ModManager.ActiveDir(), slot.Target);
            if (dest == null) return false;
            try
            {
                return IsFolderTarget(slot.Target)
                    ? Directory.Exists(dest) && Directory.EnumerateFiles(dest).Any()
                    : File.Exists(dest);
            }
            catch { return false; }
        }

        /// <summary>Убрать слот из активного профиля (клиент вернётся к ванили).</summary>
        public static bool Reset(string id)
        {
            var slot = Get(id);
            if (slot == null || slot.Target.Length == 0) return false;
            string? dest = ModManager.SafePath(ModManager.ActiveDir(), slot.Target);
            if (dest == null) return false;
            try
            {
                if (IsFolderTarget(slot.Target))
                {
                    if (!Directory.Exists(dest)) return false;
                    Directory.Delete(dest, recursive: true);
                    return true;
                }
                if (!File.Exists(dest)) return false;
                File.Delete(dest);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Папка (нет расширения) или один файл (есть расширение).</summary>
        public static bool IsFolderTarget(string target)
        {
            string t = (target ?? "").Trim().TrimEnd('\\', '/');
            return !Path.HasExtension(t);
        }

        /// <summary>Нормализация цели или null, если путь выходит за пределы клиента.</summary>
        public static string? Normalize(string? target)
        {
            string t = (target ?? "").Trim().Replace('/', '\\').TrimStart('\\');
            if (t.Length == 0 || t.Length > 200) return null;
            if (t.Contains("..") || Path.IsPathRooted(t) || t.Contains(':')) return null;
            foreach (char c in t)
                if (c < 32 || "<>\"|?*".IndexOf(c) >= 0) return null;
            return t;
        }

        private static Dictionary<string, string> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new(StringComparer.OrdinalIgnoreCase);
                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in doc.RootElement.EnumerateObject())
                    result[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : "";
                return result;
            }
            catch { return new(StringComparer.OrdinalIgnoreCase); }
        }

        private static void Save(Dictionary<string, string> data)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? ".");
                File.WriteAllText(FilePath, JsonSerializer.Serialize(data,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* ignore */ }
        }
    }
}
