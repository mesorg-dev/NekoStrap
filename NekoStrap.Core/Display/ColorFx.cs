using System.Runtime.InteropServices;

namespace NekoStrap.Display
{
    /// <summary>
    /// Настройки цветокоррекции. Диапазоны безопасные, дефолт = без изменений.
    /// </summary>
    public sealed record ColorSettings(
        float Saturation = 1f,    // 0 (чб) .. 2
        float Brightness = 0f,    // -0.5 .. +0.5
        float Contrast = 1f,      // 0 .. 2
        float Temperature = 0f)   // -1 (холодно) .. +1 (тепло)
    {
        public static readonly ColorSettings Default = new();
        public static readonly ColorSettings Cinema = new(1.15f, 0f, 1.05f, 0.25f);
        public static readonly ColorSettings Vivid = new(1.3f, 0.05f, 1.1f, 0f);
        public static readonly ColorSettings Mono = new(0f, 0f, 1.05f, 0f);
    }

    /// <summary>
    /// Цветокоррекция экрана через Magnification API Windows:
    /// матрица 5×5 применяется DWM на видеокарте — ноль лагов, без инъекций
    /// в процесс игры (бан невозможен). Handle окна не нужен (hwnd зарезервирован).
    /// </summary>
    internal static class ColorFx
    {
        // Внимание: начиная с Windows 8 сигнатура БЕЗ hwnd (только матрица),
        // тип переименован в MAGCOLOREFFECT. Старый Vista-вариант с hwnd
        // на Win10/11 падает с ERROR_INVALID_PARAMETER (87).
        [StructLayout(LayoutKind.Sequential)]
        private struct MAGCOLOREFFECT
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
            public float[] v;
        }

        [DllImport("Magnification.dll")]
        private static extern bool MagInitialize();

        [DllImport("Magnification.dll")]
        private static extern bool MagUninitialize();

        [DllImport("Magnification.dll")]
        private static extern bool MagSetFullscreenColorEffect(ref MAGCOLOREFFECT pEffect);

        [DllImport("Magnification.dll")]
        private static extern bool MagGetFullscreenColorEffect(ref MAGCOLOREFFECT pEffect);

        private static bool _initialized;
        private static readonly object _sync = new();

        public static bool EnsureInitialized()
        {
            lock (_sync)
            {
                if (_initialized) return true;
                try { _initialized = MagInitialize(); }
                catch { _initialized = false; }
                return _initialized;
            }
        }

        public static void Shutdown()
        {
            lock (_sync)
            {
                if (!_initialized) return;
                try
                {
                    Reset();
                    MagUninitialize();
                }
                catch { /* ignore */ }
                _initialized = false;
            }
        }

        /// <summary>
        /// Строит матрицу 5×5 (row-major) из настроек. Дефолт = identity.
        /// ВАЖНО: Magnification API ест матрицу как out = v·M
        /// (пример grayscale из доков MS сходится только так):
        /// m[вход, выход], оффсеты — в ПОСЛЕДНЕЙ СТРОКЕ, а не столбце.
        /// Перепутанный порядок даёт зелёный оттенок вместо серого при sat=0.
        /// </summary>
        public static float[] BuildMatrix(ColorSettings s)
        {
            float sat = Math.Clamp(s.Saturation, 0f, 2f);
            float bri = Math.Clamp(s.Brightness, -0.5f, 0.5f);
            float con = Math.Clamp(s.Contrast, 0f, 2f);
            float tmp = Math.Clamp(s.Temperature, -1f, 1f);

            const float lr = 0.2126f, lg = 0.7152f, lb = 0.0722f;

            // m[вход i, выход j]: out_j = sat*v_j + (1-sat)*Lum(v).
            var m = new float[5, 5];
            float[] lum = { lr, lg, lb };
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    m[i, j] = ((i == j) ? sat : 0f) + (1f - sat) * lum[i];

            // Температура: gains по ВЫХОДАМ (столбцам).
            float rGain = Math.Max(0f, 1f + 0.18f * tmp);
            float bGain = Math.Max(0f, 1f - 0.18f * tmp);
            float[] gains = { rGain, 1f, bGain };
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++)
                    m[i, j] *= gains[j];

            // Контраст вокруг 0.5 + яркость — в последнюю строку.
            float off = 0.5f * (1f - con) + bri;
            for (int j = 0; j < 3; j++)
            {
                for (int i = 0; i < 3; i++)
                    m[i, j] *= con;
                m[4, j] = off;
            }

            // Альфа без изменений, хвост константной строки.
            m[3, 3] = 1f;
            m[4, 4] = 1f;

            var v = new float[25];
            for (int row = 0; row < 5; row++)
                for (int col = 0; col < 5; col++)
                    v[row * 5 + col] = m[row, col];
            return v;
        }

        public static bool Apply(ColorSettings s)
        {
            try
            {
                if (!EnsureInitialized()) return false;
                var t = new MAGCOLOREFFECT { v = BuildMatrix(s) };
                lock (_sync) return MagSetFullscreenColorEffect(ref t);
            }
            catch { return false; }
        }

        public static bool Reset()
        {
            return Apply(ColorSettings.Default);
        }

        /// <summary>Читает текущую матрицу (для тестов/диагностики).</summary>
        public static float[]? GetCurrent()
        {
            try
            {
                if (!EnsureInitialized()) return null;
                var t = new MAGCOLOREFFECT { v = new float[25] };
                lock (_sync)
                {
                    if (!MagGetFullscreenColorEffect(ref t)) return null;
                }
                return t.v;
            }
            catch { return null; }
        }
    }
}
