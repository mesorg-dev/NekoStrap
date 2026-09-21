namespace NekoStrap.Roblox
{
    internal sealed record RobloxPackage(string Name, string Signature, long PackedSize, long Size);

    /// <summary>
    /// Парсер rbxPkgManifest: первая строка "v0", дальше четвёрки
    /// (имя, сигнатура, сжатый размер, размер). Exe-установщики пропускаем —
    /// качаем только контентные зипы (подходит и плееру, и Studio).
    /// </summary>
    internal static class PackageManifest
    {
        public static List<RobloxPackage> Parse(string text)
        {
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (lines.Length == 0 || lines[0].Trim() != "v0")
                throw new InvalidDataException("bad manifest header");
            var list = new List<RobloxPackage>();
            for (int i = 1; i + 3 < lines.Length; i += 4)
            {
                string name = lines[i].Trim();
                if (name.Length == 0) continue;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 3 >= lines.Length) break;
                list.Add(new RobloxPackage(
                    name,
                    lines[i + 1].Trim(),
                    long.Parse(lines[i + 2].Trim()),
                    long.Parse(lines[i + 3].Trim())));
            }
            if (list.Count == 0)
                throw new InvalidDataException("empty manifest");
            return list;
        }
    }
}
