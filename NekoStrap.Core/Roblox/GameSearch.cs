using System.Text.Json;

namespace NekoStrap.Roblox
{
    /// <summary>Одна игра из результатов поиска.</summary>
    public sealed record GameHit(long PlaceId, long UniverseId, string Name, string Creator, int Players);

    /// <summary>
    /// Поиск игр по живому API Roblox — omni-search, тот же, что ищет на сайте
    /// (старый games/v1/games/list отдаёт 404). Парсер терпимый: лишние поля
    /// игнорируются, выпавшие — считаются нулевыми, поэтому смена формата
    /// урезает результат, а не роняет лаунчер. 429 = перелёт с лимитом:
    /// запрос делаем только по кнопке/Enter, не на каждое нажатие клавиши.
    /// </summary>
    internal static class GameSearch
    {
        private const string SearchEndpoint = "https://apis.roblox.com/search-api/omni-search";
        private const string IconsEndpoint = "https://thumbnails.roblox.com/v1/games/icons";

        public const int MaxResults = 8;

        public static async Task<List<GameHit>> SearchAsync(string query, CancellationToken ct = default)
        {
            var list = new List<GameHit>();
            query = (query ?? "").Trim();
            if (query.Length == 0) return list;

            // sessionId обязателен и в формате uuid — без него API отдаёт 429.
            string url = SearchEndpoint
                + "?searchQuery=" + Uri.EscapeDataString(query)
                + "&sessionId=" + Guid.NewGuid()
                + "&pageType=all";

            using var res = await Deployment.Http.GetAsync(url, ct);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return Parse(doc.RootElement);
        }

        /// <summary>Разбор корня ответа omni-search (вынесен — его гоняет дымовой тест).</summary>
        internal static List<GameHit> Parse(JsonElement root)
        {
            var list = new List<GameHit>();
            if (!root.TryGetProperty("searchResults", out var groups) ||
                groups.ValueKind != JsonValueKind.Array)
                return list;

            var seen = new HashSet<long>();
            foreach (var group in groups.EnumerateArray())
            {
                if (!group.TryGetProperty("contents", out var contents) ||
                    contents.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var item in contents.EnumerateArray())
                {
                    long placeId = PlaceIdOf(item);
                    if (placeId <= 0 || !seen.Add(placeId)) continue;
                    list.Add(new GameHit(
                        placeId,
                        Long(item, "universeId"),
                        Str(item, "name"),
                        Str(item, "creatorName"),
                        Int(item, "playerCount")));
                    if (list.Count >= MaxResults) return list;
                }
            }
            return list;
        }

        /// <summary>
        /// Иконки игр по universeId одним запросом. Ключ — universeId;
        /// отсутствующие картинки просто не попадают в словарь.
        /// </summary>
        public static async Task<Dictionary<long, string>> GetIconsAsync(
            IReadOnlyCollection<long> universeIds, CancellationToken ct = default)
        {
            var map = new Dictionary<long, string>();
            var ids = universeIds.Where(id => id > 0).Distinct().Take(20).ToList();
            if (ids.Count == 0) return map;

            string url = IconsEndpoint + "?universeIds=" + string.Join(",", ids)
                + "&size=150x150&format=Png&isCircular=false";
            using var res = await Deployment.Http.GetAsync(url, ct);
            res.EnsureSuccessStatusCode();
            string json = await res.Content.ReadAsStringAsync(ct);
            return ParseIcons(json);
        }

        /// <summary>Разбор ответа thumbnails (его тоже гоняет дымовой тест).</summary>
        internal static Dictionary<long, string> ParseIcons(string json)
        {
            var map = new Dictionary<long, string>();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
                return map;
            foreach (var item in data.EnumerateArray())
            {
                long id = Long(item, "targetId");
                string icon = Str(item, "imageUrl");
                if (id > 0 && icon.Length > 0) map[id] = icon;
            }
            return map;
        }

        // ---------- разбор ----------

        private static long PlaceIdOf(JsonElement item)
        {
            if (item.TryGetProperty("rootPlaceId", out var p) &&
                p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out long id))
                return id;
            // Фолбэк: часть объектов отдаёт только канонический путь /games/<id>/...
            if (item.TryGetProperty("canonicalUrlPath", out var path) &&
                path.ValueKind == JsonValueKind.String)
            {
                string s = path.GetString() ?? "";
                int i = s.IndexOf("/games/", StringComparison.Ordinal);
                if (i >= 0)
                {
                    int start = i + "/games/".Length;
                    int end = start;
                    while (end < s.Length && char.IsDigit(s[end])) end++;
                    if (end > start && long.TryParse(s[start..end], out long parsed))
                        return parsed;
                }
            }
            return 0;
        }

        private static string Str(JsonElement item, string prop)
        {
            return item.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? ""
                : "";
        }

        private static long Long(JsonElement item, string prop)
        {
            return item.TryGetProperty(prop, out var v) &&
                   v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : 0;
        }

        private static int Int(JsonElement item, string prop)
        {
            return item.TryGetProperty(prop, out var v) &&
                   v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 0;
        }
    }
}
