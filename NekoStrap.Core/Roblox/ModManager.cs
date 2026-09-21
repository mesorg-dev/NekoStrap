namespace NekoStrap.Roblox
{
    public sealed record ModEntry(string RelativePath, long Size, bool Active);

    /// <summary>
    /// Моды: файлы из папки Mods зеркалятся в установку (как Modifications
    /// в Bloxstrap/Voidstrap — относительный путь = путь внутри клиента).
    /// *.lock и *.disabled игнорируются/отключаются.
    /// </summary>
    internal static class ModManager
    {
        public static List<ModEntry> List()
        {
            var list = new List<ModEntry>();
            try
            {
                if (!Directory.Exists(RobloxPaths.ModsDir)) return list;
                foreach (var f in Directory.GetFiles(RobloxPaths.ModsDir, "*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(f);
                    if (name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) continue;
                    bool active = !name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    list.Add(new ModEntry(
                        Path.GetRelativePath(RobloxPaths.ModsDir, f),
                        new FileInfo(f).Length, active));
                }
            }
            catch { /* ignore */ }
            return list.OrderBy(m => m.RelativePath).ToList();
        }

        public static void AddFiles(string[] sourcePaths)
        {
            Directory.CreateDirectory(RobloxPaths.ModsDir);
            foreach (var src in sourcePaths)
            {
                string dest = Path.Combine(RobloxPaths.ModsDir, Path.GetFileName(src));
                File.Copy(src, dest, overwrite: true);
            }
        }

        /// <summary>Копирует моды поверх версии. Возвращает число применённых.</summary>
        public static int ApplyTo(string versionDir)
        {
            int n = 0;
            try
            {
                if (!Directory.Exists(RobloxPaths.ModsDir)) return 0;
                foreach (var f in Directory.GetFiles(RobloxPaths.ModsDir, "*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(f);
                    if (name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string dest = Path.Combine(versionDir, Path.GetRelativePath(RobloxPaths.ModsDir, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? ".");
                    File.Copy(f, dest, overwrite: true);
                    n++;
                }
            }
            catch { /* ignore */ }
            return n;
        }
    }
}
