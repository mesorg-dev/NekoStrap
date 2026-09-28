using System.Text.Json;

namespace NekoStrap.Roblox
{
    /// <summary>
    /// Счётчик наигранного времени: общее + по плейсам.
    /// Хранится в playtime.json, пишется по сессиям (смена плейса/выход из игры).
    /// </summary>
    internal sealed class PlaytimeStore
    {
        public sealed class GameTime
        {
            public string Name { get; set; } = "";
            public long Seconds { get; set; }
            public DateTime LastPlayed { get; set; }
        }

        public long TotalSeconds { get; private set; }
        public Dictionary<string, GameTime> Games { get; private set; } = new();

        public static string FilePath => Path.Combine(RobloxPaths.BaseDir, "playtime.json");

        public static PlaytimeStore Load()
        {
            var store = new PlaytimeStore();
            try
            {
                if (!File.Exists(FilePath)) return store;
                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                var r = doc.RootElement;
                if (r.TryGetProperty("TotalSeconds", out var t)) store.TotalSeconds = t.GetInt64();
                if (r.TryGetProperty("Games", out var g))
                {
                    foreach (var p in g.EnumerateObject())
                    {
                        store.Games[p.Name] = new GameTime
                        {
                            Name = p.Value.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
                            Seconds = p.Value.TryGetProperty("Seconds", out var s) ? s.GetInt64() : 0,
                            LastPlayed = p.Value.TryGetProperty("LastPlayed", out var l) &&
                                DateTime.TryParse(l.GetString(), out var dt) ? dt : default
                        };
                    }
                }
            }
            catch { /* битый файл — с нуля */ }
            store.MergeDuplicates();
            return store;
        }

        public void Add(long placeId, string name, long seconds)
        {
            if (seconds <= 0) return;
            name = name.Trim();
            TotalSeconds += seconds;
            string key = placeId > 0 ? placeId.ToString() : "_";
            Games.TryGetValue(key, out var g);
            // Одна игра может жить на нескольких placeId (телепорты внутри
            // экспириенса) — склеиваем записи с одинаковым названием, чтобы
            // в истории не было строк «Evade 1 ч» и «Evade 5 мин».
            if (name.Length > 0 && (g == null || g.Name.Length == 0 ||
                string.Equals(g.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            {
                string? dupKey = FindDuplicateKey(name, key);
                if (dupKey != null)
                {
                    var dup = Games[dupKey];
                    Games.Remove(dupKey);
                    if (g == null)
                    {
                        g = dup;
                        Games[key] = g;
                    }
                    else
                    {
                        g.Seconds += dup.Seconds;
                        if (dup.LastPlayed > g.LastPlayed) g.LastPlayed = dup.LastPlayed;
                    }
                }
            }
            if (g == null)
            {
                g = new GameTime();
                Games[key] = g;
            }
            g.Seconds += seconds;
            if (name.Length > 0) g.Name = name;
            g.LastPlayed = DateTime.UtcNow;
            Save();
        }

        /// <summary>Ключ записи с тем же названием (не считая текущей).</summary>
        private string? FindDuplicateKey(string name, string skipKey)
        {
            if (name.Length == 0) return null;
            foreach (var kv in Games)
            {
                if (kv.Key == skipKey) continue;
                if (string.Equals((kv.Value.Name ?? "").Trim(), name,
                        StringComparison.OrdinalIgnoreCase))
                    return kv.Key;
            }
            return null;
        }

        /// <summary>Схлопнуть уже накопленные дубли (старые файлы playtime.json).</summary>
        private void MergeDuplicates()
        {
            bool merged = false;
            var groups = Games
                .GroupBy(kv => (kv.Value.Name ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Key.Length > 0);
            foreach (var group in groups)
            {
                var list = group.ToList();
                if (list.Count < 2) continue;
                var target = list
                    .OrderByDescending(x => x.Value.LastPlayed)
                    .ThenByDescending(x => x.Value.Seconds)
                    .First().Value;
                foreach (var kv in list)
                {
                    if (ReferenceEquals(kv.Value, target)) continue;
                    target.Seconds += kv.Value.Seconds;
                    if (kv.Value.LastPlayed > target.LastPlayed)
                        target.LastPlayed = kv.Value.LastPlayed;
                    Games.Remove(kv.Key);
                    merged = true;
                }
                target.Name = target.Name.Trim();
            }
            if (merged) Save();
        }

        public long ForPlace(long placeId)
        {
            return Games.TryGetValue(placeId.ToString(), out var g) ? g.Seconds : 0;
        }

        public void Remove(long placeId)
        {
            try
            {
                if (Games.Remove(placeId.ToString())) Save();
            }
            catch { /* ignore */ }
        }

        public void Clear()
        {
            try
            {
                TotalSeconds = 0;
                Games.Clear();
                Save();
            }
            catch { /* ignore */ }
        }

        public void Save()
        {
            try
            {
                using var ms = new MemoryStream();
                using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteNumber("TotalSeconds", TotalSeconds);
                    w.WriteStartObject("Games");
                    foreach (var (k, g) in Games)
                    {
                        w.WriteStartObject(k);
                        w.WriteString("Name", g.Name);
                        w.WriteNumber("Seconds", g.Seconds);
                        w.WriteString("LastPlayed", g.LastPlayed.ToString("o"));
                        w.WriteEndObject();
                    }
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? ".");
                File.WriteAllBytes(FilePath, ms.ToArray());
            }
            catch { /* ignore */ }
        }

        public static string Format(long seconds) => Format(seconds, "меньше минуты", "мин", "ч");

        /// <summary>То же с явными единицами (для локализации UI).</summary>
        public static string Format(long seconds, string lessMinute, string minUnit, string hourUnit)
        {
            if (seconds < 60) return lessMinute;
            if (seconds < 3600) return $"{seconds / 60} {minUnit}";
            long h = seconds / 3600;
            long m = seconds % 3600 / 60;
            return m > 0 ? $"{h} {hourUnit} {m} {minUnit}" : $"{h} {hourUnit}";
        }
    }
}
