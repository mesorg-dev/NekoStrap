namespace NekoStrap.Roblox
{
    /// <summary>
    /// Все пути лаунчера. База по умолчанию — %LocalAppData%\NekoStrap,
    /// переопределяется полем «Путь установки Roblox» в настройках.
    /// Раскладка версий — как в Bloxstrap/Voidstrap: Versions\version-xxx.
    /// </summary>
    internal static class RobloxPaths
    {
        public const string PlayerExe = "RobloxPlayerBeta.exe";

        public static string BaseDir { get; private set; } = DefaultBaseDir();

        public static void Configure(string? customDir)
        {
            BaseDir = string.IsNullOrWhiteSpace(customDir) ? DefaultBaseDir() : customDir.Trim();
        }

        public static string DefaultBaseDir() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NekoStrap");

        public static string VersionsDir => Path.Combine(BaseDir, "Versions");
        public static string ModsDir => Path.Combine(BaseDir, "Mods");
        public static string DownloadsDir => Path.Combine(BaseDir, "Downloads");
        public static string ConfigPath => Path.Combine(BaseDir, "config.json");

        /// <summary>guid вида version- + 16 hex-символов, всего 24.</summary>
        public static bool IsVersionGuid(string? s)
        {
            if (string.IsNullOrEmpty(s) || s.Length != 24 ||
                !s.StartsWith("version-", StringComparison.Ordinal))
                return false;
            for (int i = 8; i < 24; i++)
            {
                if (!Uri.IsHexDigit(s[i])) return false;
            }
            return true;
        }

        /// <summary>
        /// Ищет ЦЕЛУЮ установку: папка version-* с exe И AppSettings.xml.
        /// AppSettings пишется только после полной распаковки — это маркер
        /// того, что прошлая установка не умерла посередине (проверено тестом).
        /// </summary>
        public static string? FindInstalledVersion()
        {
            try
            {
                if (!Directory.Exists(VersionsDir)) return null;
                foreach (var dir in Directory.GetDirectories(VersionsDir, "version-*"))
                {
                    string guid = Path.GetFileName(dir);
                    if (IsVersionGuid(guid) &&
                        File.Exists(Path.Combine(dir, PlayerExe)) &&
                        File.Exists(Path.Combine(dir, "AppSettings.xml")))
                        return guid;
                }
            }
            catch { /* ignore */ }
            return null;
        }
    }
}
