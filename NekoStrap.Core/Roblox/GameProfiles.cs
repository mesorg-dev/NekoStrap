using System.Text.Json;

namespace NekoStrap.Roblox
{
    /// <summary>Привязка «игра → свои профили» (моды и/или флаги).</summary>
    internal sealed class GameBinding
    {
        public long PlaceId { get; set; }
        public string GameName { get; set; } = "";
        public string ModProfile { get; set; } = "";
        public string FlagProfile { get; set; } = "";
    }

    /// <summary>
    /// Авто-профили на игру: при запуске плейса подставляем привязанные ему
    /// профили модов и флагов. Каждая привязка знает только своё место —
    /// пустое поле означает «этот вид профилей не трогаем».
    /// </summary>
    internal static class GameProfiles
    {
        public static string FilePath => Path.Combine(RobloxPaths.BaseDir, "game_profiles.json");

        public static List<GameBinding> List()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<List<GameBinding>>(File.ReadAllText(FilePath))
                           ?? new List<GameBinding>();
            }
            catch { /* битый файл — начинаем с пустого */ }
            return new List<GameBinding>();
        }

        public static GameBinding? Get(long placeId) =>
            List().FirstOrDefault(b => b.PlaceId == placeId);

        /// <summary>Ставит/обновляет привязку. Совсем пустая привязка удаляется.</summary>
        public static void Set(GameBinding binding)
        {
            if (binding.PlaceId <= 0) return;
            var list = List();
            list.RemoveAll(b => b.PlaceId == binding.PlaceId);
            if (binding.ModProfile.Length > 0 || binding.FlagProfile.Length > 0)
                list.Add(binding);
            Save(list);
        }

        public static void Remove(long placeId)
        {
            var list = List();
            if (list.RemoveAll(b => b.PlaceId == placeId) > 0) Save(list);
        }

        public static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch { /* ignore */ }
        }

        private static void Save(List<GameBinding> list)
        {
            Directory.CreateDirectory(RobloxPaths.BaseDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list,
                new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
