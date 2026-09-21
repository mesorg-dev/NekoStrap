using System.Text.Json;

namespace NekoStrap.Roblox
{
    /// <summary>
    /// Сеть Roblox: проверка версии, манифест пакетов, скачивание.
    /// Протокол — как в Bloxstrap/Voidstrap (MIT): только официальный CDN
    /// clientsettings.roblox.com + setup.rbxcdn.com, никаких инъекций и обходов.
    /// </summary>
    internal static class Deployment
    {
        public const string VersionApiHost = "https://clientsettings.roblox.com";
        public const string CdnHost = "https://setup.rbxcdn.com";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        static Deployment()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("NekoStrap/1.0");
        }

        public sealed record ClientVersionInfo(string Version, string VersionGuid);

        public static Task<ClientVersionInfo> GetLatestPlayerVersionAsync(CancellationToken ct = default)
        {
            return GetLatestVersionAsync("WindowsPlayer", ct);
        }

        public static Task<ClientVersionInfo> GetLatestStudioVersionAsync(CancellationToken ct = default)
        {
            return GetLatestVersionAsync("WindowsStudio64", ct);
        }

        public static async Task<ClientVersionInfo> GetLatestVersionAsync(string binaryType, CancellationToken ct = default)
        {
            using var res = await Http.GetAsync($"{VersionApiHost}/v2/client-version/{binaryType}", ct);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            string version = root.GetProperty("version").GetString() ?? "";
            string guid = root.GetProperty("clientVersionUpload").GetString() ?? "";
            if (version.Length == 0 || !RobloxPaths.IsVersionGuid(guid))
                throw new InvalidDataException("bad version api response");
            return new ClientVersionInfo(version, guid);
        }

        public static Task<string> DownloadManifestAsync(string versionGuid, CancellationToken ct = default)
        {
            return Http.GetStringAsync($"{CdnHost}/{versionGuid}-rbxPkgManifest.txt", ct);
        }

        public static string PackageUrl(string versionGuid, string packageName)
        {
            return $"{CdnHost}/{versionGuid}-{packageName}";
        }

        /// <summary>Качает файл потоково с прогрессом (doneBytes, totalBytes|-1).</summary>
        public static async Task DownloadPackageAsync(
            string url, string destPath,
            IProgress<(long done, long total)>? progress,
            CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? ".");
            using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            res.EnsureSuccessStatusCode();
            long total = res.Content.Headers.ContentLength ?? -1;
            await using var net = await res.Content.ReadAsStreamAsync(ct);
            await using var file = new FileStream(destPath, FileMode.Create, FileAccess.Write,
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
        }
    }
}
