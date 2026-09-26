using System.Runtime.ExceptionServices;

namespace NekoStrap.Roblox
{
    /// <summary>Стадия батча: фаза (download/extract), техническая деталь, доля 0..1.</summary>
    internal sealed record PackageBatchStage(string Phase, string Detail, double? Fraction);

    /// <summary>Один пакет: сжатый размер, где лежит зип, куда его распаковать.</summary>
    internal sealed record PackageBatchItem(string Name, long PackedSize, string ZipPath, string DestDir);

    /// <summary>Итоги батча — для прогресса и тестов.</summary>
    internal sealed class PackageBatchStats
    {
        public int Fetched;   // вошли в скачивание (с перекачками)
        public int Resumed;   // уже лежали на диске — качать не пришлось
        public int Extracted; // распаковано
        public int Retries;   // перекачек из-за битого размера/битого зипа
        public long Bytes;    // суммарно прогнано через сеть

        public override string ToString() =>
            $"fetched={Fetched} resumed={Resumed} extracted={Extracted} retries={Retries} bytes={Bytes}";
    }

    /// <summary>
    /// Установка пакетов двумя фазами: сначала качаем параллельно (лимит
    /// потоков — чтобы и линию занять, и диск не забить), потом распаковываем.
    /// Уже скачанное с нужным размером не качаем заново (возобновление после
    /// обрыва), битый размер и битый зип перекачиваем один раз.
    /// </summary>
    internal static class PackageBatch
    {
        /// <summary>Доля прогресса на загрузку, остальное — распаковка.</summary>
        public const double DownloadShare = 0.85;

        private const int ExtractParallelismMax = 4;
        private const long ReportMs = 120;

        /// <summary>Зип на диске целиковый (размер совпал с манифестом).</summary>
        public static bool IsComplete(string zipPath, long packedSize)
        {
            return packedSize > 0 && SafeLength(zipPath) == packedSize;
        }

        public static async Task<PackageBatchStats> RunAsync(
            IReadOnlyList<PackageBatchItem> items,
            Func<PackageBatchItem, IProgress<(long done, long total)>?, CancellationToken, Task> fetch,
            Action<PackageBatchItem, CancellationToken> extract,
            int parallelism,
            IProgress<PackageBatchStage>? progress,
            CancellationToken ct)
        {
            if (parallelism < 1) parallelism = 1;
            var st = new BatchState(items, progress);
            if (items.Count == 0) return st.Stats;

            // Фаза 1 — загрузка.
            st.Phase = 0;
            await RunParallelAsync(items.Count, parallelism, async i =>
            {
                var item = items[i];
                if (IsComplete(item.ZipPath, item.PackedSize))
                {
                    Interlocked.Increment(ref st.Stats.Resumed);
                    st.SetBytes(i, item.PackedSize);
                }
                else
                {
                    await FetchOneAsync(item, i, fetch, st, ct);
                }
                int done = Interlocked.Increment(ref st.DoneDownload);
                st.DownloadReport(force: done >= items.Count);
            }, ct);

            // Фаза 2 — распаковка (параллельно, но осторожнее: это диск).
            Volatile.Write(ref st.Phase, 1);
            await RunParallelAsync(items.Count, Math.Min(parallelism, ExtractParallelismMax), async i =>
            {
                var item = items[i];
                for (int attempt = 0; ; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        extract(item, ct);
                        break;
                    }
                    catch (InvalidDataException) when (attempt < 1)
                    {
                        // Зип битый (обычно обрыв при скачивании) — чистим и
                        // качаем заново, потом распаковываем ещё раз.
                        Interlocked.Increment(ref st.Stats.Retries);
                        TryDelete(item.ZipPath);
                        st.SetBytes(i, 0);
                        await FetchOneAsync(item, i, fetch, st, ct);
                    }
                }
                Interlocked.Increment(ref st.Stats.Extracted);
                int done = Interlocked.Increment(ref st.DoneExtract);
                st.ExtractReport(force: done >= items.Count);
            }, ct);

            return st.Stats;
        }

        /// <summary>Качает один пакет, пока размер не сойдётся ( максимум 2 попытки).</summary>
        private static async Task FetchOneAsync(
            PackageBatchItem item, int index,
            Func<PackageBatchItem, IProgress<(long done, long total)>?, CancellationToken, Task> fetch,
            BatchState st, CancellationToken ct)
        {
            for (int attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                await fetch(item, new ItemProgress(st, index), ct);
                Interlocked.Increment(ref st.Stats.Fetched);

                long len = SafeLength(item.ZipPath);
                if (len > 0) Interlocked.Add(ref st.Stats.Bytes, len);

                if (item.PackedSize <= 0 || len == item.PackedSize)
                {
                    st.SetBytes(index, len > 0 ? len : Math.Max(0, item.PackedSize));
                    return;
                }

                // Размер не сошёлся — это не тот файл, мусор убираем.
                st.SetBytes(index, 0);
                TryDelete(item.ZipPath);
                if (attempt >= 1)
                {
                    // Второй раз не сошлось — оставляем что есть, решит
                    // распаковка (и она же перекачает, если зип битый).
                    st.SetBytes(index, Math.Max(len, 0));
                    return;
                }
                Interlocked.Increment(ref st.Stats.Retries);
            }
        }

        /// <summary>Запускает шаги с лимитом одновременности; всегда дожидается всех.</summary>
        private static async Task RunParallelAsync(
            int count, int parallelism, Func<int, Task> step, CancellationToken ct)
        {
            if (count == 0) return;
            using var gate = new SemaphoreSlim(parallelism, parallelism);
            var tasks = new List<Task>(count);
            Exception? startError = null;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    int idx = i;
                    await gate.WaitAsync(ct);
                    // CancellationToken.None: отменённый до старта Task.Run не
                    // выполнил бы step и не открыл бы gate — остальные бы ждали.
                    tasks.Add(Task.Run(async () =>
                    {
                        try { await step(idx); }
                        finally { gate.Release(); }
                    }, CancellationToken.None));
                }
            }
            catch (Exception ex)
            {
                startError = ex;
            }

            try { await Task.WhenAll(tasks); }
            catch { if (startError == null) throw; }

            if (startError != null)
                ExceptionDispatchInfo.Capture(startError).Throw();
        }

        private static long SafeLength(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : -1; }
            catch { return -1; }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* ignore */ }
        }

        /// <summary>Сводный прогресс: байты по всем пакетам + счётчик стадий.</summary>
        private sealed class BatchState
        {
            public readonly PackageBatchStats Stats = new();
            public readonly long[] ItemBytes;
            public readonly long TotalBytes;
            public readonly int Total;
            public readonly IProgress<PackageBatchStage>? Progress;
            public int DoneDownload;
            public int DoneExtract;
            public int Phase; // 0 = download, 1 = extract
            private long _tick;

            public BatchState(IReadOnlyList<PackageBatchItem> items, IProgress<PackageBatchStage>? progress)
            {
                Progress = progress;
                Total = items.Count;
                ItemBytes = new long[Total];
                long sum = 0;
                for (int i = 0; i < Total; i++) sum += Math.Max(0, items[i].PackedSize);
                TotalBytes = sum;
            }

            public void SetBytes(int i, long v) => Interlocked.Exchange(ref ItemBytes[i], Math.Max(0, v));

            public long SumBytes()
            {
                long s = 0;
                for (int i = 0; i < ItemBytes.Length; i++) s += Interlocked.Read(ref ItemBytes[i]);
                return s;
            }

            public void DownloadReport(bool force)
            {
                var p = Progress;
                if (p == null) return;
                if (!force && !Throttle()) return;
                int done = Volatile.Read(ref DoneDownload);
                double frac = TotalBytes > 0
                    ? PackageBatch.DownloadShare * Math.Clamp((double)SumBytes() / TotalBytes, 0, 1)
                    : (done >= Total ? PackageBatch.DownloadShare : 0);
                p.Report(new PackageBatchStage("download", $"{Math.Min(done, Total)}/{Total}", frac));
            }

            public void ExtractReport(bool force)
            {
                var p = Progress;
                if (p == null) return;
                if (!force && !Throttle()) return;
                int done = Volatile.Read(ref DoneExtract);
                double frac = Total == 0 ? 1
                    : PackageBatch.DownloadShare +
                      (1 - PackageBatch.DownloadShare) * Math.Clamp((double)done / Total, 0, 1);
                p.Report(new PackageBatchStage("extract", $"{Math.Min(done, Total)}/{Total}", frac));
            }

            private bool Throttle()
            {
                long now = Environment.TickCount64;
                if (now - Interlocked.Read(ref _tick) < ReportMs) return false;
                Interlocked.Exchange(ref _tick, now);
                return true;
            }
        }

        /// <summary>Байты текущего пакета → сводный прогресс (темп отчётов — у BatchState).</summary>
        private sealed class ItemProgress : IProgress<(long done, long total)>
        {
            private readonly BatchState _st;
            private readonly int _index;

            public ItemProgress(BatchState st, int index)
            {
                _st = st;
                _index = index;
            }

            public void Report((long done, long total) value)
            {
                _st.SetBytes(_index, value.done);
                if (Volatile.Read(ref _st.Phase) == 0) _st.DownloadReport(force: false);
                else _st.ExtractReport(force: false);
            }
        }
    }
}
