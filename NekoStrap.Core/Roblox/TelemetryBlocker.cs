using System.Diagnostics;

namespace NekoStrap.Roblox
{
    /// <summary>
    /// Блокировка телеметрии Roblox через файл hosts (как в Voidstrap):
    /// добавляет/удаляет записи с маркером # NEKOSTRAP-TELEMETRYBLOCK.
    /// Требует прав администратора для записи в hosts.
    /// </summary>
    internal static class TelemetryBlocker
    {
        private const string Marker = "# NEKOSTRAP-TELEMETRYBLOCK";

        public static readonly string[] Domains =
        {
            "client-telemetry.roblox.com",
            "ephemeralcounters.api.roblox.com",
            "metrics.roblox.com",
            "tracing.roblox.com",
            "lms.roblox.com",
            "ncs.roblox.com",
            "gold.roblox.com",
            "abtesting.roblox.com",
            "upload.crashes.roblox.com",
            "upload.crashes.rbxinfra.com",
            "roblox.qq.com",
        };

        private static string HostsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers", "etc", "hosts");

        /// <summary>Проверить, есть ли наши записи в hosts.</summary>
        public static bool IsApplied()
        {
            try
            {
                if (!File.Exists(HostsPath)) return false;
                return File.ReadAllLines(HostsPath)
                    .Any(l => l.Contains(Marker, StringComparison.Ordinal));
            }
            catch { return false; }
        }

        /// <summary>Применить блокировку (нужны права администратора).</summary>
        public static bool Apply()
        {
            try
            {
                var lines = ReadHostsWithoutBlock();
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                    lines.Add(string.Empty);
                foreach (var domain in Domains)
                {
                    lines.Add($"0.0.0.0 {domain} {Marker}");
                    lines.Add($":: {domain} {Marker}");
                }
                File.WriteAllLines(HostsPath, lines);
                FlushDns();
                return true;
            }
            catch { return false; }
        }

        /// <summary>Убрать наши записи из hosts.</summary>
        public static bool Remove()
        {
            try
            {
                if (!File.Exists(HostsPath)) return true;
                var all = File.ReadAllLines(HostsPath);
                var kept = all.Where(l => !l.Contains(Marker, StringComparison.Ordinal)).ToArray();
                if (kept.Length != all.Length)
                {
                    File.WriteAllLines(HostsPath, kept);
                    FlushDns();
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Переключить: on = Apply, off = Remove. Возвращает результат.</summary>
        public static bool Set(bool enable) => enable ? Apply() : Remove();

        private static List<string> ReadHostsWithoutBlock()
        {
            if (!File.Exists(HostsPath)) return new List<string>();
            return File.ReadAllLines(HostsPath)
                .Where(l => !l.Contains(Marker, StringComparison.Ordinal))
                .ToList();
        }

        private static void FlushDns()
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                p?.WaitForExit(3000);
            }
            catch { }
        }

        /// <summary>Запущен ли процесс от имени администратора.</summary>
        public static bool IsAdministrator()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
