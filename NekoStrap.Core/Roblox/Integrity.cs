using System.Text.Json;

namespace NekoStrap.Roblox
{
    /// <summary>Найденная проблема: путь (относительно версии) и её вид.</summary>
    public sealed record IntegrityIssue(string Path, string Kind);

    /// <summary>Итог проверки одной версии клиента.</summary>
    public sealed class IntegrityReport
    {
        public string VersionGuid { get; init; } = "";
        public bool HasBaseline { get; init; }
        public int Checked { get; init; }
        public List<IntegrityIssue> Issues { get; init; } = new();
        public bool Ok => Issues.Count == 0;
    }

    /// <summary>
    /// Целостность клиента. Два уровня:
    ///  — обязательно: exe и AppSettings.xml на месте, файлы не нулевые;
    ///  — снимок (nekostrap.integrity.json): имя+размер каждого файла на момент
    ///    установки — его пишем сразу после распаковки, пока клиент гарантированно
    ///    свежий. Сравнение по размеру ловит и удалённые, и подмёненные файлы,
    ///    при этом не гоняет хэши по десяткам тысяч файлов.
    /// </summary>
    internal static class Integrity
    {
        public const string BaselineName = "nekostrap.integrity.json";

        public const string KindMissing = "missing";
        public const string KindEmpty = "empty";
        public const string KindSize = "size";

        private sealed class Baseline
        {
            public List<BFile> Files { get; set; } = new();
        }

        private sealed class BFile
        {
            public string Path { get; set; } = "";
            public long Size { get; set; }
        }

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public static string BaselinePath(string versionDir) =>
            Path.Combine(versionDir, BaselineName);

        // ---------- Снимок ----------

        /// <summary>Снимок файлов версии. Пишется в момент чистой установки.</summary>
        public static void WriteBaseline(string versionDir)
        {
            try
            {
                var b = new Baseline();
                foreach (var f in Directory.EnumerateFiles(versionDir, "*", SearchOption.AllDirectories))
                {
                    if (f.EndsWith(BaselineName, StringComparison.OrdinalIgnoreCase)) continue;
                    b.Files.Add(new BFile
                    {
                        Path = Path.GetRelativePath(versionDir, f).Replace('\\', '/'),
                        Size = new FileInfo(f).Length
                    });
                }
                File.WriteAllText(BaselinePath(versionDir), JsonSerializer.Serialize(b, JsonOpts));
            }
            catch { /* снимок вторичен: без него проверка деградует до структурной */ }
        }

        /// <summary>Снимок есть — не перезаписываем. Пишем только у «чистой» версии:
        /// структура цела и моды в неё не влезали (иначе размеры не эталон).</summary>
        public static void EnsureBaseline(string versionDir)
        {
            try
            {
                if (File.Exists(BaselinePath(versionDir))) return;
                if (ModManager.HasAppliedFiles(versionDir)) return;
                if (!StructuralOk(versionDir)) return;
                WriteBaseline(versionDir);
            }
            catch { /* ignore */ }
        }

        private static Baseline? LoadBaseline(string versionDir)
        {
            try
            {
                string path = BaselinePath(versionDir);
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<Baseline>(File.ReadAllText(path));
            }
            catch { return null; }
        }

        // ---------- Проверка ----------

        /// <summary>Минимум, без которого клиент вообще не стартует.</summary>
        private static string[] RequiredFiles(string versionDir) => new[]
        {
            Path.Combine(versionDir, RobloxPaths.PlayerExe),
            Path.Combine(versionDir, "AppSettings.xml")
        };

        public static bool StructuralOk(string versionDir)
        {
            try
            {
                if (!Directory.Exists(versionDir)) return false;
                foreach (var f in RequiredFiles(versionDir))
                    if (!File.Exists(f)) return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Проверка версии: структура + снимок (если он есть).</summary>
        public static IntegrityReport Check(string versionDir)
        {
            string guid = Path.GetFileName(versionDir);
            if (!Directory.Exists(versionDir))
            {
                return new IntegrityReport { VersionGuid = guid };
            }

            var issues = new List<IntegrityIssue>();
            foreach (var f in RequiredFiles(versionDir))
                if (!File.Exists(f))
                    issues.Add(new IntegrityIssue(
                        Path.GetRelativePath(versionDir, f).Replace('\\', '/'), KindMissing));

            int checkedCount = 0;
            var baseline = LoadBaseline(versionDir);
            // Правленные модами файлы — не эталон: их размеры осознанно другие.
            var applied = ModManager.AppliedPaths(versionDir);
            bool Skip(string path) => applied.Contains(path.Replace('\\', '/'));

            if (baseline != null)
            {
                foreach (var e in baseline.Files)
                {
                    string p = Path.Combine(versionDir, e.Path.Replace('/', Path.DirectorySeparatorChar));
                    checkedCount++;
                    if (Skip(e.Path)) continue;
                    if (!File.Exists(p))
                        issues.Add(new IntegrityIssue(e.Path, KindMissing));
                    else
                    {
                        long len = new FileInfo(p).Length;
                        if (len != e.Size)
                            issues.Add(new IntegrityIssue(e.Path, KindSize));
                    }
                }
            }
            else
            {
                // Снимка нет (клиент ставили старой версией лаунчера) —
                // бережно ищем хотя бы пустые файлы.
                try
                {
                    foreach (var f in Directory.EnumerateFiles(versionDir, "*", SearchOption.AllDirectories))
                    {
                        if (f.EndsWith(BaselineName, StringComparison.OrdinalIgnoreCase)) continue;
                        checkedCount++;
                        if (new FileInfo(f).Length == 0)
                            issues.Add(new IntegrityIssue(
                                Path.GetRelativePath(versionDir, f).Replace('\\', '/'), KindEmpty));
                    }
                }
                catch { /* каталог могли закрыть — тогда останется только структура */ }
            }

            return new IntegrityReport
            {
                VersionGuid = guid,
                HasBaseline = baseline != null,
                Checked = checkedCount,
                Issues = issues
            };
        }
    }
}
