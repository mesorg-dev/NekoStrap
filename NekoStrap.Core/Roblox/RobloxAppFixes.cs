using System.Text.Json.Nodes;

namespace NekoStrap.Roblox
{
    /// <summary>
    /// Фиксы поведения официального клиента через его же хранилище
    /// (%LocalAppData%\Roblox\LocalStorage\appStorage.json) — как в Voidstrap:
    /// запрет сворачивания Roblox в трей и его автозапуска с Windows.
    /// </summary>
    internal static class RobloxAppFixes
    {
        public static string Location => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox", "LocalStorage", "appStorage.json");

        public static void Apply(bool noTray, bool noStartup)
        {
            try
            {
                JsonObject data;
                if (File.Exists(Location))
                {
                    try { data = JsonNode.Parse(File.ReadAllText(Location)) as JsonObject ?? new JsonObject(); }
                    catch { data = new JsonObject(); }
                }
                else
                {
                    data = new JsonObject();
                }
                data["MinimizeToTray"] = noTray ? "false" : "true";
                data["LaunchAtStartup"] = noStartup ? "false" : "true";
                Directory.CreateDirectory(Path.GetDirectoryName(Location) ?? ".");
                File.WriteAllText(Location, data.ToJsonString(
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* чужой конфиг не смогли — не критично */ }
        }

        public static (bool noTray, bool noStartup) Read()
        {
            try
            {
                if (!File.Exists(Location)) return (false, false);
                var data = JsonNode.Parse(File.ReadAllText(Location)) as JsonObject;
                if (data == null) return (false, false);
                return (data["MinimizeToTray"]?.GetValue<string>() == "false",
                        data["LaunchAtStartup"]?.GetValue<string>() == "false");
            }
            catch { return (false, false); }
        }
    }
}
