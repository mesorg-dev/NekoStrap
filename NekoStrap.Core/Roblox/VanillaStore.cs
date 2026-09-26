using System.Text.Json;

namespace NekoStrap.Roblox
{
    /// <summary>Снимок «до чистого запуска»: версия, профили и флаги клиента.</summary>
    internal sealed class VanillaState
    {
        public string VersionGuid { get; set; } = "";
        public string ModProfile { get; set; } = "";
        public string FlagProfile { get; set; } = "";
        public Dictionary<string, string> Flags { get; set; } = new();
    }

    /// <summary>
    /// Чистый запуск: перед стартом кладём снимок состояния, сам клиент
    /// приводим к ванили (флаги пустые, моды откатаны), а после выхода
    /// всё возвращаем как было. Снимок живёт на диске — переживает крах
    /// лаунчера, восстановится при следующем старте.
    /// </summary>
    internal static class VanillaStore
    {
        public static string FilePath => Path.Combine(RobloxPaths.BaseDir, "vanilla_backup.json");

        public static bool Exists
        {
            get { try { return File.Exists(FilePath); } catch { return false; } }
        }

        public static VanillaState? Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<VanillaState>(File.ReadAllText(FilePath));
            }
            catch { /* битый снимок — не восстанавливаем */ }
            return null;
        }

        public static void Save(VanillaState state)
        {
            Directory.CreateDirectory(RobloxPaths.BaseDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state,
                new JsonSerializerOptions { WriteIndented = true }));
        }

        public static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch { /* ignore */ }
        }
    }
}
