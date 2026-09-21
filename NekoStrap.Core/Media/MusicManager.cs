using NAudio.Wave;

namespace NekoStrap.Media
{
    public sealed record MusicTrack(string Path, string Title);

    /// <summary>
    /// NekoStrap Music — свой плеер в духе плеера Voidstrap, но наш:
    /// треки из папки Music (кидай mp3/wav/m4a), шаффл, громкость,
    /// библиотека и состояние сохраняются в music.json.
    /// Управление — из трея. NAudio под капотом, как у них.
    /// </summary>
    internal sealed class MusicManager : IDisposable
    {
        private static readonly string[] Extensions =
            { ".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac" };

        private WaveOutEvent? _out;
        private AudioFileReader? _reader;
        private bool _manualStop;
        private bool _disposed;
        private readonly Random _rng = new();

        public List<MusicTrack> Tracks { get; private set; } = new();
        public int CurrentIndex { get; private set; } = -1;
        public bool IsPlaying { get; private set; }
        public bool Shuffle { get; set; } = true;
        public float Volume { get; private set; } = 0.8f;

        public string MusicDir => Path.Combine(Roblox.RobloxPaths.BaseDir, "Music");
        private string StatePath => Path.Combine(MusicDir, "music.json");

        public string CurrentTitle =>
            CurrentIndex >= 0 && CurrentIndex < Tracks.Count ? Tracks[CurrentIndex].Title : "";

        public event EventHandler? StateChanged;

        public void Refresh()
        {
            var list = new List<MusicTrack>();
            try
            {
                Directory.CreateDirectory(MusicDir);
                foreach (var f in Directory.GetFiles(MusicDir, "*.*", SearchOption.TopDirectoryOnly))
                {
                    if (!Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
                    list.Add(new MusicTrack(f, Path.GetFileNameWithoutExtension(f)));
                }
            }
            catch { /* ignore */ }
            list.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            // Не теряем текущий трек при совпадении пути.
            string cur = CurrentTitle != "" && CurrentIndex >= 0 && CurrentIndex < Tracks.Count
                ? Tracks[CurrentIndex].Path : "";
            Tracks = list;
            CurrentIndex = list.FindIndex(t => t.Path.Equals(cur, StringComparison.OrdinalIgnoreCase));
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void PlayPause()
        {
            try
            {
                if (IsPlaying && _out != null)
                {
                    _out.Pause();
                    IsPlaying = false;
                }
                else if (_reader != null && _out != null)
                {
                    _out.Play();
                    IsPlaying = true;
                }
                else
                {
                    PlayAt(CurrentIndex >= 0 ? CurrentIndex : 0);
                    return;
                }
                SaveState();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch { /* ignore */ }
        }

        public void PlayAt(int index)
        {
            try
            {
                if (Tracks.Count == 0 || index < 0 || index >= Tracks.Count) return;
                StopInternal();
                CurrentIndex = index;
                _reader = new AudioFileReader(Tracks[index].Path) { Volume = Volume };
                _out = new WaveOutEvent();
                _out.PlaybackStopped += (_, _) => OnEnded();
                _out.Init(_reader);
                _out.Play();
                IsPlaying = true;
                SaveState();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch { /* битый файл — пропускаем */ }
        }

        public void Next()
        {
            if (Tracks.Count == 0) return;
            if (Tracks.Count == 1) { PlayAt(0); return; }
            int next;
            if (Shuffle)
            {
                do { next = _rng.Next(Tracks.Count); }
                while (next == CurrentIndex);
            }
            else
            {
                next = (CurrentIndex + 1) % Tracks.Count;
            }
            PlayAt(next);
        }

        public void Prev()
        {
            if (Tracks.Count == 0) return;
            if (_reader != null && _reader.CurrentTime > TimeSpan.FromSeconds(3))
            {
                try { _reader.CurrentTime = TimeSpan.Zero; } catch { /* ignore */ }
                return;
            }
            int prev = CurrentIndex <= 0 ? Tracks.Count - 1 : CurrentIndex - 1;
            PlayAt(prev);
        }

        public void Stop()
        {
            _manualStop = true;
            try
            {
                StopInternal();
                IsPlaying = false;
                SaveState();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            finally { _manualStop = false; }
        }

        public void SetVolume(float v)
        {
            Volume = Math.Clamp(v, 0f, 1f);
            try
            {
                if (_reader != null) _reader.Volume = Volume;
            }
            catch { /* ignore */ }
            SaveState();
        }

        private void OnEnded()
        {
            if (_manualStop || _disposed) return;
            IsPlaying = false;
            // Следующий трек — в пуле, чтобы не дёргать UI-поток из колбэка NAudio.
            Task.Run(() =>
            {
                try { Thread.Sleep(150); } catch { /* ignore */ }
                if (_disposed || _manualStop) return;
                try { Next(); } catch { /* ignore */ }
            });
        }

        private void StopInternal()
        {
            try { _out?.Stop(); } catch { /* ignore */ }
            try { _out?.Dispose(); } catch { /* ignore */ }
            try { _reader?.Dispose(); } catch { /* ignore */ }
            _out = null;
            _reader = null;
        }

        public void LoadState()
        {
            try
            {
                Refresh();
                if (!File.Exists(StatePath)) return;
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(StatePath));
                var r = doc.RootElement;
                if (r.TryGetProperty("Volume", out var v)) Volume = Math.Clamp((float)v.GetDouble(), 0f, 1f);
                if (r.TryGetProperty("Shuffle", out var s)) Shuffle = s.GetBoolean();
                if (r.TryGetProperty("Current", out var c))
                {
                    string p = c.GetString() ?? "";
                    int i = Tracks.FindIndex(t => t.Path.Equals(p, StringComparison.OrdinalIgnoreCase));
                    if (i >= 0) CurrentIndex = i;
                }
            }
            catch { /* ignore */ }
        }

        private void SaveState()
        {
            try
            {
                string cur = CurrentIndex >= 0 && CurrentIndex < Tracks.Count
                    ? Tracks[CurrentIndex].Path : "";
                File.WriteAllText(StatePath,
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        Volume = Volume,
                        Shuffle = Shuffle,
                        Current = cur
                    }));
            }
            catch { /* ignore */ }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _manualStop = true;
            StopInternal();
        }
    }
}
