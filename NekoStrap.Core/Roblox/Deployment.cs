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

        // Общий клиент API (UA уже настроен) — им пользуются и поиск игр.
        internal static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        // Отдельный клиент на файлы: таймаут 30 с рвал бы большие пакеты
        // (он действует на всё тело ответа), а параллельным закачкам нужно
        // больше соединений к CDN.
        private static readonly HttpClient FileHttp = new(new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 16,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        })
        { Timeout = Timeout.InfiniteTimeSpan };

        static Deployment()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("NekoStrap/1.0");
            FileHttp.DefaultRequestHeaders.UserAgent.ParseAdd("NekoStrap/1.0");
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

        /// <summary>
        /// Иконка плейса с официального thumbnails API — ею рисуем картинку
        /// в Discord-присутствии. Пустая строка, если API молчит.
        /// </summary>
        public static async Task<string> GetPlaceIconAsync(long placeId, CancellationToken ct = default)
        {
            if (placeId <= 0) return "";
            string url = "https://thumbnails.roblox.com/v1/places/gameicons" +
                $"?placeIds={placeId}&returnPolicy=PlaceHolder&size=512x512&format=Png&isCircular=false";
            using var res = await Http.GetAsync(url, ct);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
                return "";
            var first = data[0];
            if (first.TryGetProperty("imageUrl", out var img) && img.ValueKind == JsonValueKind.String)
                return img.GetString() ?? "";
            return "";
        }

        public static string PackageUrl(string versionGuid, string packageName)
        {
            return $"{CdnHost}/{versionGuid}-{packageName}";
        }

        /// <summary>
        /// Качает файл потоково с прогрессом (doneBytes, totalBytes|-1).
        /// expectedSize &gt; 0 включает возобновление: целый лежащий файл не
        /// трогаем, недокачанный докачиваем через Range (сервер без поддержки
        /// Range ответит 200 — начнём заново).
        /// </summary>
        public static async Task DownloadPackageAsync(
            string url, string destPath,
            IProgress<(long done, long total)>? progress,
            long expectedSize,
            CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? ".");

            long existing = 0;
            try { if (File.Exists(destPath)) existing = new FileInfo(destPath).Length; }
            catch { existing = 0; }

            if (expectedSize > 0 && existing == expectedSize)
            {
                progress?.Report((existing, expectedSize));
                return;
            }
            // Возобновляем только когда размер известен и хвост не больше файла.
            if (expectedSize <= 0 || existing >= expectedSize) existing = 0;

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (existing > 0)
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);

            using var res = await FileHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            res.EnsureSuccessStatusCode();

            bool append = false;
            if (existing > 0)
            {
                var range = res.Content.Headers.ContentRange;
                append = res.StatusCode == System.Net.HttpStatusCode.PartialContent
                         && range != null
                         && range.From == existing
                         && (range.Length == null || expectedSize <= 0 || range.Length == expectedSize);
            }
            if (!append) existing = 0;

            long total = expectedSize > 0
                ? expectedSize
                : existing + Math.Max(0, res.Content.Headers.ContentLength ?? -1);

            await using var net = await res.Content.ReadAsStreamAsync(ct);
            await using var file = new FileStream(destPath,
                append ? FileMode.Append : FileMode.Create,
                FileAccess.Write, FileShare.None, 81920, useAsync: true);
            if (existing > 0) progress?.Report((existing, total));

            var buf = new byte[81920];
            long done = existing;
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
