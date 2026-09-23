using System.Text.Json;

namespace NekoStrap.Roblox
{
    /// <summary>
    /// Профили FastFlags: именованные наборы флагов («FPS», «Красиво»)
    /// с переключением в один клик. Хранятся в flag_profiles.json рядом
    /// с конфигом: { "Profiles": { "FPS": { "Flag": "Value" } } }.
    /// </summary>
    internal static class FlagProfiles
    {
        public static string FilePath => Path.Combine(RobloxPaths.BaseDir, "flag_profiles.json");

        public static Dictionary<string, Dictionary<string, string>> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new(StringComparer.OrdinalIgnoreCase);
                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                if (!doc.RootElement.TryGetProperty("Profiles", out var arr)) return result;
                foreach (var p in arr.EnumerateObject())
                {
                    string name = p.Name.Trim();
                    if (name.Length == 0) continue;
                    var flags = new Dictionary<string, string>();
                    if (p.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var f in p.Value.EnumerateObject())
                        {
                            string fn = f.Name.Trim();
                            if (fn.Length == 0) continue;
                            flags[fn] = f.Value.ValueKind == JsonValueKind.String
                                ? f.Value.GetString() ?? ""
                                : f.Value.GetRawText();
                        }
                    }
                    result[name] = flags;
                }
                return result;
            }
            catch { /* битый файл — начинаем с пустого */ }
            return new(StringComparer.OrdinalIgnoreCase);
        }

        public static List<string> List()
        {
            return Load().Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static Dictionary<string, string>? Get(string name)
        {
            var all = Load();
            return all.TryGetValue(name.Trim(), out var flags) ? flags : null;
        }

        public static void SaveProfile(string name, Dictionary<string, string> flags)
        {
            name = name.Trim();
            if (name.Length == 0) return;
            var all = Load();
            all[name] = new Dictionary<string, string>(flags);
            SaveAll(all);
        }

        public static void Delete(string name)
        {
            var all = Load();
            if (all.Remove(name.Trim()))
                SaveAll(all);
        }

        private static void SaveAll(Dictionary<string, Dictionary<string, string>> all)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? ".");
                using var ms = new MemoryStream();
                using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteStartObject("Profiles");
                    foreach (var (name, flags) in all.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        w.WriteStartObject(name);
                        foreach (var (k, v) in flags.OrderBy(x => x.Key))
                        {
                            w.WritePropertyName(k);
                            FastFlagStore.WriteValue(w, v);
                        }
                        w.WriteEndObject();
                    }
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
                File.WriteAllBytes(FilePath, ms.ToArray());
            }
            catch { /* ignore */ }
        }
    }
}
