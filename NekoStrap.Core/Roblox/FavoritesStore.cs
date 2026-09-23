using System.Text.Json;

namespace NekoStrap.Roblox
{
    /// <summary>Избранный плейс: быстрый запуск с Главной в один клик.</summary>
    public sealed class FavoritePlace
    {
        public long PlaceId { get; set; }
        public string Name { get; set; } = "";
        public DateTime AddedAt { get; set; }

        public string Display =>
            Name.Length > 0 ? Name : "Place " + PlaceId;
    }

    /// <summary>
    /// Избранные плейсы (favorites.json рядом с конфигом).
    /// Добавляются из Истории, запускаются с Главной.
    /// </summary>
    internal static class FavoritesStore
    {
        public static string FilePath => Path.Combine(RobloxPaths.BaseDir, "favorites.json");

        public static List<FavoritePlace> Load()
        {
            var list = new List<FavoritePlace>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                if (!doc.RootElement.TryGetProperty("Favorites", out var arr)) return list;
                foreach (var e in arr.EnumerateArray())
                {
                    long id = 0;
                    if (e.TryGetProperty("PlaceId", out var p))
                        p.TryGetInt64(out id);
                    if (id <= 0) continue;
                    string name = e.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                    DateTime added = DateTime.MinValue;
                    if (e.TryGetProperty("AddedAt", out var a))
                        DateTime.TryParse(a.GetString(), out added);
                    list.Add(new FavoritePlace { PlaceId = id, Name = name, AddedAt = added });
                }
            }
            catch { /* битый файл — начинаем с пустого */ }
            return list.OrderBy(f => f.Display, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static bool Contains(long placeId)
        {
            return Load().Any(f => f.PlaceId == placeId);
        }

        public static void Add(long placeId, string name)
        {
            if (placeId <= 0) return;
            var list = Load();
            var existing = list.FirstOrDefault(f => f.PlaceId == placeId);
            if (existing != null)
            {
                if (name.Length > 0) existing.Name = name;
            }
            else
            {
                list.Add(new FavoritePlace
                {
                    PlaceId = placeId,
                    Name = name,
                    AddedAt = DateTime.UtcNow
                });
            }
            Save(list);
        }

        public static void Remove(long placeId)
        {
            var list = Load();
            int n = list.RemoveAll(f => f.PlaceId == placeId);
            if (n > 0) Save(list);
        }

        private static void Save(List<FavoritePlace> list)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? ".");
                using var ms = new MemoryStream();
                using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteStartArray("Favorites");
                    foreach (var f in list.OrderBy(x => x.Display, StringComparer.OrdinalIgnoreCase))
                    {
                        w.WriteStartObject();
                        w.WriteNumber("PlaceId", f.PlaceId);
                        w.WriteString("Name", f.Name);
                        w.WriteString("AddedAt", f.AddedAt.ToString("o"));
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                File.WriteAllBytes(FilePath, ms.ToArray());
            }
            catch { /* ignore */ }
        }
    }
}
