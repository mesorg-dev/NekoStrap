using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace NekoStrap.Roblox
{
    public sealed record ModEntry(string RelativePath, long Size, bool Active);

    /// <summary>Строка manifest: что накладывали и был ли в клиенте оригинал.</summary>
    internal sealed class AppliedMod
    {
        public string Path { get; set; } = "";
        public bool HadOriginal { get; set; }
    }

    /// <summary>Manifest правок одной версии: отпечаток профиля + список файлов.</summary>
    internal sealed class ModManifest
    {
        public string Signature { get; set; } = "";
        public List<AppliedMod> Files { get; set; } = new();
    }

    /// <summary>
    /// Моды-профили: ModProfiles\&lt;имя&gt;\ зеркалит клиент (как Modifications
    /// в Bloxstrap/Voidstrap — относительный путь = путь внутри клиента).
    /// Профиль = инстанс набора модов: свой пак текстур, звуки, шрифты.
    /// Активный профиль применяется при установке и перед каждым запуском;
    /// перед применением прошлые правки откатываются по manifest.json с бэкапами
    /// в ModState\&lt;version&gt;\ — поэтому смена профиля всегда даёт чистый
    /// клиент плюс новый набор, а не кашу из двух наборов.
    /// *.lock и *.disabled игнорируются/отключаются. Старые файлы из Mods\
    /// переезжают в профиль Default при первом обращении.
    /// </summary>
    internal static class ModManager
    {
        public const string DefaultProfile = "Default";

        private const string InvalidNameChars = "<>:\"/|?*";

        private static readonly object Sync = new();
        private static string _baseDir = "";
        private static bool _laidOut;
        private static string _active = "";

        /// <summary>Кто активен: { "Active": "Имя" }.</summary>
        public static string StateFile => Path.Combine(RobloxPaths.BaseDir, "mod_profiles.json");

        // ================= Профили =================

        /// <summary>Имя активного профиля.</summary>
        public static string ActiveProfile()
        {
            lock (Sync)
            {
                EnsureLayout();
                return _active;
            }
        }

        /// <summary>Папка активного профиля (зеркало клиента).</summary>
        public static string ActiveDir()
        {
            lock (Sync)
            {
                EnsureLayout();
                return ProfileDir(_active);
            }
        }

        /// <summary>Все профили по алфавиту. Пустых не бывает — Default создаётся.</summary>
        public static List<string> Profiles()
        {
            lock (Sync)
            {
                EnsureLayout();
                return ProfilesLocked();
            }
        }

        public static string ProfileDir(string name) =>
            Path.Combine(RobloxPaths.ModProfilesDir, FileSafeName(name));

        /// <summary>Имя профиля допустимо: не пусто, до 64 символов, без мусора.</summary>
        public static bool ValidName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string n = name.Trim();
            if (n.Length > 64 || n is "." or "..") return false;
            foreach (char c in n)
                if (c < 32 || InvalidNameChars.IndexOf(c) >= 0) return false;
            return true;
        }

        /// <summary>Новый профиль; copyActive = дубликат активного. false = уже есть.</summary>
        public static bool Create(string name, bool copyActive)
        {
            if (!ValidName(name)) return false;
            lock (Sync)
            {
                EnsureLayout();
                string dir = ProfileDir(name);
                if (Directory.Exists(dir)) return false;
                Directory.CreateDirectory(dir);
                if (copyActive) CopyTree(ProfileDir(_active), dir);
                return true;
            }
        }

        /// <summary>
        /// Удаление профиля. Нельзя удалить единственный — после удаления
        /// активный перескакивает на первый оставшийся.
        /// </summary>
        public static bool Delete(string name)
        {
            lock (Sync)
            {
                EnsureLayout();
                string dir = ProfileDir(name);
                if (!Directory.Exists(dir) || ProfilesLocked().Count <= 1) return false;
                Directory.Delete(dir, true);
                if (string.Equals(Path.GetFileName(dir), _active, StringComparison.OrdinalIgnoreCase))
                {
                    var rest = ProfilesLocked();
                    _active = rest.Count > 0 ? rest[0] : DefaultProfile;
                    Directory.CreateDirectory(ProfileDir(_active));
                    WriteActive(_active);
                }
                return true;
            }
        }

        /// <summary>Сделать профиль активным (файлы накладываются при ближайшем применении).</summary>
        public static bool SetActive(string name)
        {
            lock (Sync)
            {
                EnsureLayout();
                string dir = ProfileDir(name);
                if (!Directory.Exists(dir)) return false;
                _active = Path.GetFileName(dir);
                WriteActive(_active);
                return true;
            }
        }

        /// <summary>Открыть папку активного профиля в проводнике.</summary>
        public static void OpenActiveFolder()
        {
            string dir = ActiveDir();
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"")
            {
                UseShellExecute = true
            });
        }

        // ================= Экспорт/импорт (zip) =================

        /// <summary>
        /// Профиль → zip: корень архива = зеркало клиента. Такой файл можно
        /// кинуть другу — он импортирует и получит ровно тот же набор.
        /// </summary>
        public static void ExportProfile(string name, string zipPath)
        {
            lock (Sync)
            {
                EnsureLayout();
                string dir = ProfileDir(name);
                if (!Directory.Exists(dir))
                    throw new DirectoryNotFoundException(name);
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(dir, zipPath, CompressionLevel.Optimal, false);
            }
        }

        /// <summary>Zip → новый профиль. Возвращает имя созданного профиля.</summary>
        public static string ImportProfile(string zipPath)
        {
            lock (Sync)
            {
                EnsureLayout();
                if (!File.Exists(zipPath))
                    throw new FileNotFoundException(zipPath, zipPath);

                string baseName = SanitizeName(Path.GetFileNameWithoutExtension(zipPath));
                if (baseName.Length == 0) baseName = "Imported";
                string name = baseName;
                for (int i = 2; Directory.Exists(ProfileDir(name)); i++)
                    name = $"{baseName} ({i})";

                string dest = ProfileDir(name);
                Directory.CreateDirectory(dest);
                int files = 0;
                try
                {
                    using var archive = ZipFile.OpenRead(zipPath);
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.Length == 0 || entry.Name.Length == 0) continue;
                        string? target = SafePath(dest, entry.FullName);
                        if (target == null) continue; // путь наружу — пропускаем
                        Directory.CreateDirectory(Path.GetDirectoryName(target) ?? dest);
                        entry.ExtractToFile(target, overwrite: true);
                        files++;
                    }
                }
                catch
                {
                    try { Directory.Delete(dest, true); } catch { /* ignore */ }
                    throw;
                }
                if (files == 0)
                {
                    try { Directory.Delete(dest, true); } catch { /* ignore */ }
                    throw new InvalidDataException("empty zip");
                }
                return name;
            }
        }

        /// <summary>Имя файла архива → допустимое имя профиля.</summary>
        private static string SanitizeName(string raw)
        {
            var sb = new StringBuilder();
            foreach (char c in raw.Trim())
                if (c >= 32 && InvalidNameChars.IndexOf(c) < 0) sb.Append(c);
            string n = sb.ToString().Trim().TrimEnd('.');
            return n.Length > 64 ? n.Substring(0, 64) : n;
        }

        // ================= Файлы активного профиля =================

        public static List<ModEntry> List()
        {
            lock (Sync)
            {
                EnsureLayout();
                string dir = ProfileDir(_active);
                var list = new List<ModEntry>();
                try
                {
                    foreach (var f in EnumerateModFiles(dir))
                    {
                        bool active = !Path.GetFileName(f)
                            .EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                        list.Add(new ModEntry(
                            Path.GetRelativePath(dir, f),
                            new FileInfo(f).Length, active));
                    }
                }
                catch { /* ignore */ }
                return list.OrderBy(m => m.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        /// <summary>Кладёт файлы в корень активного профиля (имя = путь в клиенте).</summary>
        public static void AddFiles(string[] sourcePaths)
        {
            lock (Sync)
            {
                EnsureLayout();
                string dir = ProfileDir(_active);
                Directory.CreateDirectory(dir);
                foreach (var src in sourcePaths)
                {
                    if (!File.Exists(src)) continue;
                    string dest = Path.Combine(dir, Path.GetFileName(src));
                    File.Copy(src, dest, overwrite: true);
                }
            }
        }

        /// <summary>Вкл/выкл файл мода (.disabled). false = файла нет.</summary>
        public static bool Toggle(string relativePath)
        {
            lock (Sync)
            {
                EnsureLayout();
                string? full = SafePath(ProfileDir(_active), relativePath);
                if (full == null || !File.Exists(full)) return false;
                if (full.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    File.Move(full, full[..^".disabled".Length], overwrite: true);
                else
                    File.Move(full, full + ".disabled", overwrite: true);
                return true;
            }
        }

        /// <summary>Удаляет файлы из активного профиля. Возвращает, сколько удалило.</summary>
        public static int DeleteFiles(IEnumerable<string> relativePaths)
        {
            lock (Sync)
            {
                EnsureLayout();
                string root = ProfileDir(_active);
                int n = 0;
                foreach (var rel in relativePaths)
                {
                    string? full = SafePath(root, rel);
                    if (full == null || !File.Exists(full)) continue;
                    File.Delete(full);
                    n++;
                }
                return n;
            }
        }

        // ================= Применение к установке =================

        /// <summary>
        /// Откат прошлых правок этой версии + наложение активного профиля.
        /// Возвращает число наложенных файлов. Вызывается при установке и
        /// перед каждым запуском — профиль меняется мгновенно и без сюрпризов.
        /// Если профиль и клиентские файлы не менялись с прошлого раза,
        /// ничего не трогает: пак в сотни мегабайт не копируется зря.
        /// </summary>
        public static int ApplyTo(string versionDir)
        {
            lock (Sync)
            {
                EnsureLayout();
                if (string.IsNullOrWhiteSpace(versionDir) || !Directory.Exists(versionDir))
                {
                    PruneStates();
                    return 0;
                }

                string src = ProfileDir(_active);
                var manifest = LoadManifest(versionDir);
                string signature = Signature(src);
                if (manifest.Files.Count > 0 &&
                    string.Equals(manifest.Signature, signature, StringComparison.Ordinal) &&
                    StillInPlace(src, versionDir, manifest))
                {
                    PruneStates();
                    return manifest.Files.Count;
                }

                Restore(versionDir, manifest);

                var applied = new List<AppliedMod>();
                foreach (var f in EnumerateModFiles(src))
                {
                    string rel = Path.GetRelativePath(src, f);
                    string? dest = SafePath(versionDir, rel);
                    if (dest == null) continue;
                    try
                    {
                        bool hadOriginal = File.Exists(dest);
                        if (hadOriginal)
                        {
                            // Первый бэкап — эталон. Повторные наложения его
                            // не затирают: в клиент могли влезть посторонние.
                            string bak = Path.Combine(StateDir(versionDir), "backup", rel);
                            if (!File.Exists(bak))
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(bak) ?? ".");
                                File.Copy(dest, bak, overwrite: true);
                            }
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? ".");
                        File.Copy(f, dest, overwrite: true);
                        applied.Add(new AppliedMod { Path = rel, HadOriginal = hadOriginal });
                    }
                    catch { /* файл занят — заберёмся при следующем запуске */ }
                }

                if (applied.Count > 0)
                {
                    SaveState(versionDir, new ModManifest { Signature = signature, Files = applied });
                    PruneBackups(versionDir, applied);
                }
                else
                {
                    ClearState(versionDir);
                }
                PruneStates();
                return applied.Count;
            }
        }

        /// <summary>
        /// Только откат наложенного (ничего не накладываем): чистый запуск —
        /// клиент остаётся ванильным, пока не вернём профиль обратно.
        /// </summary>
        public static int RevertTo(string versionDir)
        {
            lock (Sync)
            {
                EnsureLayout();
                if (string.IsNullOrWhiteSpace(versionDir) || !Directory.Exists(versionDir))
                    return 0;
                var manifest = LoadManifest(versionDir);
                if (manifest.Files.Count == 0)
                {
                    PruneStates();
                    return 0;
                }
                int restored = Restore(versionDir, manifest);
                ClearState(versionDir);
                PruneStates();
                return restored;
            }
        }

        // ================= Безопасные пути =================

        /// <summary>Полный путь внутри root или null, если путь выходит наружу.</summary>
        internal static string? SafePath(string root, string relativePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(relativePath))
                    return null;
                string rel = relativePath.Trim().Replace('/', '\\');
                if (rel.Length == 0 || rel.Contains(':') || Path.IsPathRooted(rel))
                    return null;
                string fullRoot = Path.GetFullPath(root).TrimEnd('\\');
                string full = Path.GetFullPath(Path.Combine(fullRoot, rel));
                if (!full.StartsWith(fullRoot + "\\", StringComparison.OrdinalIgnoreCase))
                    return null;
                return full;
            }
            catch { return null; }
        }

        // ================= Внутреннее =================

        private static void EnsureLayout()
        {
            if (_laidOut && _baseDir == RobloxPaths.BaseDir) return;
            _baseDir = RobloxPaths.BaseDir;
            _laidOut = true;
            _active = "";
            try
            {
                Directory.CreateDirectory(RobloxPaths.ModProfilesDir);
                MigrateLegacy();
                if (ProfilesLocked().Count == 0)
                    Directory.CreateDirectory(ProfileDir(DefaultProfile));
                string want = ReadActive();
                _active = Directory.Exists(ProfileDir(want)) ? want : DefaultProfile;
                Directory.CreateDirectory(ProfileDir(_active));
                WriteActive(_active);
            }
            catch
            {
                _active = DefaultProfile;
            }
        }

        /// <summary>Старые Mods\ целиком становятся профилем Default.</summary>
        private static void MigrateLegacy()
        {
            string legacy = RobloxPaths.ModsDir;
            if (!Directory.Exists(legacy)) return;
            if (Directory.EnumerateFileSystemEntries(legacy).Any())
            {
                string target = ProfileDir(DefaultProfile);
                if (!Directory.Exists(target))
                {
                    Directory.Move(legacy, target);
                }
                else
                {
                    foreach (var entry in Directory.GetFileSystemEntries(legacy))
                    {
                        string dest = Path.Combine(target, Path.GetFileName(entry));
                        try
                        {
                            if (Directory.Exists(entry))
                            {
                                if (!Directory.Exists(dest)) Directory.Move(entry, dest);
                                else CopyTree(entry, dest);
                            }
                            else if (!File.Exists(dest))
                            {
                                File.Move(entry, dest);
                            }
                        }
                        catch { /* не мигрировалось — не беда */ }
                    }
                }
            }
            try
            {
                if (Directory.Exists(legacy) && !Directory.EnumerateFileSystemEntries(legacy).Any())
                    Directory.Delete(legacy, recursive: false);
            }
            catch { /* ignore */ }
        }

        private static List<string> ProfilesLocked()
        {
            try
            {
                if (!Directory.Exists(RobloxPaths.ModProfilesDir)) return new();
                return Directory.GetDirectories(RobloxPaths.ModProfilesDir)
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { return new(); }
        }

        private static void CopyTree(string from, string to)
        {
            if (!Directory.Exists(from)) return;
            Directory.CreateDirectory(to);
            foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string dest = Path.Combine(to, Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? ".");
                File.Copy(file, dest, overwrite: true);
            }
        }

        private static string FileSafeName(string name)
        {
            name = (name ?? "").Trim();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(c < 32 || InvalidNameChars.IndexOf(c) >= 0 ? '_' : c);
            string s = sb.ToString().Trim().TrimEnd('.', ' ');
            return s.Length == 0 ? DefaultProfile : s;
        }

        private static string ReadActive()
        {
            try
            {
                if (!File.Exists(StateFile)) return DefaultProfile;
                using var doc = JsonDocument.Parse(File.ReadAllText(StateFile));
                string? v = doc.RootElement.TryGetProperty("Active", out var p)
                    && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                return string.IsNullOrWhiteSpace(v) ? DefaultProfile : v!.Trim();
            }
            catch { return DefaultProfile; }
        }

        private static void WriteActive(string name)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StateFile) ?? ".");
                var data = new Dictionary<string, string> { ["Active"] = name };
                File.WriteAllText(StateFile, JsonSerializer.Serialize(data,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* ignore */ }
        }

        // ---- manifest правок на конкретную версию ----

        private static string StateDir(string versionDir)
        {
            string key = Path.GetFileName(Path.GetFullPath(versionDir));
            if (!RobloxPaths.IsVersionGuid(key)) key = FileSafeName(key);
            return Path.Combine(RobloxPaths.ModStateDir, key);
        }

        /// <summary>
        /// Читает manifest. Понимает и старый формат (просто список файлов) —
        /// тогда подпись пустая, применение пройдёт заново с откатом.
        /// </summary>
        private static ModManifest LoadManifest(string versionDir)
        {
            try
            {
                string file = Path.Combine(StateDir(versionDir), "manifest.json");
                if (!File.Exists(file)) return new();
                string json = File.ReadAllText(file);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                if (json.TrimStart().StartsWith('['))
                    return new ModManifest { Files = JsonSerializer.Deserialize<List<AppliedMod>>(json, opts) ?? new() };
                return JsonSerializer.Deserialize<ModManifest>(json, opts) ?? new();
            }
            catch { return new(); }
        }

        /// <summary>
        /// Относительные пути, которые правили модами (в снапшот целостности
        /// их размеры не входят — там лежит не оригинал, а файл профиля).
        /// </summary>
        public static HashSet<string> AppliedPaths(string versionDir)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (Sync)
            {
                foreach (var m in LoadManifest(versionDir).Files)
                    set.Add(m.Path.Replace('\\', '/'));
            }
            return set;
        }

        /// <summary>Есть ли на версию применённые правки (значит — не эталон).</summary>
        public static bool HasAppliedFiles(string versionDir)
        {
            lock (Sync) return LoadManifest(versionDir).Files.Count > 0;
        }

        private static void SaveState(string versionDir, ModManifest manifest)
        {
            try
            {
                string dir = StateDir(versionDir);
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "manifest.json");
                File.WriteAllText(file, JsonSerializer.Serialize(manifest,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* ignore */ }
        }

        private static void ClearState(string versionDir)
        {
            try
            {
                string dir = StateDir(versionDir);
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch { /* ignore */ }
        }

        /// <summary>Только manifest: бэкапы оригиналов остаются на месте.</summary>
        private static void ClearManifest(string versionDir)
        {
            try
            {
                string file = Path.Combine(StateDir(versionDir), "manifest.json");
                if (File.Exists(file)) File.Delete(file);
            }
            catch { /* ignore */ }
        }

        /// <summary>Убираем бэкапы тех файлов, что больше не накладываются.</summary>
        private static void PruneBackups(string versionDir, List<AppliedMod> applied)
        {
            try
            {
                string bakRoot = Path.Combine(StateDir(versionDir), "backup");
                if (!Directory.Exists(bakRoot)) return;
                var keep = new HashSet<string>(
                    applied.Where(a => a.HadOriginal).Select(a => a.Path),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var f in Directory.GetFiles(bakRoot, "*", SearchOption.AllDirectories))
                {
                    if (keep.Contains(Path.GetRelativePath(bakRoot, f))) continue;
                    try { File.Delete(f); } catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }
        }

        /// <summary>Возвращает клиент к ванили: наши файлы убираем, бэкапы — на место.</summary>
        private static int Restore(string versionDir, ModManifest manifest)
        {
            if (manifest.Files.Count == 0) return 0;
            string stateDir = StateDir(versionDir);
            int n = 0;
            foreach (var e in manifest.Files)
            {
                string? dest = SafePath(versionDir, e.Path);
                if (dest == null) continue;
                try
                {
                    if (e.HadOriginal)
                    {
                        string bak = Path.Combine(stateDir, "backup", e.Path);
                        if (!File.Exists(bak)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? ".");
                        File.Copy(bak, dest, overwrite: true);
                        n++;
                    }
                    else if (File.Exists(dest))
                    {
                        File.Delete(dest);
                        n++;
                    }
                }
                catch { /* ignore */ }
            }
            // Манифест чистим, бэкапы оставляем: эталоны оригиналов однажды
            // снятые с этой версии больше никогда не понадобятся переснимать.
            ClearManifest(versionDir);
            return n;
        }

        /// <summary>Файлы профиля, которые реально накладываются (без .lock/.disabled).</summary>
        private static IEnumerable<string> EnumerateModFiles(string dir)
        {
            if (!Directory.Exists(dir)) yield break;
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(f);
                if (name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    continue;
                yield return f;
            }
        }

        /// <summary>
        /// Отпечаток профиля: пути + размеры + время правки. Меняется хоть один
        /// байт — подпись другая, моды накладываются заново.
        /// </summary>
        private static string Signature(string dir)
        {
            var sb = new StringBuilder();
            try
            {
                foreach (var f in EnumerateModFiles(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var fi = new FileInfo(f);
                    sb.Append(Path.GetRelativePath(dir, f)).Append('|')
                      .Append(fi.Length).Append('|')
                      .Append(fi.LastWriteTimeUtc.Ticks).Append('\n');
                }
            }
            catch { /* ignore */ }
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        /// <summary>Всё, что накладывали, всё ещё лежит в клиенте и не тронуто.</summary>
        private static bool StillInPlace(string profileDir, string versionDir, ModManifest manifest)
        {
            foreach (var e in manifest.Files)
            {
                string? src = SafePath(profileDir, e.Path);
                string? dest = SafePath(versionDir, e.Path);
                if (src == null || dest == null) return false;
                try
                {
                    if (!File.Exists(src) || !File.Exists(dest)) return false;
                    var sfi = new FileInfo(src);
                    var dfi = new FileInfo(dest);
                    if (sfi.Length != dfi.Length) return false;
                    // File.Copy сохраняет время правки — значит, любая порча
                    // файла в клиенте видна даже при совпадении размера.
                    if (sfi.LastWriteTimeUtc != dfi.LastWriteTimeUtc) return false;
                }
                catch { return false; }
            }
            return true;
        }

        /// <summary>Сносим состояние версий, которых больше нет в Versions\.</summary>
        private static void PruneStates()
        {
            try
            {
                if (!Directory.Exists(RobloxPaths.ModStateDir)) return;
                foreach (var d in Directory.GetDirectories(RobloxPaths.ModStateDir))
                {
                    string name = Path.GetFileName(d);
                    if (!RobloxPaths.IsVersionGuid(name) ||
                        !Directory.Exists(Path.Combine(RobloxPaths.VersionsDir, name)))
                    {
                        try { Directory.Delete(d, recursive: true); } catch { /* ignore */ }
                    }
                }
            }
            catch { /* ignore */ }
        }
    }
}
