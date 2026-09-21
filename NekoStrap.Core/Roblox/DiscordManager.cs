using System.Diagnostics;
using DiscordRPC;

namespace NekoStrap.Roblox
{
    /// <summary>
    /// Discord Rich Presence через DiscordRichPresence (NuGet, чистый C# без нативных dll).
    /// Показывает игру/сервер, кнопка Join у друзей заходит на тот же сервер
    /// через roblox://placeId + gameInstanceId (механика как у Voidstrap).
    /// Нужен свой Application ID из discord.com/developers (бесплатно, 2 минуты).
    /// </summary>
    internal sealed class DiscordManager : IDisposable
    {
        private DiscordRpcClient? _client;
        private string _appId = "";
        private bool _allowJoin = true;
        private DateTime _sessionStart = DateTime.UtcNow;
        private readonly System.Threading.Timer _retryTimer;
        private bool _disposed;

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

        public void Configure(string appId, bool allowJoin)
        {
            bool changed = appId.Trim() != _appId || allowJoin != _allowJoin;
            _appId = appId.Trim();
            _allowJoin = allowJoin;
            if (!changed) return;
            Shutdown();
            if (_appId.Length > 0)
                Start();
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
            _sessionStart = DateTime.UtcNow;
            Set(new RichPresence
            {
                Details = "NekoStrap",
                State = "Выбирает игру",
                Timestamps = Timestamps.Now
            });
        }

        public void SetSession(string gameName, string serverText, long placeId, string jobId)
        {
            _sessionStart = DateTime.UtcNow;
            var presence = new RichPresence
            {
                Details = gameName.Length > 0 ? gameName : "Roblox",
                State = serverText,
                Timestamps = new Timestamps(_sessionStart),
                Party = new Party { ID = jobId.Length > 0 ? jobId : placeId.ToString(), Size = 1, Max = 50 }
            };
            if (_allowJoin && placeId > 0)
                presence.Secrets = new Secrets { JoinSecret = $"{placeId}|{jobId}" };
            Set(presence);
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
