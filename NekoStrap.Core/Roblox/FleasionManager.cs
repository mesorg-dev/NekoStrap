using System.Diagnostics;
using System.Text.Json;

namespace NekoStrap.Roblox
{
    /// <summary>
    /// Fleasion — внешний инструмент для замены ассетов/шейдеров Roblox
    /// (github.com/fleasion/Fleasion). Как в Voidstrap: качаем свежий
    /// Fleasion.exe с GitHub-релизов в Extensions\Fleasion и стартуем рядом с игрой.
    /// </summary>
    internal static class FleasionManager
    {
        private const string ReleasesApi = "https://api.github.com/repos/fleasion/Fleasion/releases/latest";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        static FleasionManager()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("NekoStrap/1.0");
        }

        public static string Dir => Path.Combine(RobloxPaths.BaseDir, "Extensions", "Fleasion");
        public static string ExePath => Path.Combine(Dir, "Fleasion.exe");

        public static bool IsInstalled() => File.Exists(ExePath);
        public static bool IsRunning() => Process.GetProcessesByName("Fleasion").Length > 0;

        public static async Task<string> ResolveDownloadUrlAsync(CancellationToken ct = default)
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(ReleasesApi, ct));
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                if (name.Equals("Fleasion.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("-Windows.exe", StringComparison.OrdinalIgnoreCase))
                {
                    string url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    if (url.Length > 0) return url;
                }
            }
            throw new InvalidDataException("в релизе Fleasion нет exe");
        }

        public static async Task DownloadAsync(
            IProgress<(long done, long total)>? progress, CancellationToken ct)
        {
            string url = await ResolveDownloadUrlAsync(ct);
            Directory.CreateDirectory(Dir);
            string tmp = ExePath + ".download";
            using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            res.EnsureSuccessStatusCode();
            long total = res.Content.Headers.ContentLength ?? -1;
            await using var net = await res.Content.ReadAsStreamAsync(ct);
            await using var file = new FileStream(tmp, FileMode.Create, FileAccess.Write,
                FileShare.None, 81920, useAsync: true);
            var buf = new byte[81920];
            long done = 0;
            int r;
            while ((r = await net.ReadAsync(buf, ct)) > 0)
            {
                await file.WriteAsync(buf.AsMemory(0, r), ct);
                done += r;
                progress?.Report((done, total));
            }
            File.Move(tmp, ExePath, overwrite: true);
        }

        public static void Uninstall()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("Fleasion"))
                {
                    try { p.Kill(); } catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }
            try
            {
                if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Не вышло удалить папку: " + ex.Message);
            }
        }

        /// <summary>Старт рядом с игрой (если включён и стоит; уже запущенный не трогаем).</summary>
        public static void LaunchIfEnabled(bool enabled)
        {
            if (!enabled || !IsInstalled() || IsRunning()) return;
            try
            {
                Process.Start(new ProcessStartInfo(ExePath)
                {
                    WorkingDirectory = Dir,
                    UseShellExecute = false
                });
            }
            catch { /* Fleasion опционален — игра стартует и без него */ }
        }

        public static void OpenFolder()
        {
            Directory.CreateDirectory(Dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Dir}\"")
            {
                UseShellExecute = true
            });
        }
    }
}
