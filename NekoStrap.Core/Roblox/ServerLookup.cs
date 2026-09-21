using System.Net.NetworkInformation;
using System.Text.Json;

namespace NekoStrap.Roblox
{
    public sealed record GeoInfo(string City, string Region, string Country, double Lat, double Lon);

    public sealed record PingResult(int Ms, bool Estimated);

    /// <summary>
    /// Гео IP сервера (ipinfo.io → запасной ipapi.co, оба без ключа) и пинг.
    /// </summary>
    internal static class ServerLookup
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly Dictionary<string, GeoInfo> GeoCache = new();
        private static readonly Dictionary<long, string> NameCache = new();
        private static GeoInfo? _selfGeo;

        static ServerLookup()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("NekoStrap/1.0");
        }

        public static async Task<GeoInfo?> GetGeoAsync(string ip)
        {
            if (ip.Length == 0) return null;
            lock (GeoCache)
            {
                if (GeoCache.TryGetValue(ip, out var cached)) return cached;
            }
            var geo = await TryIpInfo(ip) ?? await TryIpApiCo(ip);
            if (geo != null)
            {
                lock (GeoCache) GeoCache[ip] = geo;
            }
            return geo;
        }

        private static async Task<GeoInfo?> TryIpInfo(string ip)
        {
            try
            {
                using var doc = JsonDocument.Parse(
                    await Http.GetStringAsync($"https://ipinfo.io/{ip}/json"));
                var r = doc.RootElement;
                ParseLoc(Get(r, "loc"), out double lat, out double lon);
                return new GeoInfo(Get(r, "city"), Get(r, "region"), Get(r, "country"), lat, lon);
            }
            catch { return null; }
        }

        private static async Task<GeoInfo?> TryIpApiCo(string ip)
        {
            try
            {
                using var doc = JsonDocument.Parse(
                    await Http.GetStringAsync($"https://ipapi.co/{ip}/json/"));
                var r = doc.RootElement;
                return new GeoInfo(
                    Get(r, "city"), Get(r, "region"), Get(r, "country_name"),
                    GetDouble(r, "latitude"), GetDouble(r, "longitude"));
            }
            catch { return null; }
        }

        private static string Get(JsonElement r, string name)
        {
            if (!r.TryGetProperty(name, out var v)) return "";
            return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        }

        private static double GetDouble(JsonElement r, string name)
        {
            try
            {
                if (r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
                    return v.GetDouble();
            }
            catch { /* ignore */ }
            return double.NaN;
        }

        private static void ParseLoc(string loc, out double lat, out double lon)
        {
            lat = double.NaN;
            lon = double.NaN;
            try
            {
                var parts = loc.Split(',');
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double la) &&
                    double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double lo))
                {
                    lat = la;
                    lon = lo;
                }
            }
            catch { /* ignore */ }
        }

        /// <summary>Где я сам (для оценки дистанции). Кэшируется навсегда.</summary>
        public static async Task<GeoInfo?> GetSelfGeoAsync()
        {
            if (_selfGeo != null) return _selfGeo;
            _selfGeo = await TryIpInfoSelf() ?? await TryIpApiCoSelf();
            return _selfGeo;
        }

        private static async Task<GeoInfo?> TryIpInfoSelf()
        {
            try
            {
                using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://ipinfo.io/json"));
                var r = doc.RootElement;
                ParseLoc(Get(r, "loc"), out double lat, out double lon);
                return new GeoInfo(Get(r, "city"), Get(r, "region"), Get(r, "country"), lat, lon);
            }
            catch { return null; }
        }

        private static async Task<GeoInfo?> TryIpApiCoSelf()
        {
            try
            {
                using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://ipapi.co/json/"));
                var r = doc.RootElement;
                return new GeoInfo(
                    Get(r, "city"), Get(r, "region"), Get(r, "country_name"),
                    GetDouble(r, "latitude"), GetDouble(r, "longitude"));
            }
            catch { return null; }
        }

        /// <summary>Гаверсинус — км между точками. Как в VoidstrapMatchmaker.</summary>
        public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            static double ToRad(double d) => d * Math.PI / 180.0;
            double dLat = ToRad(lat2 - lat1);
            double dLon = ToRad(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 6371.0 * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        /// <summary>Оценка пинга по дистанции — формула Voidstrap: 5 + км/75.</summary>
        public static int EstimatePingMs(double distanceKm)
        {
            if (double.IsNaN(distanceKm) || distanceKm < 0.0) return -1;
            return Math.Clamp((int)Math.Round(5.0 + distanceKm / 75.0), 1, 999);
        }

        public static async Task<int> PingAsync(string ip)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 1500);
                return reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : -1;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Пинг сервера по-честному:
        /// 1) медиана ICMP (как у Voidstrap),
        /// 2) медиана TCP до 443 (игровые порты DPI может RST-шить мгновенно — врёт),
        /// 3) оценка по дистанции формулой Voidstrap (помечается Estimated).
        /// Каждый замер проверяется физикой: свет в оптоволокне идёт ~200км/мс,
        /// так что пинг ниже distance/150 невозможен — это отвечает кто-то
        /// локальный (антивирус, VPN-клиент, DPI), такие образцы отбрасываем.
        /// </summary>
        public static async Task<PingResult> PingGameAsync(string ip, int port)
        {
            if (ip.Length == 0) return new PingResult(-1, false);

            double floor = await PlausibilityFloorForAsync(ip);

            var icmp = await MedianValidAsync(3, async () => await PingAsync(ip), floor);
            if (icmp >= 0) return new PingResult(icmp, false);

            var tcp = await MedianValidAsync(3, async () => await TcpPingAsync(ip, 443), floor);
            if (tcp >= 0) return new PingResult(tcp, false);

            int est = await EstimateToServerAsync(ip);
            return new PingResult(est, est >= 0);
        }

        /// <summary>
        /// Минимально возможный пинг по дистанции. NaN (гео неизвестно) — 1 мс.
        /// </summary>
        public static double PlausibilityFloorMs(double distanceKm)
        {
            if (double.IsNaN(distanceKm) || distanceKm < 0.0) return 1.0;
            return Math.Max(2.0, distanceKm / 150.0);
        }

        private static async Task<double> PlausibilityFloorForAsync(string ip)
        {
            try
            {
                var server = await GetGeoAsync(ip);
                var me = await GetSelfGeoAsync();
                if (server == null || me == null) return 1.0;
                if (double.IsNaN(server.Lat) || double.IsNaN(server.Lon) ||
                    double.IsNaN(me.Lat) || double.IsNaN(me.Lon))
                    return 1.0;
                return PlausibilityFloorMs(HaversineKm(me.Lat, me.Lon, server.Lat, server.Lon));
            }
            catch { return 1.0; }
        }

        private static async Task<int> MedianValidAsync(int tries, Func<Task<int>> probe, double floor)
        {
            var samples = new List<int>();
            for (int i = 0; i < tries; i++)
            {
                int v = await probe();
                if (v >= floor) samples.Add(v);
                if (i + 1 < tries)
                {
                    try { await Task.Delay(150); } catch { /* ignore */ }
                }
            }
            if (samples.Count == 0) return -1;
            samples.Sort();
            return samples[samples.Count / 2];
        }

        private static async Task<int> MedianAsync(int tries, Func<Task<int>> probe)
        {
            return await MedianValidAsync(tries, probe, 1.0);
        }

        private static async Task<int> EstimateToServerAsync(string ip)
        {
            try
            {
                var server = await GetGeoAsync(ip);
                var me = await GetSelfGeoAsync();
                if (server == null || me == null) return -1;
                if (double.IsNaN(server.Lat) || double.IsNaN(server.Lon) ||
                    double.IsNaN(me.Lat) || double.IsNaN(me.Lon))
                    return -1;
                return EstimatePingMs(HaversineKm(me.Lat, me.Lon, server.Lat, server.Lon));
            }
            catch { return -1; }
        }

        public static async Task<int> TcpPingAsync(string ip, int port)
        {
            if (ip.Length == 0 || port <= 0 || port > 65535) return -1;
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    await client.ConnectAsync(ip, port).WaitAsync(TimeSpan.FromMilliseconds(1500));
                }
                catch { /* отказ/таймаут — время всё равно замерено */ }
                sw.Stop();
                return sw.ElapsedMilliseconds < 1450 ? (int)sw.ElapsedMilliseconds : -1;
            }
            catch { return -1; }
        }

        /// <summary>Название игры по placeId: place → universe → games API.</summary>
        public static async Task<string?> GetPlaceNameAsync(long placeId)
        {
            if (placeId <= 0) return null;
            lock (NameCache)
            {
                if (NameCache.TryGetValue(placeId, out var cached)) return cached;
            }
            try
            {
                using var u = JsonDocument.Parse(await Http.GetStringAsync(
                    $"https://apis.roblox.com/universes/v1/places/{placeId}/universe"));
                long universeId = u.RootElement.GetProperty("universeId").GetInt64();
                using var g = JsonDocument.Parse(await Http.GetStringAsync(
                    $"https://games.roblox.com/v1/games?universeIds={universeId}"));
                string? name = g.RootElement.GetProperty("data")[0].GetProperty("name").GetString();
                if (!string.IsNullOrEmpty(name))
                {
                    lock (NameCache) NameCache[placeId] = name;
                    return name;
                }
            }
            catch { /* ignore */ }
            return null;
        }
    }
}
