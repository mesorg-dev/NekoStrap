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
            return store;
        }

        public void Add(long placeId, string name, long seconds)
        {
            if (seconds <= 0) return;
            TotalSeconds += seconds;
            string key = placeId > 0 ? placeId.ToString() : "_";
            if (!Games.TryGetValue(key, out var g))
            {
                g = new GameTime();
                Games[key] = g;
            }
            g.Seconds += seconds;
            if (name.Length > 0) g.Name = name;
            g.LastPlayed = DateTime.UtcNow;
            Save();
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
