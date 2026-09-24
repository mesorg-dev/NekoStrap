namespace NekoStrap.Roblox
{
    public sealed record InstalledVersionInfo(
        string Guid, long SizeBytes, DateTime Modified, bool HasPlayer, bool HasStudio);

    /// <summary>
    /// Установленные версии: список, удаление, откат (активация предыдущей).
    /// Держим текущую + предыдущую + Studio, остальное чистится.
    /// </summary>
    internal static class VersionManager
    {
        public static List<InstalledVersionInfo> List()
        {
            var list = new List<InstalledVersionInfo>();
            try
            {
                if (!Directory.Exists(RobloxPaths.VersionsDir)) return list;
                foreach (var dir in Directory.GetDirectories(RobloxPaths.VersionsDir, "version-*"))
                {
                    string guid = Path.GetFileName(dir);
                    if (!RobloxPaths.IsVersionGuid(guid)) continue;
                    list.Add(new InstalledVersionInfo(
                        guid,
                        DirSize(dir),
                        Directory.GetLastWriteTime(dir),
                        File.Exists(Path.Combine(dir, RobloxPaths.PlayerExe)),
                        File.Exists(Path.Combine(dir, RobloxInstaller.StudioExe))));
                }
            }
            catch { /* ignore */ }
            return list.OrderByDescending(v => v.Modified).ToList();
        }

        public static long DirSize(string dir)
        {
            long sum = 0;
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { sum += new FileInfo(f).Length; } catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }
            return sum;
        }

        public static void Delete(string guid)
        {
            if (!RobloxPaths.IsVersionGuid(guid)) return;
            string dir = Path.Combine(RobloxPaths.VersionsDir, guid);
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Не вышло удалить версию: " + ex.Message);
            }
        }

        public static string FormatSize(long bytes) => FormatSize(bytes, "ГБ", "МБ", "КБ");

        /// <summary>То же с явными единицами (для локализации UI).</summary>
        public static string FormatSize(long bytes, string gbUnit, string mbUnit, string kbUnit)
        {
            if (bytes >= 1073741824) return $"{bytes / 1073741824.0:F1} {gbUnit}";
            if (bytes >= 1048576) return $"{bytes / 1048576.0:F0} {mbUnit}";
            return $"{Math.Max(1, bytes / 1024)} {kbUnit}";
        }
    }
}
