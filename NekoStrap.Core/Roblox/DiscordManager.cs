using System.Diagnostics;
using DiscordRPC;

namespace NekoStrap.Roblox
{
    /// <summary>Что показывать в Discord-статусе (все галки — из настроек).</summary>
    internal sealed class DiscordOptions
    {
        public bool AllowJoin { get; set; } = true;
        public bool ShowName { get; set; } = true;
        public bool ShowServer { get; set; } = true;
        public bool ShowIcon { get; set; } = true;
        public bool ShowElapsed { get; set; } = true;
        public bool ShowButton { get; set; } = true;
        public string AssetKey { get; set; } = "";
    }

    /// <summary>
    /// Discord Rich Presence через DiscordRichPresence (NuGet, чистый C# без нативных dll).
    /// Показывает игру/сервер, кнопка Join у друзей заходит на тот же сервер
    /// через roblox://placeId + gameInstanceId (механика как у Voidstrap).
    /// ID приложения зашит в коде (LauncherConfig.DefaultDiscordAppId).
    /// </summary>
    internal sealed class DiscordManager : IDisposable
    {
        private DiscordRpcClient? _client;
        private string _appId = "";
        private DiscordOptions _opts = new();
        private DateTime _sessionStart = DateTime.UtcNow;
        private readonly System.Threading.Timer _retryTimer;
        private bool _disposed;

        // Последняя сессия — чтобы перерисовать статус сразу после смены галок.
        private string _lastGame = "";
        private string _lastServer = "";
        private long _lastPlace;
        private string _lastJob = "";
        private string _lastLabel = "";
        private string _iconUrl = "";
        private bool _hasSession;

        public bool IsRunning => _client?.IsInitialized ?? false;

        public DiscordManager()
        {
            _retryTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    if (!_disposed && _client != null && !_client.IsInitialized && _appId.Length > 0)
                        _client.Initialize();
                }
                catch { /* Discord закрыт — попробуем позже */ }
            }, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>Подключение/переключение настроек: реконнект только при смене ID.</summary>
        public void Configure(string appId, DiscordOptions opts)
        {
            bool reconnect = appId.Trim() != _appId;
            _appId = appId.Trim();
            _opts = opts;
            if (!reconnect)
            {
                Refresh(); // флаги видимости применяются сразу, без переподключения
                return;
            }
            Shutdown();
            if (_appId.Length > 0)
                Start();
        }

        /// <summary>Перерисовать текущий статус с новыми галками (без сброса таймера).</summary>
        public void Refresh()
        {
            if (_hasSession) Set(Build());
        }

        private void Start()
        {
            try
            {
                Shutdown();
                var client = new DiscordRpcClient(_appId);
                client.OnJoin += (_, e) => HandleJoin(e.Secret);
                _client = client;
                if (client.Initialize())
                {
                    try { client.Subscribe(EventType.Join); } catch { /* ignore */ }
                    SetIdle();
                }
                _retryTimer.Change(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
            }
            catch
            {
                _client = null;
                _retryTimer.Change(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
            }
        }

        private static void HandleJoin(string secret)
        {
            try
            {
                // Секрет формата "placeId|jobId".
                var parts = secret.Split('|');
                if (parts.Length != 2) return;
                if (!long.TryParse(parts[0], out long placeId) || placeId <= 0) return;
                string url = string.IsNullOrWhiteSpace(parts[1])
                    ? $"roblox://placeId={placeId}"
                    : $"roblox://placeId={placeId}&gameInstanceId={parts[1]}";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { /* ignore */ }
        }

        public void SetIdle()
        {
            _hasSession = false;
            _sessionStart = DateTime.UtcNow;
            _iconUrl = "";
            Set(new RichPresence
            {
                Details = "NekoStrap",
                State = "Выбирает игру",
                Timestamps = Timestamps.Now
            });
        }

        /// <summary>
        /// Присутствие в игре. Кнопка «Открыть игру» ставится только когда
        /// Join выключен: Discord не показывает кнопки вместе с секретами,
        /// тогда кнопку Join рисует сам клиент (видна друзьям в их Discord).
        /// </summary>
        public void SetSession(string gameName, string serverText, long placeId,
            string jobId, string openGameLabel = "")
        {
            _lastGame = gameName;
            _lastServer = serverText;
            _lastPlace = placeId;
            _lastJob = jobId;
            _lastLabel = openGameLabel;
            _sessionStart = DateTime.UtcNow;
            _iconUrl = "";          // у нового плейса своя иконка
            _hasSession = true;
            Set(Build());
        }

        private RichPresence Build()
        {
            string name = _lastGame.Length > 0 ? _lastGame : "Roblox";
            string? details = _opts.ShowName && _lastGame.Length > 0 ? _lastGame : null;
            string? state = _opts.ShowServer && _lastServer.Length > 0 ? _lastServer : null;
            if (details == null && state == null) details = "Roblox"; // Discord требует хоть строку

            var presence = new RichPresence
            {
                Details = details,
                State = state,
                Timestamps = _opts.ShowElapsed ? new Timestamps(_sessionStart) : null,
                Party = new Party
                {
                    ID = _lastJob.Length > 0 ? _lastJob : _lastPlace.ToString(),
                    Size = 1,
                    Max = 50
                }
            };

            string image = _opts.ShowIcon
                ? (_iconUrl.Length > 0 ? _iconUrl : _opts.AssetKey.Trim())
                : "";
            if (image.Length > 0)
                presence.Assets = new Assets { LargeImageKey = image, LargeImageText = name };

            if (_opts.AllowJoin && _lastPlace > 0)
                presence.Secrets = new Secrets { JoinSecret = $"{_lastPlace}|{_lastJob}" };
            else if (_opts.ShowButton && _lastPlace > 0 && _lastLabel.Trim().Length > 0)
                presence.Buttons = new[]
                {
                    new Button
                    {
                        Label = _lastLabel.Trim(),
                        Url = $"https://www.roblox.com/games/{_lastPlace}"
                    }
                };
            return presence;
        }

        /// <summary>
        /// Картинка плейса: URL из thumbnails API или ключ из Assets приложения —
        /// вписываем в текущий статус сразу, без ожидания следующего обновления.
        /// </summary>
        public void UpdateLargeAsset(string keyOrUrl, string tooltip)
        {
            if (keyOrUrl.Trim().Length == 0) return;
            _iconUrl = keyOrUrl.Trim();
            if (_hasSession) Set(Build());
        }

        private void Set(RichPresence presence)
        {
            try { _client?.SetPresence(presence); } catch { /* ignore */ }
        }

        public void Shutdown()
        {
            _retryTimer.Change(Timeout.Infinite, Timeout.Infinite);
            try
            {
                _client?.ClearPresence();
                _client?.Deinitialize();
                _client?.Dispose();
            }
            catch { /* ignore */ }
            _client = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Shutdown();
            _retryTimer.Dispose();
        }
    }
}
