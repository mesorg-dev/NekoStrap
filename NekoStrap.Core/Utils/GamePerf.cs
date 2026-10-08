using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NekoStrap.Utils
{
    /// <summary>
    /// Метрики процесса игрока для оверлея производительности.
    /// RAM и CPU% берутся из Process (WorkingSet + дельта TotalProcessorTime,
    /// делённая на число ядер), GPU% — из счётчиков Windows через PDH:
    /// \GPU Engine(pid_*_...)\Utilization Percentage (работает без админа и
    /// без инъекций; инстансы процесса ищем через PdhEnumObjectItems).
    /// Недоступное значение = -1 — оверлей в этом случае прячет строку.
    /// </summary>
    internal sealed class GamePerf : IDisposable
    {
        private const int PDH_FMT_DOUBLE = 0x00000200;
        private const int PERF_DETAIL_WIZARD = 400;
        private const int PDH_MORE_DATA = unchecked((int)0x800007D5);

        // Счётчик GPU уязвим к гонке: PDH недоступен (нет объекта) — после
        // трёх подряд отказов перестаём лезть в API до следующего сэмпла
        // от другой сессии.
        private const int MaxFails = 3;

        // CPU: базовая точка + дельта между тиками.
        private TimeSpan _lastCpu = TimeSpan.Zero;
        private DateTime _lastCpuAt = DateTime.MinValue;
        private bool _cpuPrimed;

        // GPU: один открытый запрос с явными счётчиками по инстансам процесса.
        private IntPtr _query;
        private readonly List<IntPtr> _counters = new();
        private string _sig = "\u0001"; // pid + отсортированные инстансы
        private int _fails;

        /// <summary>Загрузка CPU, % от всех ядер. -1 = ещё не измерено.</summary>
        public double CpuPercent { get; private set; } = -1;

        /// <summary>Рабочая память процесса, МБ.</summary>
        public double RamMb { get; private set; }

        /// <summary>Загрузка GPU процессом, % (0..100). -1 = недоступно.</summary>
        public double GpuPercent { get; private set; } = -1;

        /// <summary>Один сэмпл. Вызывать раз в секунду, пока оверлей виден.</summary>
        public void Update(Process? player)
        {
            if (player == null)
            {
                Reset();
                return;
            }
            ReadCpuRam(player);
            try { UpdateGpu(player.Id); }
            catch { GpuPercent = -1; _fails++; }
        }

        private void ReadCpuRam(Process player)
        {
            try
            {
                player.Refresh();
                RamMb = player.WorkingSet64 / 1048576.0;
            }
            catch
            {
                CpuPercent = -1;
                RamMb = 0;
                return;
            }
            try
            {
                var now = DateTime.UtcNow;
                var total = player.TotalProcessorTime;
                if (!_cpuPrimed)
                {
                    // Первый тик — только базовая точка для дельты.
                    _lastCpu = total;
                    _lastCpuAt = now;
                    _cpuPrimed = true;
                    CpuPercent = -1;
                    return;
                }
                double wall = (now - _lastCpuAt).TotalMilliseconds;
                if (wall < 250) return;
                double busy = (total - _lastCpu).TotalMilliseconds;
                CpuPercent = Math.Clamp(
                    busy / wall / Math.Max(1, Environment.ProcessorCount) * 100.0,
                    0, 100);
                _lastCpu = total;
                _lastCpuAt = now;
            }
            catch { CpuPercent = -1; }
        }

        private void UpdateGpu(int pid)
        {
            if (_fails >= MaxFails)
            {
                GpuPercent = -1;
                return;
            }
            string[] inst = EnumInstances(pid);
            _fails = 0;

            // Множество инстансов (или pid) изменилось — пересобираем запрос.
            string sig = pid + ":" + string.Join("|", inst);
            if (!string.Equals(sig, _sig, StringComparison.Ordinal))
            {
                _sig = sig;
                Rebuild(pid, inst); // внутри базовый снимок для дельты
                GpuPercent = -1;    // значение будет со следующего тика
                return;
            }
            if (_query == IntPtr.Zero || _counters.Count == 0)
            {
                GpuPercent = -1;
                return;
            }
            if (PdhCollectQueryData(_query) != 0)
            {
                GpuPercent = -1;
                return;
            }
            double sum = 0;
            bool any = false;
            foreach (var c in _counters)
            {
                if (PdhGetFormattedCounterValue(c, PDH_FMT_DOUBLE, out _, out var v) == 0)
                {
                    sum += v.DoubleValue;
                    any = true;
                }
            }
            // 3D/VideoDecode и т.д. считаются по отдельности — сумма может
            // перевалить за 100, показываем в пределах одного GPU.
            GpuPercent = any ? Math.Clamp(sum, 0, 100) : -1;
        }

        /// <summary>
        /// Инстансы "GPU Engine" этого процесса (null = PDH недоступен).
        /// Буферы отдаём char[длина из API] — длина в символах и в байтах
        /// даёт корректный результат (проверено: 2-й вызов rc=0).
        /// </summary>
        private static string[] EnumInstances(int pid)
        {
            int cchCounter = 0, cchInstance = 0;
            int rc = PdhEnumObjectItemsW(null, null, "GPU Engine",
                IntPtr.Zero, ref cchCounter, IntPtr.Zero, ref cchInstance,
                PERF_DETAIL_WIZARD, 0);
            // Первый вызов всегда отвечает отказом с размерами (проверено).
            if (cchInstance <= 0)
            {
                if (rc == 0) return Array.Empty<string>();
                throw new System.ComponentModel.Win32Exception(rc);
            }
            var counterBuf = new char[Math.Max(1, cchCounter)];
            var instanceBuf = new char[cchInstance];
            var hCounter = GCHandle.Alloc(counterBuf, GCHandleType.Pinned);
            var hInstance = GCHandle.Alloc(instanceBuf, GCHandleType.Pinned);
            try
            {
                rc = PdhEnumObjectItemsW(null, null, "GPU Engine",
                    hCounter.AddrOfPinnedObject(), ref cchCounter,
                    hInstance.AddrOfPinnedObject(), ref cchInstance,
                    PERF_DETAIL_WIZARD, 0);
                if (rc != 0) throw new System.ComponentModel.Win32Exception(rc);
            }
            finally
            {
                hCounter.Free();
                hInstance.Free();
            }
            string prefix = "pid_" + pid + "_";
            return new string(instanceBuf)
                .Split('\0')
                .Where(s => s.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
        }

        private void Rebuild(int pid, string[] instances)
        {
            CloseQuery();
            if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0)
            {
                _query = IntPtr.Zero;
                return;
            }
            foreach (string inst in instances)
            {
                string path = "\\GPU Engine(" + inst + ")\\Utilization Percentage";
                if (PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out IntPtr c) == 0)
                    _counters.Add(c);
            }
            if (_counters.Count > 0)
                PdhCollectQueryData(_query); // база для дельты на следующем тике
        }

        private void CloseQuery()
        {
            if (_query != IntPtr.Zero)
            {
                try { PdhCloseQuery(_query); } catch { /* ignore */ }
                _query = IntPtr.Zero;
            }
            _counters.Clear();
        }

        private void Reset()
        {
            CloseQuery();
            _sig = "\u0001";
            _fails = 0;
            _cpuPrimed = false;
            _lastCpu = TimeSpan.Zero;
            _lastCpuAt = DateTime.MinValue;
            CpuPercent = -1;
            RamMb = 0;
            GpuPercent = -1;
        }

        public void Dispose() => CloseQuery();

        // ---------- PDH ----------

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhEnumObjectItemsW(
            string? szDataSource, string? szMachineName, string szObjectName,
            IntPtr mszCounterList, ref int pdwCounterListLength,
            IntPtr mszInstanceList, ref int pdwInstanceListLength,
            int dwDetailLevel, int dwFlags);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhOpenQueryW(
            string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhAddEnglishCounterW(
            IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData,
            out IntPtr phCounter);

        [DllImport("pdh.dll")]
        private static extern int PdhCollectQueryData(IntPtr hQuery);

        [DllImport("pdh.dll")]
        private static extern int PdhCloseQuery(IntPtr hQuery);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhGetFormattedCounterValue(
            IntPtr hCounter, int dwFormat, out uint pdwType,
            out PdhFmtCounterValue pValue);

        [StructLayout(LayoutKind.Sequential)]
        private struct PdhFmtCounterValue
        {
            public uint CStatus;
            public double DoubleValue;
        }
    }
}
