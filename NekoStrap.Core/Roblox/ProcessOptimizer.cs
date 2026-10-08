using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NekoStrap.Roblox
{
    /// <summary>
    /// Оптимизация процесса RobloxPlayerBeta после запуска: приоритет
    /// процесса, снижение приоритета лаунчера в фоне, опциональный
    /// трим рабочего набора при сворачивании. Вызывается из
    /// MainWindow при старте клиента и при закрытии.
    /// </summary>
    internal sealed class ProcessOptimizer : IDisposable
    {
        private const int PollIntervalMs = 3000;
        private const int TrimDelayMs = 15000;
        private const int TrimIntervalMs = 60000;

        private readonly int _processId;
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;
        private bool _disposed;
        private int _started;

        private ProcessPriorityClass? _originalPriority;
        private DateTime _unfocusedSinceUtc = DateTime.MinValue;
        private long _lastTrimTicks;

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public ProcessOptimizer(int processId)
        {
            _processId = processId;
        }

        /// <summary>
        /// Каноничные метки настройки приоритета: порядок = порядок в UI,
        /// "" — системный по умолчанию. Любой другой вход ResolvePriority
        /// молча сводит к Normal, поэтому рукописный конфиг не уронит старт.
        /// </summary>
        public static readonly string[] PriorityOptions =
            { "", "idle", "belownormal", "abovenormal", "high" };

        /// <summary>Приоритет для нового процесса по настройке (пусто = Normal).</summary>
        public static ProcessPriorityClass ResolvePriority(string? priority)
        {
            return (priority ?? "").Trim().ToLowerInvariant() switch
            {
                "high" or "realtime" => ProcessPriorityClass.High,
                "abovenormal" or "above normal" => ProcessPriorityClass.AboveNormal,
                "belownormal" or "below normal" => ProcessPriorityClass.BelowNormal,
                "idle" or "low" => ProcessPriorityClass.Idle,
                _ => ProcessPriorityClass.Normal,
            };
        }

        /// <summary>
        /// Разово назначить приоритет процессу Roblox при запуске.
        /// Идемпотентно: повторный вызов не сбрасывает.
        /// </summary>
        public static void ApplyLaunchPriority(int processId, string? priority)
        {
            try
            {
                var desired = ResolvePriority(priority);
                if (desired == ProcessPriorityClass.Normal) return;
                using var p = Process.GetProcessById(processId);
                if (p.HasExited || !IsRobloxPlayer(p)) return;
                p.PriorityClass = desired;
            }
            catch { /* нет прав или процесс уже завершён */ }
        }

        /// <summary>Запустить фоновый цикл: приоритет по фокусу + трим памяти.</summary>
        public void Start()
        {
            if (_disposed || Interlocked.Exchange(ref _started, 1) != 0) return;
            _loop = Task.Run(() => RunAsync(_cts.Token));
        }

        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                using var process = TryGetProcess();
                if (process == null || process.HasExited || !IsRobloxPlayer(process)) return;
                _originalPriority = process.PriorityClass;

                while (!token.IsCancellationRequested && !process.HasExited)
                {
                    bool focused = IsProcessFocused();

                    // Roblox в фоне -> BelowNormal; в фокусе -> исходный.
                    var desired = focused ? _originalPriority : ProcessPriorityClass.BelowNormal;
                    if (desired.HasValue && process.PriorityClass != desired.Value)
                    {
                        try { process.PriorityClass = desired.Value; } catch { }
                    }

                    if (focused)
                    {
                        _unfocusedSinceUtc = DateTime.MinValue;
                    }
                    else
                    {
                        if (_unfocusedSinceUtc == DateTime.MinValue)
                            _unfocusedSinceUtc = DateTime.UtcNow;
                        else if ((DateTime.UtcNow - _unfocusedSinceUtc).TotalMilliseconds >= TrimDelayMs)
                            TrimWorkingSet(process);
                    }

                    await Task.Delay(PollIntervalMs, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch { /* процесс мог закрыться */ }
            finally
            {
                RestorePriority();
            }
        }

        private void TrimWorkingSet(Process process)
        {
            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastTrimTicks) < TrimIntervalMs) return;
            Interlocked.Exchange(ref _lastTrimTicks, now);
            try
            {
                EmptyWorkingSet(process.Handle);
                process.Refresh();
            }
            catch { }
        }

        private void RestorePriority()
        {
            if (!_originalPriority.HasValue) return;
            try
            {
                using var p = TryGetProcess();
                if (p != null && !p.HasExited)
                    p.PriorityClass = _originalPriority.Value;
            }
            catch { }
        }

        private Process? TryGetProcess()
        {
            try { return Process.GetProcessById(_processId); }
            catch { return null; }
        }

        private bool IsProcessFocused()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return false;
                GetWindowThreadProcessId(hwnd, out uint pid);
                return pid == (uint)_processId;
            }
            catch { return false; }
        }

        private static bool IsRobloxPlayer(Process p)
        {
            try
            {
                return p.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase)
                    || p.ProcessName.Equals("RobloxStudioBeta", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            try { _loop?.GetAwaiter().GetResult(); } catch { }
            _cts.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
