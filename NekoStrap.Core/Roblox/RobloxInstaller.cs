using System.IO.Compression;

namespace NekoStrap.Roblox
{
    /// <summary>
    /// Прогресс установки: Phase — код фазы (check/done/download/extract/setup),
    /// Detail — либо null, либо код детали (already/installed + Arg), либо
    /// техническая строка как есть (имя пакета, счётчик). Тексты собирает UI
    /// по кодам — поэтому установщик не тащит русских строк (локализация).
    /// </summary>
    public sealed record InstallProgress(string Phase, string? Detail, double? Fraction, string? Arg = null);

    /// <summary>
    /// Установка/обновление клиента и Studio: версия → манифест → зипы →
    /// распаковка по карте директорий (как в Voidstrap) → AppSettings.xml
    /// (он же маркер целой установки) → моды → чистка старых версий.
    /// </summary>
    internal static class RobloxInstaller
    {
        public const string StudioExe = "RobloxStudioBeta.exe";

        // Карта распаковки плеера — 1 в 1 как CommonAppData._commonMap в Voidstrap
        // (+ RobloxApp.zip), неизвестные пакеты — в корень версии.
        private static readonly Dictionary<string, string> PlayerDirMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Libraries.zip", "" },
            { "RobloxApp.zip", "" },
            { "redist.zip", "" },
            { "shaders.zip", "shaders" },
            { "ssl.zip", "ssl" },
            { "WebView2.zip", "" },
            { "WebView2RuntimeInstaller.zip", "WebView2RuntimeInstaller" },
            { "content-avatar.zip", @"content\avatar" },
            { "content-configs.zip", @"content\configs" },
            { "content-fonts.zip", @"content\fonts" },
            { "content-sky.zip", @"content\sky" },
            { "content-sounds.zip", @"content\sounds" },
            { "content-textures2.zip", @"content\textures" },
            { "content-models.zip", @"content\models" },
            { "content-textures3.zip", @"PlatformContent\pc\textures" },
            { "content-terrain.zip", @"PlatformContent\pc\terrain" },
            { "content-platform-fonts.zip", @"PlatformContent\pc\fonts" },
            { "content-platform-dictionaries.zip", @"PlatformContent\pc\shared_compression_dictionaries" },
            { "extracontent-luapackages.zip", @"ExtraContent\LuaPackages" },
            { "extracontent-translations.zip", @"ExtraContent\translations" },
            { "extracontent-models.zip", @"ExtraContent\models" },
            { "extracontent-textures.zip", @"ExtraContent\textures" },
            { "extracontent-places.zip", @"ExtraContent\places" },
        };

        // Карта Studio — как RobloxStudioData.PackageDirectoryMap в Voidstrap.
        private static readonly Dictionary<string, string> StudioDirMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "RobloxStudio.zip", "" },
            { "LibrariesQt5.zip", "" },
            { "content-studio_svg_textures.zip", @"content\studio_svg_textures" },
            { "content-qt_translations.zip", @"content\qt_translations" },
            { "content-api-docs.zip", @"content\api_docs" },
            { "extracontent-scripts.zip", @"ExtraContent\scripts" },
            { "studiocontent-models.zip", @"StudioContent\models" },
            { "studiocontent-textures.zip", @"StudioContent\textures" },
            { "BuiltInPlugins.zip", "BuiltInPlugins" },
            { "BuiltInStandalonePlugins.zip", "BuiltInStandalonePlugins" },
            { "ApplicationConfig.zip", "ApplicationConfig" },
            { "Plugins.zip", "Plugins" },
            { "Qml.zip", "Qml" },
            { "StudioFonts.zip", "StudioFonts" },
            { "RibbonConfig.zip", "RibbonConfig" },
        };

        private const string AppSettingsXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<Settings>\r\n" +
            "\t<ContentFolder>content</ContentFolder>\r\n" +
            "\t<BaseUrl>http://www.roblox.com</BaseUrl>\r\n</Settings>\r\n";

        /// <summary>Возвращает guid установленной (или уже бывшей) версии плеера.</summary>
        public static Task<string> EnsureInstalledAsync(
            LauncherConfig cfg,
            IProgress<InstallProgress>? progress,
            CancellationToken ct)
        {
            return EnsureAsync("WindowsPlayer", RobloxPaths.PlayerExe, PlayerDirMap,
                applyMods: true, progress, ct,
                installed: PlayerInstalled(cfg),
                onInstalled: guid => cfg.InstalledVersion = guid);
        }

        /// <summary>Возвращает guid установленной Studio.</summary>
        public static Task<string> EnsureStudioInstalledAsync(
            LauncherConfig cfg,
            IProgress<InstallProgress>? progress,
            CancellationToken ct)
        {
            return EnsureAsync("WindowsStudio64", StudioExe, StudioDirMap,
                applyMods: false, progress, ct,
                installed: StudioInstalled(cfg),
                onInstalled: guid => cfg.StudioVersion = guid);
        }

        private static string? PlayerInstalled(LauncherConfig cfg)
        {
            string? found = RobloxPaths.FindInstalledVersion();
            if (found != null) return found;
            if (RobloxPaths.IsVersionGuid(cfg.InstalledVersion))
            {
                string dir = Path.Combine(RobloxPaths.VersionsDir, cfg.InstalledVersion);
                if (File.Exists(Path.Combine(dir, RobloxPaths.PlayerExe)) &&
                    File.Exists(Path.Combine(dir, "AppSettings.xml")))
                    return cfg.InstalledVersion;
            }
            return null;
        }

        private static string? StudioInstalled(LauncherConfig cfg)
        {
            if (!RobloxPaths.IsVersionGuid(cfg.StudioVersion)) return null;
            string dir = Path.Combine(RobloxPaths.VersionsDir, cfg.StudioVersion);
            if (File.Exists(Path.Combine(dir, StudioExe)) &&
                File.Exists(Path.Combine(dir, "AppSettings.xml")))
                return cfg.StudioVersion;
            return null;
        }

        private static async Task<string> EnsureAsync(
            string binaryType, string exeName,
            Dictionary<string, string> dirMap, bool applyMods,
            IProgress<InstallProgress>? progress, CancellationToken ct,
            string? installed, Action<string> onInstalled)
        {
            progress?.Report(new InstallProgress("check", null, null));
            var latest = await Deployment.GetLatestVersionAsync(binaryType, ct);

            if (installed == latest.VersionGuid)
            {
                onInstalled(installed);
                if (applyMods)
                    ModManager.ApplyTo(Path.Combine(RobloxPaths.VersionsDir, installed));
                progress?.Report(new InstallProgress("done", "already", 1, installed));
                return installed;
            }

            progress?.Report(new InstallProgress("download", null, null));
            string manifest = await Deployment.DownloadManifestAsync(latest.VersionGuid, ct);
            var packages = PackageManifest.Parse(manifest);

            string versionDir = Path.Combine(RobloxPaths.VersionsDir, latest.VersionGuid);
            string dlDir = Path.Combine(RobloxPaths.DownloadsDir, latest.VersionGuid);
            long totalPacked = packages.Sum(p => p.PackedSize);
            long donePacked = 0;

            for (int i = 0; i < packages.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var pkg = packages[i];
                string zipPath = Path.Combine(dlDir, pkg.Name);

                var fileProg = new Progress<(long done, long total)>(t =>
                {
                    double frac = totalPacked > 0
                        ? 0.85 * (donePacked + t.done) / totalPacked
                        : 0;
                    progress?.Report(new InstallProgress("download",
                        $"{pkg.Name} ({i + 1}/{packages.Count})", frac));
                });

                await Deployment.DownloadPackageAsync(
                    Deployment.PackageUrl(latest.VersionGuid, pkg.Name), zipPath, fileProg, ct);
                donePacked += new FileInfo(zipPath).Length;

                progress?.Report(new InstallProgress("extract",
                    $"{pkg.Name} ({i + 1}/{packages.Count})", 0.85 + 0.15 * (i + 1) / packages.Count));

                string sub = dirMap.GetValueOrDefault(pkg.Name) ?? "";
                string dest = Path.Combine(versionDir, sub);
                Directory.CreateDirectory(dest);
                ExtractZipSafe(zipPath, dest);
            }

            progress?.Report(new InstallProgress("setup", null, 1));
            await File.WriteAllTextAsync(Path.Combine(versionDir, "AppSettings.xml"), AppSettingsXml, ct);
            if (applyMods)
                ModManager.ApplyTo(versionDir);
            onInstalled(latest.VersionGuid);

            progress?.Report(new InstallProgress("done", "installed", 1, latest.VersionGuid));
            return latest.VersionGuid;
        }

        /// <summary>
        /// Распаковка вручную: System.IO в .NET режет записи с путями наружу
        /// (в зипах Roblox такие есть), а SharpZip из Voidstrap — нет.
        /// CDN официальный, но пути всё равно санитизируем и такое пропускаем.
        /// </summary>
        private static void ExtractZipSafe(string zipPath, string destDir)
        {
            Directory.CreateDirectory(destDir);
            string fullDest = Path.GetFullPath(destDir);
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.Length == 0 || entry.Name.Length == 0) continue; // папка
                string target = Path.GetFullPath(Path.Combine(fullDest, entry.FullName));
                if (!target.StartsWith(fullDest + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? fullDest);
                entry.ExtractToFile(target, overwrite: true);
            }
        }

        /// <summary>Чистит загрузки и версии, кроме перечисленных guid (откат!).</summary>
        public static void CleanupExcept(params string[] keepGuids)
        {
            try
            {
                if (Directory.Exists(RobloxPaths.DownloadsDir))
                    Directory.Delete(RobloxPaths.DownloadsDir, true);
            }
            catch { /* ignore */ }
            try
            {
                foreach (var d in Directory.GetDirectories(RobloxPaths.VersionsDir, "version-*"))
                {
                    string guid = Path.GetFileName(d);
                    if (!keepGuids.Contains(guid, StringComparer.OrdinalIgnoreCase))
                    {
                        try { Directory.Delete(d, true); } catch { /* ignore */ }
                    }
                }
            }
            catch { /* ignore */ }
        }
    }
}
