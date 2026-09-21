using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace NekoStrap.Roblox
{
    /// <summary>
    /// Фикс загрузки картинок и аватаров Roblox для РФ: прописывает рабочий IP
    /// CDN (tr/t0-t7/c0-c7.rbxcdn.com) в hosts, после — flush DNS и чистка
    /// кэша ассетов. Порт батников Fix/Revert_Roblox_CDN, но аккуратнее:
    /// удаляются ТОЛЬКО наши строки (их findstr с пробелами сносил бы вообще
    /// все комментарии), бэкап лежит у нас в BaseDir.
    /// </summary>
    internal static class CdnFix
    {
        public const string FixIp = "78.110.36.16";
        public const string Header = "# Roblox CDN Fix (NekoStrap)";

        public static readonly string[] Hosts =
        {
            "tr.rbxcdn.com",
            "t0.rbxcdn.com", "t1.rbxcdn.com", "t2.rbxcdn.com", "t3.rbxcdn.com",
            "t4.rbxcdn.com", "t5.rbxcdn.com", "t6.rbxcdn.com", "t7.rbxcdn.com",
            "c0.rbxcdn.com", "c1.rbxcdn.com", "c2.rbxcdn.com", "c3.rbxcdn.com",
            "c4.rbxcdn.com", "c5.rbxcdn.com", "c6.rbxcdn.com", "c7.rbxcdn.com",
        };

        public static string SystemHostsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

        public static string BackupPath => Path.Combine(RobloxPaths.BaseDir, "hosts.backup");

        public static bool IsAdmin()
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static bool IsApplied(string? hostsPath = null)
        {
            try
            {
                string text = File.ReadAllText(hostsPath ?? SystemHostsPath, Encoding.UTF8);
                return text.Contains(Header, StringComparison.Ordinal) ||
                       text.Contains($"{FixIp} tr.rbxcdn.com", StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static bool IsOurLine(string line)
        {
            string t = line.Trim();
            if (t.Equals(Header, StringComparison.Ordinal)) return true;
            foreach (var h in Hosts)
            {
                if (t.Equals($"{FixIp} {h}", StringComparison.OrdinalIgnoreCase) ||
                    t.StartsWith(FixIp + " ", StringComparison.OrdinalIgnoreCase) &&
                    t.Contains(h, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string StripOurLines(string text)
        {
            var kept = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Where(l => !IsOurLine(l));
            return string.Join(Environment.NewLine, kept).TrimEnd() + Environment.NewLine;
        }

        private static string BuildBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine(Header);
            foreach (var h in Hosts)
                sb.AppendLine($"{FixIp} {h}");
            return sb.ToString();
        }

        /// <summary>Включить фикс. Возвращает человекочитаемый итог.</summary>
        public static string Apply(string? hostsPath = null, string? backupPath = null)
        {
            string hp = hostsPath ?? SystemHostsPath;
            string bp = backupPath ?? BackupPath;
            string original = File.ReadAllText(hp, Encoding.UTF8);

            if (!File.Exists(bp))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(bp) ?? ".");
                File.WriteAllText(bp, original, new UTF8Encoding(false));
            }

            string cleaned = StripOurLines(original);
            File.WriteAllText(hp, cleaned + BuildBlock(), new UTF8Encoding(false));

            string dns = FlushDns();
            int cleared = ClearAssetCaches();
            return $"Фикс включён ({Hosts.Length} хостов → {FixIp}). DNS: {dns}. Кэш: {cleared} папок.";
        }

        /// <summary>Откат: восстановить бэкап (или просто вычистить наши строки).</summary>
        public static string Rollback(string? hostsPath = null, string? backupPath = null)
        {
            string hp = hostsPath ?? SystemHostsPath;
            string bp = backupPath ?? BackupPath;

            if (File.Exists(bp))
                File.Copy(bp, hp, overwrite: true);

            // На случай, если бэкап сам содержал фикс (или его нет) — чистим точно.
            string current = File.ReadAllText(hp, Encoding.UTF8);
            File.WriteAllText(hp, StripOurLines(current), new UTF8Encoding(false));

            string dns = FlushDns();
            int cleared = ClearAssetCaches();
            return $"Фикс откачен, hosts восстановлен. DNS: {dns}. Кэш: {cleared} папок.";
        }

        public static string FlushDns()
        {
            return FlushDnsWith("ipconfig");
        }

        internal static string FlushDnsWith(string tool)
        {
            try
            {
                var psi = new ProcessStartInfo(tool, "/flushdns")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                if (p == null) return "не запустился";
                p.WaitForExit(15000);
                return p.ExitCode == 0 ? "сброшен" : $"код {p.ExitCode}";
            }
            catch (Exception ex)
            {
                return "ошибка: " + ex.Message;
            }
        }

        public static int ClearAssetCaches()
        {
            return ClearAssetCaches(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Roblox", "Downloads"),
                Path.Combine(Path.GetTempPath(), "Roblox"));
        }

        internal static int ClearAssetCaches(string downloadsDir, string tempRobloxDir)
        {
            int n = 0;
            foreach (var dir in new[] { downloadsDir, tempRobloxDir })
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, true);
                        n++;
                    }
                }
                catch { /* занято — пропустим */ }
            }
            return n;
        }
    }
}
