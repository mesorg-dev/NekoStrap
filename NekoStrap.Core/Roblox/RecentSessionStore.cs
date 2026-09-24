using System.Text.Json;

namespace NekoStrap.Roblox
{
    /// <summary>
    /// Один заход в игру. Именно по этой записи можно вернуться:
    /// PlaceId — на тот же плейс, JobId — на ТОТ ЖЕ сервер
    /// (вышел случайно, а там были крутые типы — жмёшь «перезайти»).
    /// </summary>
    public sealed class RecentSession
    {
        public string JobId { get; set; } = "";
        public long PlaceId { get; set; }
        public string Name { get; set; } = "";
        public string ServerIp { get; set; } = "";
        public int ServerPort { get; set; }
        public DateTime JoinedAt { get; set; }
        public DateTime? LeftAt { get; set; }

        public string DisplayName =>
            Name.Length > 0 ? Name
            : PlaceId > 0 ? "Place " + PlaceId
            : "Roblox";

        /// <summary>Короткий JobId для таблицы (первые 8 символов).</summary>
        public string ShortJob =>
            JobId.Length >= 8 ? JobId[..8] + "…" : JobId.Length > 0 ? JobId : "—";

        public TimeSpan Duration =>
            (LeftAt ?? DateTime.UtcNow) - JoinedAt;
    }

    /// <summary>
    /// Лог последних заходов (recent_sessions.json рядом с playtime.json).
    /// Начало пишется СРАЗУ при смене сессии — даже если лаунчер/игра
    /// упадут, запись останется и туда можно будет перезайти.
    /// Держим максимум 50 свежих.
    /// </summary>
    internal sealed class RecentSessionStore
    {
        private const int MaxSessions = 50;

        private readonly object _lock = new();
        private List<RecentSession> _sessions = new();

        public static string FilePath => Path.Combine(RobloxPaths.BaseDir, "recent_sessions.json");

        public static RecentSessionStore Load()
        {
            var store = new RecentSessionStore();
            try
            {
                if (!File.Exists(FilePath)) return store;
                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                if (!doc.RootElement.TryGetProperty("Sessions", out var arr)) return store;
                var list = new List<RecentSession>();
                foreach (var e in arr.EnumerateArray())
                {
                    var s = new RecentSession
                    {
                        JobId = GetStr(e, "JobId"),
                        PlaceId = GetLong(e, "PlaceId"),
                        Name = GetStr(e, "Name"),
                        ServerIp = GetStr(e, "ServerIp"),
                        ServerPort = (int)GetLong(e, "ServerPort"),
                        JoinedAt = GetDate(e, "JoinedAt"),
                        LeftAt = GetDateOrNull(e, "LeftAt"),
                    };
                    if (s.JoinedAt == default && s.PlaceId <= 0 && s.JobId.Length == 0)
                        continue;
                    if (s.JoinedAt == default) s.JoinedAt = DateTime.UtcNow;
                    list.Add(s);
                }
                list.Sort((a, b) => b.JoinedAt.CompareTo(a.JoinedAt));
                if (list.Count > MaxSessions) list.RemoveRange(MaxSessions, list.Count - MaxSessions);
                store._sessions = list;
            }
            catch { /* битый файл — начинаем с чистого */ }
            return store;
        }

        private static string GetStr(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? "" : "";

        private static long GetLong(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n)
                ? n : 0;

        private static DateTime GetDate(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(v.GetString(), out var dt) ? dt : default;

        private static DateTime? GetDateOrNull(JsonElement e, string name)
        {
            var dt = GetDate(e, name);
            return dt == default ? null : dt;
        }

        public List<RecentSession> List()
        {
            lock (_lock) return new List<RecentSession>(_sessions);
        }

        /// <summary>
        /// Начало захода. Если прилетел UDMUX-добор по тому же JobId —
        /// просто дописываем IP/порт в открытую запись, дубли не плодим.
        /// </summary>
        public void Begin(long placeId, string jobId, string ip, int port)
        {
            lock (_lock)
            {
                DateTime now = DateTime.UtcNow;
                if (_sessions.Count > 0)
                {
                    var last = _sessions[0];
                    if (last.LeftAt == null && last.JobId == jobId)
                    {
                        if (placeId > 0) last.PlaceId = placeId;
                        if (ip.Length > 0) last.ServerIp = ip;
                        if (port > 0) last.ServerPort = port;
                        SaveLocked();
                        return;
                    }
                }
                // Закрываем все висящие открытые (смена плейса/краш прошлого раза).
                foreach (var s in _sessions)
                    if (s.LeftAt == null) s.LeftAt = now;
                _sessions.Insert(0, new RecentSession
                {
                    JobId = jobId ?? "",
                    PlaceId = placeId,
                    ServerIp = ip ?? "",
                    ServerPort = port,
                    JoinedAt = now,
                });
                if (_sessions.Count > MaxSessions)
                    _sessions.RemoveRange(MaxSessions, _sessions.Count - MaxSessions);
                SaveLocked();
            }
        }

        public void SetName(string jobId, string name)
        {
            if (name.Length == 0) return;
            lock (_lock)
            {
                var s = FindLocked(jobId);
                if (s == null) return;
                s.Name = name;
                SaveLocked();
            }
        }

        public void SetServer(string jobId, string ip, int port)
        {
            lock (_lock)
            {
                var s = FindLocked(jobId);
                if (s == null) return;
                if (ip.Length > 0) s.ServerIp = ip;
                if (port > 0) s.ServerPort = port;
                SaveLocked();
            }
        }

        /// <summary>Игра закрыта — фиксируем время выхода у всех открытых.</summary>
        public void EndAll()
        {
            lock (_lock)
            {
                bool changed = false;
                DateTime now = DateTime.UtcNow;
                foreach (var s in _sessions)
                {
                    if (s.LeftAt == null)
                    {
                        s.LeftAt = now;
                        changed = true;
                    }
                }
                if (changed) SaveLocked();
            }
        }

        public void Remove(RecentSession session)
        {
            if (session == null) return;
            lock (_lock)
            {
                _sessions.RemoveAll(s => s.JobId == session.JobId && s.JoinedAt == session.JoinedAt);
                SaveLocked();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _sessions.Clear();
                SaveLocked();
            }
        }

        private RecentSession? FindLocked(string jobId)
        {
            if (jobId.Length > 0)
            {
                var open = _sessions.Find(s => s.LeftAt == null && s.JobId == jobId);
                if (open != null) return open;
                var any = _sessions.Find(s => s.JobId == jobId);
                if (any != null) return any;
            }
            // JobId пустой (только UDMUX поймали) — обновляем самую свежую открытую.
            return _sessions.Find(s => s.LeftAt == null) ?? (_sessions.Count > 0 ? _sessions[0] : null);
        }

        private void SaveLocked()
        {
            try
            {
                using var ms = new MemoryStream();
                using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteStartArray("Sessions");
                    foreach (var s in _sessions)
                    {
                        w.WriteStartObject();
                        w.WriteString("JobId", s.JobId);
                        w.WriteNumber("PlaceId", s.PlaceId);
                        w.WriteString("Name", s.Name);
                        w.WriteString("ServerIp", s.ServerIp);
                        w.WriteNumber("ServerPort", s.ServerPort);
                        w.WriteString("JoinedAt", s.JoinedAt.ToString("o"));
                        if (s.LeftAt != null) w.WriteString("LeftAt", s.LeftAt.Value.ToString("o"));
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? ".");
                File.WriteAllBytes(FilePath, ms.ToArray());
            }
            catch { /* ignore */ }
        }

        // ---------- Форматирование для таблицы ----------

        public static string FormatWhen(DateTime utc) => FormatWhen(utc,
            "только что", "{0} мин назад", "{0} ч назад", "{0} дн назад",
            System.Globalization.CultureInfo.CurrentCulture);

        /// <summary>То же с явными словами/культурой (для локализации UI).</summary>
        public static string FormatWhen(DateTime utc, string justNow,
            string minAgoFmt, string hourAgoFmt, string dayAgoFmt,
            System.Globalization.CultureInfo culture)
        {
            if (utc == default) return "—";
            var ago = DateTime.UtcNow - utc;
            if (ago.TotalMinutes < 1) return justNow;
            if (ago.TotalMinutes < 60) return string.Format(culture, minAgoFmt, (int)ago.TotalMinutes);
            if (ago.TotalHours < 24) return string.Format(culture, hourAgoFmt, (int)ago.TotalHours);
            if (ago.TotalDays < 7) return string.Format(culture, dayAgoFmt, (int)ago.TotalDays);
            return utc.ToLocalTime().ToString("d MMM yyyy, HH:mm", culture);
        }

        public static string FormatDuration(TimeSpan d) => FormatDuration(d, "меньше минуты", "мин", "ч");

        /// <summary>То же с явными единицами (для локализации UI).</summary>
        public static string FormatDuration(TimeSpan d, string lessMinute, string minUnit, string hourUnit)
        {
            if (d.TotalSeconds < 60) return lessMinute;
            if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} {minUnit}";
            long h = (long)d.TotalHours;
            long m = d.Minutes;
            return m > 0 ? $"{h} {hourUnit} {m} {minUnit}" : $"{h} {hourUnit}";
        }
    }
}
