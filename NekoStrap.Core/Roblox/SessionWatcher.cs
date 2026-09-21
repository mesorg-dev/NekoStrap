using System.Text.RegularExpressions;

namespace NekoStrap.Roblox
{
    public sealed record GameSession(string JobId, long PlaceId, string ServerIp, int ServerPort);

    /// <summary>
    /// Следит за логом плеера (%LocalAppData%\Roblox\logs) и вытаскивает сессию:
    /// jobId + placeId из "! Joining game", IP сервера из "UDMUX Address".
    /// Механика — как ActivityWatcher в Voidstrap/Bloxstrap.
    /// </summary>
    internal sealed class SessionWatcher : IDisposable
    {
        private static readonly Regex JoinRe = new(
            @"! Joining game '([0-9a-f\-]{36})' place ([0-9]+) at ([0-9\.]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex UdmuxRe = new(
            @"UDMUX Address = ([0-9\.]+), Port = ([0-9]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Пинг из первых рук — клиент печатает его в лог:
        // "==== Total DataPing Estimate from above %d vs Measured %d" (нужен флаг
        // DFFlagDebugPrintDataPingBreakDown) и строки таблицы репликатора
        // "General (MTU Size, Data Ping): 1200, 150.17ms" / "Data Ping...: 150.17".
        // Форматы подсмотрены прямо в бинарнике плеера.
        private static readonly Regex BreakdownTotalRe = new(
            @"Total DataPing Estimate from above (\d+) vs Measured (\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ReplicatorPingRe = new(
            @"General\s+\(MTU Size, Data Ping\):\s*(\d+),\s*([\d.]+)ms",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex DataPingLineRe = new(
            @"Data Ping\.+\s*:\s*([\d.]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public event EventHandler<GameSession>? SessionChanged;

        public GameSession? Current { get; private set; }

        /// <summary>Пинг глазами самого клиента (мс) и когда замерен.</summary>
        public int LastClientPingMs { get; private set; } = -1;
        public DateTime LastClientPingAt { get; private set; } = DateTime.MinValue;

        private readonly System.Threading.Timer _timer;
        private string? _logPath;
        private long _position;
        private bool _disposed;

        public SessionWatcher()
        {
            _timer = new System.Threading.Timer(_ => Poll(), null,
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }

        public void Clear()
        {
            Current = null;
            LastClientPingMs = -1;
            _logPath = null;
            _position = 0;
        }

        private static string LogsDir() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox", "logs");

        private void Poll()
        {
            try
            {
                string dir = LogsDir();
                if (!Directory.Exists(dir)) return;
                var newest = new DirectoryInfo(dir).GetFiles("*.log")
                    .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                if (newest == null) return;

                if (_logPath != newest.FullName)
                {
                    _logPath = newest.FullName;
                    _position = 0;
                }

                using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length < _position) _position = 0; // лог пересоздали
                if (fs.Length == _position) return;
                fs.Seek(_position, SeekOrigin.Begin);
                using var sr = new StreamReader(fs);
                string? line;
                while ((line = sr.ReadLine()) != null)
                    ScanLine(line);
                _position = fs.Position;
            }
            catch { /* лог занят/нет доступа — попробуем в следующий раз */ }
        }

        private void ScanLine(string line)
        {
            ScanClientPing(line);

            var jm = JoinRe.Match(line);
            if (jm.Success)
            {
                var session = new GameSession(
                    jm.Groups[1].Value,
                    long.TryParse(jm.Groups[2].Value, out long p) ? p : 0,
                    jm.Groups[3].Value,
                    0); // порт придёт строкой UDMUX чуть позже
                Set(session);
                return;
            }
            var um = UdmuxRe.Match(line);
            if (um.Success)
            {
                int port = int.TryParse(um.Groups[2].Value, out int pt) ? pt : 0;
                if (Current != null && (Current.ServerIp.Length == 0 || Current.ServerPort == 0))
                    Set(Current with { ServerIp = um.Groups[1].Value, ServerPort = port });
                else if (Current == null && um.Groups[1].Value.Length > 0)
                    Set(new GameSession("", 0, um.Groups[1].Value, port));
            }
        }

        /// <summary>
        /// Ловит пинг клиента из лога. Возвращает true, если строка дала замер.
        /// </summary>
        internal bool ScanClientPing(string line)
        {
            var m = BreakdownTotalRe.Match(line);
            if (m.Success && int.TryParse(m.Groups[2].Value, out int measured) && measured > 0)
            {
                SetClientPing(measured);
                return true;
            }
            m = ReplicatorPingRe.Match(line);
            if (m.Success && double.TryParse(m.Groups[2].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double rp) && rp > 0)
            {
                SetClientPing((int)Math.Round(rp));
                return true;
            }
            m = DataPingLineRe.Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double dp) && dp > 0)
            {
                SetClientPing((int)Math.Round(dp));
                return true;
            }
            return false;
        }

        private void SetClientPing(int ms)
        {
            LastClientPingMs = ms;
            LastClientPingAt = DateTime.UtcNow;
        }

        private void Set(GameSession session)
        {
            if (session.Equals(Current)) return;
            Current = session;
            try { SessionChanged?.Invoke(this, session); } catch { /* ignore */ }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
        }
    }
}
