using System.Diagnostics;

namespace NekoStrap.Roblox
{
    /// <summary>
    /// Запуск клиента: exe установленной версии со своей рабочей папкой.
    /// Без аргументов открывается обычный плеер (логин/каталог игр).
    /// </summary>
    internal static class RobloxLauncher
    {
        public static string? GetExePath(string? versionGuid)
        {
            if (versionGuid == null || !RobloxPaths.IsVersionGuid(versionGuid)) return null;
            string exe = Path.Combine(RobloxPaths.VersionsDir, versionGuid, RobloxPaths.PlayerExe);
            return File.Exists(exe) ? exe : null;
        }

        public static Process? Launch(string exePath)
        {
            try
            {
                var psi = new ProcessStartInfo(exePath)
                {
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                    UseShellExecute = false
                };
                return Process.Start(psi);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Ссылка вида roblox://placeId=... — её открывает тот, кто записан
        /// в реестре (часто Voidstrap). Реестр не трогаем: передаём ссылку
        /// напрямую НАШЕМУ exe как аргумент — плеер штатно так запускается.
        /// </summary>
        public static string BuildPlaceUrl(long placeId)
        {
            return $"roblox://placeId={placeId}";
        }

        /// <summary>
        /// Ссылка на КОНКРЕТНЫЙ сервер: вышел случайно, а там были крутые
        /// типы — заходишь обратно к ним, а не на рандомный сервер.
        /// Формат gameInstanceId понимает официальный плеер (так же
        /// перезаходит Bloxstrap по кнопке «Rejoin»).
        /// </summary>
        public static string BuildServerUrl(long placeId, string jobId)
        {
            if (jobId.Length == 0) return BuildPlaceUrl(placeId);
            return $"roblox://placeId={placeId}&gameInstanceId={jobId}";
        }

        /// <summary>Веб-ссылка на страницу плейса (поделиться / открыть в браузере).</summary>
        public static string BuildWebUrl(long placeId)
        {
            return $"https://www.roblox.com/games/{placeId}";
        }

        public static Process? LaunchPlace(string exePath, long placeId)
        {
            try
            {
                var psi = new ProcessStartInfo(exePath, BuildPlaceUrl(placeId))
                {
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                    UseShellExecute = false
                };
                return Process.Start(psi);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Перезаход на тот же сервер по JobId. Без JobId — обычный заход на плейс.
        /// </summary>
        public static Process? LaunchServer(string exePath, long placeId, string jobId)
        {
            try
            {
                var psi = new ProcessStartInfo(exePath, BuildServerUrl(placeId, jobId))
                {
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                    UseShellExecute = false
                };
                return Process.Start(psi);
            }
            catch
            {
                return null;
            }
        }

        public static void LaunchStudio(string exePath)
        {
            var psi = new ProcessStartInfo(exePath)
            {
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                UseShellExecute = false
            };
            Process.Start(psi);
        }
    }
}
