using System.Diagnostics;
using System.Text.Json;

namespace NekoStrap.Roblox
{
    /// <summary>Релиз лаунчера на GitHub.</summary>
    public sealed record AppRelease(
        string Tag, string Name, string Notes, string DownloadUrl, DateTime PublishedAt);

    /// <summary>
    /// Автообновление самого лаунчера с GitHub Releases (mesorg-dev/NekoStrap).
    /// Схема: latest-релиз → сравнение тега с текущей версией → скачивание
    /// exe во временную папку → батник ждёт выхода процесса, подменяет exe
    /// и перезапускает. Никаких прав не надо (exe портативный).
    /// Соглашение о релизе: тег vX.Y.Z, к релизу приложен NekoStrap.exe
    /// (single-file, собирается workflow release.yml).
    /// </summary>
    internal static class AppUpdater
    {
        public const string RepoOwner = "mesorg-dev";
        public const string RepoName = "NekoStrap";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        static AppUpdater()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("NekoStrap-Updater/1.0");
            Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        /// <summary>Текущая версия сборки: Major.Minor.Build.</summary>
        public static string CurrentVersion
        {
            get
            {
                try
                {
                    var v = typeof(AppUpdater).Assembly.GetName().Version;
                    if (v != null) return $"{v.Major}.{v.Minor}.{v.Build}";
                }
                catch { /* ignore */ }
                return "1.0.0";
            }
        }

        /// <summary>Latest-релиз с GitHub. null — нет сети/нет релизов.</summary>
        public static async Task<AppRelease?> GetLatestAsync(CancellationToken ct = default)
        {
            try
            {
                string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
                var root = doc.RootElement;
                string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                if (tag.Length == 0) return null;
                string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;
                string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                DateTime published = DateTime.MinValue;
                if (root.TryGetProperty("published_at", out var p))
                    DateTime.TryParse(p.GetString(), out published);

                // Берём первый .exe из ассетов (предпочтительно с win-x64 в имени).
                string download = "";
                if (root.TryGetProperty("assets", out var assets))
                {
                    string fallback = "";
                    foreach (var a in assets.EnumerateArray())
                    {
                        string an = a.TryGetProperty("name", out var anp) ? anp.GetString() ?? "" : "";
                        string au = a.TryGetProperty("browser_download_url", out var aup) ? aup.GetString() ?? "" : "";
                        if (!an.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || au.Length == 0)
                            continue;
                        if (fallback.Length == 0) fallback = au;
                        if (an.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
                        {
                            download = au;
                            break;
                        }
                    }
                    if (download.Length == 0) download = fallback;
                }
                if (download.Length == 0) return null;
                return new AppRelease(tag, name, notes, download, published);
            }
            catch
            {
                return null; // без сети / нет релизов / API прилегло — молча
            }
        }

        /// <summary>Тег новее текущей версии? Тег вида v1.2.3 (v опциональна).</summary>
        public static bool IsNewer(string current, string tag)
        {
            if (!TryParseVersion(current, out var cur)) return false;
            if (!TryParseVersion(tag, out var rel)) return false;
            return rel > cur;
        }

        internal static bool TryParseVersion(string s, out Version v)
        {
            v = new Version(0, 0, 0);
            try
            {
                s = s.Trim().TrimStart('v', 'V');
                // Отрезаем суффиксы вида -beta, +build.
                int cut = s.IndexOfAny(new[] { '-', '+' });
                if (cut > 0) s = s[..cut];
                var parts = s.Split('.');
                if (parts.Length is < 1 or > 4) return false;
                int[] nums = { 0, 0, 0, 0 };
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!int.TryParse(parts[i], out int n) || n < 0) return false;
                    nums[i] = n;
                }
                v = new Version(nums[0], nums[1], nums[2], nums[3]);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Скачать exe релиза. Прогресс (прочитано, всего).</summary>
        public static async Task DownloadAsync(
            string url, string destPath, IProgress<(long done, long total)>? progress,
            CancellationToken ct = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? ".");
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? -1;
            using var net = await resp.Content.ReadAsStreamAsync(ct);
            using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var buf = new byte[81920];
            long done = 0;
            int read;
            while ((read = await net.ReadAsync(buf, ct)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, read), ct);
                done += read;
                try { progress?.Report((done, total)); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Установить скачанное и перезапуститься: батник ждёт выхода текущего
        /// процесса (exe занят, пока жив), подменяет файл и стартует заново.
        /// </summary>
        public static void InstallAndRestart(string newExePath)
        {
            string? current = Environment.ProcessPath;
            if (current == null)
                throw new InvalidOperationException("не знаю путь текущего exe");
            int pid = Environment.ProcessId;
            string bat = Path.Combine(Path.GetTempPath(), "nekostrap-update.bat");
            string script =
                "@echo off\r\n" +
                $"set NEKO_NEW=\"{newExePath}\"\r\n" +
                $"set NEKO_EXE=\"{current}\"\r\n" +
                $":waitloop\r\n" +
                $"tasklist /FI \"PID eq {pid}\" 2>NUL | find \"{pid}\" >NUL\r\n" +
                "if not errorlevel 1 (timeout /t 1 /nobreak >NUL & goto waitloop)\r\n" +
                "move /y %NEKO_NEW% %NEKO_EXE% >NUL\r\n" +
                "start \"\" %NEKO_EXE%\r\n" +
                "del \"%~f0\"\r\n";
            File.WriteAllText(bat, script);
            Process.Start(new ProcessStartInfo(bat)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });
        }
    }
}
