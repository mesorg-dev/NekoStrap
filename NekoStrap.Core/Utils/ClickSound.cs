using System.Diagnostics;
using System.IO;
using System.Media;

namespace NekoStrap.Utils
{
    /// <summary>
    /// Мягкие «премиальные» звуки интерфейса без внешних файлов.
    /// Синтез: тёплый тон с падением высоты (как «ток» в macOS/Linear),
    /// плавная атака 2-3 мс (убирает резкий щелчок) + быстрое затухание.
    /// Никакого пронзительного писка на 1200-2200 Гц.
    /// </summary>
    public static class ClickSound
    {
        /// <summary>Мастер-переключатель (привяжите к чекбоксу «Звуки интерфейса»).</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>Мастер-громкость 0..1 (множитель поверх громкости каждого звука).</summary>
        public static float Volume { get; set; } = 0.9f;

        /// <summary>
        /// Звуки интерфейса: ключ → подпись для настроек. Порядок — как в UI.
        /// </summary>
        public static IReadOnlyList<(string Key, string Title)> Sounds { get; } =
        [
            ("click", "Клик"),
            ("hover", "Наведение"),
            ("open", "Переход"),
            ("close", "Закрытие"),
            ("on", "Тумблер вкл"),
            ("off", "Тумблер выкл"),
        ];

        private static readonly byte[] _clickWav = LoadClick();
        private static readonly byte[] _hoverWav = LoadHover();
        // Переход между вкладками: твой wav из Assets, иначе мягкий шелест.
        private static readonly byte[] _openWav = LoadTransition(1f) ?? BuildPuff(48, 340, 0.12);
        private static readonly byte[] _closeWav = LoadTransition(0.9f) ?? BuildPuff(42, 290, 0.11);
        // Тумблеры: лёгкие «токи» в духе клика, вкл — чуть выше, выкл — чуть ниже.
        private static readonly byte[] _onWav = BuildTok(680, 610, 30, 0.20);
        private static readonly byte[] _offWav = BuildTok(500, 450, 30, 0.17);

        // --- Кастом: свой wav + своя громкость на каждый звук ---
        private static readonly object _customLock = new();
        private static readonly Dictionary<string, byte[]> _customWav = new();
        private static readonly Dictionary<string, string> _customPath = new();
        private static readonly Dictionary<string, float> _volumes = new();

        private static readonly Stopwatch _hoverWatch = Stopwatch.StartNew();
        private static long _lastHoverMs;

        public static void PlayClick() => Play(Resolve("click", _clickWav), "click");
        public static void PlayOpen() => Play(Resolve("open", _openWav), "open");
        public static void PlayClose() => Play(Resolve("close", _closeWav), "close");
        public static void PlayToggle(bool on) => Play(on ? Resolve("on", _onWav) : Resolve("off", _offWav), on ? "on" : "off");

        /// <summary>Очень тихий тик наведения + троттлинг, чтобы не строчил при движении мыши.</summary>
        public static void PlayHover()
        {
            long now = _hoverWatch.ElapsedMilliseconds;
            if (now - _lastHoverMs < 90) return;
            _lastHoverMs = now;
            Play(Resolve("hover", _hoverWav), "hover");
        }

        // ================= Кастомные звуки =================

        private static bool KnownKey(string key)
        {
            foreach (var (k, _) in Sounds)
                if (k == key) return true;
            return false;
        }

        private static byte[] Resolve(string key, byte[] fallback)
        {
            lock (_customLock)
                return _customWav.TryGetValue(key, out var custom) ? custom : fallback;
        }

        /// <summary>
        /// Поставить свой wav на звук. Файл чистится тем же SanitizeWav
        /// (левые чанки, стерео→моно, нормализация) — подойдёт любой wav.
        /// false + error — файл битый, остаётся предыдущий звук.
        /// </summary>
        public static bool TrySetCustom(string key, string path, out string error)
        {
            error = "";
            if (!KnownKey(key))
            {
                error = "неизвестный звук";
                return false;
            }
            try
            {
                if (!File.Exists(path))
                {
                    error = "файл не найден";
                    return false;
                }
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fs.Length < 44 || fs.Length > 8 * 1024 * 1024)
                {
                    error = "подозрительный размер (нужен wav до 8 МБ)";
                    return false;
                }
                var raw = new byte[fs.Length];
                int read = 0;
                while (read < raw.Length)
                {
                    int r = fs.Read(raw, read, raw.Length - read);
                    if (r == 0) break;
                    read += r;
                }
                byte[] clean = SanitizeWav(raw, 1f);
                lock (_customLock)
                {
                    _customWav[key] = clean;
                    _customPath[key] = path;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "не вышло прочитать wav: " + ex.Message;
                return false;
            }
        }

        /// <summary>Убрать свой wav — вернётся встроенный/синтезированный.</summary>
        public static void ClearCustom(string key)
        {
            lock (_customLock)
            {
                _customWav.Remove(key);
                _customPath.Remove(key);
            }
        }

        /// <summary>Путь к своему wav (null — встроенный).</summary>
        public static string? GetCustomPath(string key)
        {
            lock (_customLock)
                return _customPath.TryGetValue(key, out var p) ? p : null;
        }

        /// <summary>Громкость звука 0..1 (множитель поверх мастер-громкости).</summary>
        public static void SetSoundVolume(string key, float v)
        {
            if (!KnownKey(key)) return;
            lock (_customLock)
                _volumes[key] = Math.Clamp(v, 0f, 1f);
        }

        public static float GetSoundVolume(string key)
        {
            lock (_customLock)
                return _volumes.TryGetValue(key, out var v) ? v : 1f;
        }

        /// <summary>
        /// Применить сохранённые пути и громкости (при старте). Битые файлы
        /// молча пропускаются — играют встроенные звуки.
        /// </summary>
        public static void LoadFromConfig(
            IDictionary<string, string>? paths, IDictionary<string, float>? volumes)
        {
            try
            {
                if (volumes != null)
                    foreach (var (k, v) in volumes)
                        SetSoundVolume(k, v);
                if (paths != null)
                    foreach (var (k, p) in paths)
                    {
                        if (p.Length == 0) continue;
                        TrySetCustom(k, p, out _);
                    }
            }
            catch { /* ignore */ }
        }

        private static void Play(byte[] wav, string key)
        {
            if (!Enabled) return;
            float v = Math.Clamp(Volume, 0f, 1f) * GetSoundVolume(key);
            if (v <= 0.001f) return;
            try
            {
                // Копируем массив: SoundPlayer читает поток асинхронно (Play),
                // общий буфер без копии давал бы гонки при быстрых кликах.
                // Заодно применяем актуальную громкость к PCM-данным (заголовок 44 байта не трогаем).
                var copy = new byte[wav.Length];
                Buffer.BlockCopy(wav, 0, copy, 0, wav.Length);
                if (Math.Abs(v - 1f) > 0.001f)
                {
                    for (int i = 44; i + 1 < copy.Length; i += 2)
                    {
                        short s = (short)(copy[i] | (copy[i + 1] << 8));
                        int scaled = (int)(s * v);
                        copy[i] = (byte)(scaled & 0xFF);
                        copy[i + 1] = (byte)((scaled >> 8) & 0xFF);
                    }
                }
                using var ms = new MemoryStream(copy);
                using var player = new SoundPlayer(ms);
                player.Play();
            }
            catch
            {
                // Нет звукового устройства — молча пропускаем.
            }
        }

        // «Ток»: мягкий удар 560→380 Гц, 55 мс. Тёплый, не писклявый.
        // Используется только как фолбэк, если встроенный wav-файл не загрузился.
        private static byte[] BuildClick() => BuildTok(560, 380, 55, 0.32);

        // Наведение: едва слышный короткий тик, 18 мс (фолбэк).
        private static byte[] BuildHover() => BuildTok(980, 860, 18, 0.07);

        /// <summary>Клик: твой wav из Assets, иначе синтезированный фолбэк.</summary>
        private static byte[] LoadClick() => TryLoadEmbedded("click.wav", 1f) ?? BuildClick();

        /// <summary>Наведение: тот же клик, но тише (×0.35) — характер звука общий.</summary>
        private static byte[] LoadHover() => TryLoadEmbedded("click.wav", 0.35f) ?? BuildHover();

        /// <summary>Переход: твой wav из Assets (null → синтезированный шелест).</summary>
        private static byte[]? LoadTransition(float gain) => TryLoadEmbedded("transition.wav", gain);

        /// <summary>
        /// Грузит wav из ресурсов exe (папка Assets, вшивается при сборке).
        /// Возвращает null при любой проблеме — вызыватель берёт синтезированный звук.
        /// </summary>
        private static byte[]? TryLoadEmbedded(string fileName, float gain)
        {
            try
            {
                var asm = typeof(ClickSound).Assembly;
                string res = $"{asm.GetName().Name}.Assets.{fileName}";
                using var s = asm.GetManifestResourceStream(res);
                if (s == null || s.Length < 44 || s.Length > 8 * 1024 * 1024) return null;
                var raw = new byte[s.Length];
                int read = 0;
                while (read < raw.Length)
                {
                    int r = s.Read(raw, read, raw.Length - read);
                    if (r == 0) break;
                    read += r;
                }
                return SanitizeWav(raw, gain);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Чистит внешний wav для UI: левые чанки (bext/junk) выкидываются,
        /// стерео → моно, тишина по краям обрезается, пик нормализуется к 0.85.
        /// Возвращает готовый PCM16-mono wav. Бросает исключение —
        /// вызыватель молча использует синтезированный фолбэк.
        /// </summary>
        private static byte[] SanitizeWav(byte[] riff, float gain)
        {
            if (riff.Length < 44 || riff[0] != 'R' || riff[1] != 'I' || riff[2] != 'F' || riff[3] != 'F')
                throw new InvalidDataException("not RIFF");
            if (riff[8] != 'W' || riff[9] != 'A' || riff[10] != 'V' || riff[11] != 'E')
                throw new InvalidDataException("not WAVE");

            int audioFormat = 1, channels = 1, rate = 44100, bits = 16;
            byte[]? data = null;
            int pos = 12;
            while (pos + 8 <= riff.Length)
            {
                string tag = System.Text.Encoding.ASCII.GetString(riff, pos, 4);
                int size = BitConverter.ToInt32(riff, pos + 4);
                if (size < 0 || pos + 8 + size > riff.Length) break;
                if (tag == "fmt " && size >= 16)
                {
                    audioFormat = BitConverter.ToUInt16(riff, pos + 8);
                    channels = BitConverter.ToUInt16(riff, pos + 10);
                    rate = BitConverter.ToInt32(riff, pos + 12);
                    bits = BitConverter.ToUInt16(riff, pos + 22); // data+14: byteRate(+8) blockAlign(+12) bits(+14)
                }
                else if (tag == "data" && data == null)
                {
                    data = new byte[size];
                    Buffer.BlockCopy(riff, pos + 8, data, 0, size);
                }
                pos += 8 + size + (size & 1);
            }
            if (data == null || data.Length == 0) throw new InvalidDataException("no data");
            if (channels < 1 || channels > 8) throw new InvalidDataException("channels");
            if (rate < 8000 || rate > 192000) throw new InvalidDataException("rate");

            // → float mono.
            int bytesPerSample = bits / 8;
            if (bits % 8 != 0 || bytesPerSample < 1 || bytesPerSample > 4)
                throw new InvalidDataException("bits");
            int frameSize = bytesPerSample * channels;
            int frames = data.Length / frameSize;
            if (frames < 16) throw new InvalidDataException("too short");
            var mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                double sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int o = f * frameSize + c * bytesPerSample;
                    double v = (audioFormat, bits) switch
                    {
                        (1, 8) => (data[o] - 128) / 128.0,
                        (1, 16) => BitConverter.ToInt16(data, o) / 32768.0,
                        (1, 24) => ReadS24(data, o) / 8388608.0,
                        (1, 32) => BitConverter.ToInt32(data, o) / 2147483648.0,
                        (3, 32) => BitConverter.ToSingle(data, o),
                        _ => throw new InvalidDataException("format")
                    };
                    sum += v;
                }
                mono[f] = (float)(sum / channels);
            }

            // Пик → нормализация к 0.85.
            float peak = 0;
            foreach (var x in mono)
            {
                float a = Math.Abs(x);
                if (a > peak) peak = a;
            }
            if (peak < 0.001f) throw new InvalidDataException("silence");
            float norm = 0.85f / peak;

            int ms(int mseconds) => Math.Max(1, (int)(rate * (mseconds / 1000.0)));
            int first = 0;
            while (first < frames && Math.Abs(mono[first]) < 0.01f) first++;
            int last = frames - 1;
            while (last > first && Math.Abs(mono[last]) < 0.03f) last--;
            int start = Math.Max(0, first - ms(2));
            int end = Math.Min(frames, last + ms(60));
            if (end - start < ms(10)) throw new InvalidDataException("too short after trim");

            var pcm = new short[end - start];
            int fadeIn = Math.Min(ms(1), pcm.Length);
            int fadeOut = Math.Min(ms(10), pcm.Length);
            for (int i = 0; i < pcm.Length; i++)
            {
                float v = mono[start + i] * norm * gain;
                float fade = 1f;
                if (i < fadeIn) fade = i / (float)fadeIn;
                else if (i >= pcm.Length - fadeOut) fade = (pcm.Length - 1 - i) / (float)fadeOut;
                pcm[i] = (short)(Math.Clamp(v * fade, -1f, 1f) * short.MaxValue);
            }
            return BuildWav(pcm, rate);
        }

        private static int ReadS24(byte[] data, int o)
        {
            int s = data[o] | (data[o + 1] << 8) | (data[o + 2] << 16);
            if ((s & 0x800000) != 0) s |= unchecked((int)0xFF000000);
            return s;
        }

        /// <summary>
        /// Тёплый «ток» со свипом высоты вниз: основа + тихая вторая гармоника
        /// для округлости + мягкий нойз-транзиент первые 3 мс для тактильности.
        /// </summary>
        private static byte[] BuildTok(double fromHz, double toHz, int durationMs, double volume)
        {
            const int rate = 44100;
            int n = (int)(rate * (durationMs / 1000.0));
            var rnd = new Random(7);
            var samples = new short[n];

            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                double phase = 2 * Math.PI * (fromHz * t + (toHz - fromHz) * t * t / (durationMs / 1000.0 * 2));

                // Плавная атака 2.5 мс убирает резкий щелчок в начале.
                double attack = Math.Min(1.0, t / 0.0025);
                attack = attack * attack * (3 - 2 * attack); // smoothstep
                double decay = Math.Exp(-t * 55.0);

                double tone = Math.Sin(phase) + 0.28 * Math.Sin(phase * 2.0);
                tone /= 1.28;

                // Едва заметный нойз-транзиент для «телесности», только в начале.
                double noise = 0;
                if (t < 0.004)
                    noise = (rnd.NextDouble() * 2 - 1) * 0.25 * (1 - t / 0.004);

                double v = (tone * decay + noise * Math.Exp(-t * 400.0)) * attack * volume;
                samples[i] = (short)(Math.Clamp(v, -1.0, 1.0) * short.MaxValue);
            }
            return BuildWav(samples, rate);
        }

        /// <summary>
        /// Мягкий «воздушный» шелест для переходов: приглушённый шум + тихий
        /// низкий тон, плавное появление и затухание. Без свиста высоты —
        /// ничего «лазерного», просто лёгкое движение воздуха.
        /// </summary>
        private static byte[] BuildPuff(int durationMs, double bodyHz, double volume)
        {
            const int rate = 44100;
            int n = (int)(rate * (durationMs / 1000.0));
            var rnd = new Random(21);
            var samples = new short[n];
            double dur = durationMs / 1000.0;
            double lp = 0; // состояние однополюсного lowpass — глушит шипение

            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;

                double attack = Math.Min(1.0, t / 0.010);
                attack = attack * attack * (3 - 2 * attack);
                double release = Math.Clamp((dur - t) / 0.016, 0.0, 1.0);
                release = release * release * (3 - 2 * release);

                double white = rnd.NextDouble() * 2 - 1;
                lp += 0.12 * (white - lp);
                double body = Math.Sin(2 * Math.PI * bodyHz * t) * 0.5;

                double v = (lp * 0.8 + body * 0.5) * attack * release * volume;
                samples[i] = (short)(Math.Clamp(v, -1.0, 1.0) * short.MaxValue);
            }
            return BuildWav(samples, rate);
        }

        private static byte[] BuildWav(short[] samples, int sampleRate)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            int dataSize = samples.Length * 2;
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + dataSize);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(dataSize);
            foreach (var s in samples)
                bw.Write(s);

            bw.Flush();
            return ms.ToArray();
        }
    }
}
