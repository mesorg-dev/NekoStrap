using System.Text.Json;

namespace NekoStrap.Roblox
{
    /// <summary>
    /// Чтение/запись ClientAppSettings.json установленной версии.
    /// Значения храним строками; при сохранении восстанавливаем типы
    /// (bool/число/строка) — иначе Roblox не поймёт флаги.
    /// </summary>
    internal static class FastFlagStore
    {
        public static string? GetFilePath(string? installedGuid)
        {
            if (installedGuid == null || !RobloxPaths.IsVersionGuid(installedGuid)) return null;
            return Path.Combine(RobloxPaths.VersionsDir, installedGuid,
                "ClientSettings", "ClientAppSettings.json");
        }

        public static Dictionary<string, string> Load(string? installedGuid)
        {
            var result = new Dictionary<string, string>();
            try
            {
                string? path = GetFilePath(installedGuid);
                if (path == null || !File.Exists(path)) return result;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    result[p.Name] = p.Value.ValueKind == JsonValueKind.String
                        ? p.Value.GetString() ?? ""
                        : p.Value.GetRawText();
                }
            }
            catch { /* битый json — отдаём пустое */ }
            return result;
        }

        public static void Save(string? installedGuid, Dictionary<string, string> flags)
        {
            string? path = GetFilePath(installedGuid);
            if (path == null)
                throw new InvalidOperationException("Roblox не установлен — флаги некуда сохранять");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                foreach (var (k, v) in flags)
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    w.WritePropertyName(k.Trim());
                    WriteValue(w, v.Trim());
                }
                w.WriteEndObject();
            }
            File.WriteAllBytes(path, ms.ToArray());
        }

        public static void WriteValue(Utf8JsonWriter w, string v)
        {
            if (bool.TryParse(v, out bool b))
                w.WriteBooleanValue(b);
            else if (long.TryParse(v, out long l))
                w.WriteNumberValue(l);
            else if (double.TryParse(v, System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture, out double d))
                w.WriteNumberValue(d);
            else
                w.WriteStringValue(v);
        }
    }
}
