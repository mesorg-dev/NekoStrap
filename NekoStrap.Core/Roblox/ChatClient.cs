using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace NekoStrap.Roblox
{
    public enum ChatState
    {
        Disconnected,
        Connecting,
        Connected,
    }

    /// <summary>Игрок в чате (тот же роблокс-сервер = одна комната).</summary>
    public sealed record ChatUser(long Uid, string Name);

    /// <summary>Сообщение чата: общий канал сервера (To == 0) или ЛС (To > 0).</summary>
    public sealed class ChatMessage
    {
        public long From { get; set; }
        public string Name { get; set; } = "";
        public string Text { get; set; } = "";
        public long To { get; set; }
        public DateTime At { get; set; } = DateTime.UtcNow;
        public bool Mine => SelfUid > 0 && From == SelfUid;

        /// <summary>Uid текущего пользователя — проставляется приемником
        /// (нужно, чтобы отличать свои сообщения без передачи флага).</summary>
        public long SelfUid { get; set; }
    }

    /// <summary>
    /// WebSocket-клиент чата NekoStrap. Комната = placeId_jobId (один роблокс-
    /// сервер): все в комнате видят общий чат и список игроков, ЛС доставляются
    /// только внутри неё. Поднимается только на время живой игровой сессии —
    /// вне игры чат не работает. Автопереподключение с бэкоффом.
    /// </summary>
    internal sealed class ChatClient : IDisposable
    {
        public event Action<ChatState>? StateChanged;
        public event Action<IReadOnlyList<ChatUser>>? UsersChanged;
        public event Action<ChatMessage>? MessageReceived;
        public event Action<string>? ServerError;

        public ChatState State { get; private set; } = ChatState.Disconnected;
        public long SelfUid { get; private set; }
        public string SelfName { get; private set; } = "";

        /// <summary>ЛС разрешены настройкой лаунчера: входящие ЛС при
        /// выключении просто отбрасываются, исходящие не отправляются.</summary>
        public bool DmAllowed { get; set; } = true;

        private readonly SemaphoreSlim _sendLock = new(1, 1);
        // Connect/Disconnect приходят из разных триггеров (сессия, настройки)
        // и гоняются параллельно: без замка второй вызов перетирает _cts,
        // первый RunAsync остаётся жить → два коннекта и дубли сообщений.
        private readonly SemaphoreSlim _connLock = new(1, 1);
        private CancellationTokenSource? _cts;
        private Task? _runTask;
        private ClientWebSocket? _ws;
        private volatile bool _wantConnected;
        private string _url = "";
        private string _room = "";

        // Сервер при каждом join шлёт историю комнаты — после реконнекта она
        // прилетает второй раз. Дедуп по ключу (from|ts|text), окно ~500.
        private readonly Queue<string> _seenKeys = new();
        private readonly HashSet<string> _seenSet = new();

        public async Task ConnectAsync(string url, string room, long uid, string name)
        {
            url = NormalizeUrl(url);
            if (url.Length == 0 || room.Length == 0 || uid <= 0)
            {
                SetState(ChatState.Disconnected);
                return;
            }
            await _connLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await DisconnectCoreAsync().ConfigureAwait(false);
                _url = url;
                _room = room;
                SelfUid = uid;
                SelfName = name.Length > 0 ? name : "Player" + uid % 10000;
                _wantConnected = true;
                _cts = new CancellationTokenSource();
                _runTask = Task.Run(() => RunAsync(_cts.Token));
            }
            finally { _connLock.Release(); }
        }

        public async Task DisconnectAsync()
        {
            await _connLock.WaitAsync().ConfigureAwait(false);
            try { await DisconnectCoreAsync().ConfigureAwait(false); }
            finally { _connLock.Release(); }
        }

        private async Task DisconnectCoreAsync()
        {
            _wantConnected = false;
            var cts = _cts;
            var run = _runTask;
            _cts = null;
            _runTask = null;
            try { cts?.Cancel(); } catch { /* ignore */ }
            try
            {
                if (run != null)
                    await run.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch { /* ignore */ }
            finally
            {
                try { cts?.Dispose(); } catch { /* ignore */ }
                _ws = null;
                SetState(ChatState.Disconnected);
            }
        }

        public Task SendServerAsync(string text) => SendAsync("msg", null, text);

        public Task SendDmAsync(long to, string text)
        {
            if (!DmAllowed || to <= 0)
                return Task.CompletedTask;
            return SendAsync("dm", to, text);
        }

        private async Task SendAsync(string type, long? to, string text)
        {
            text = text.Trim();
            if (text.Length == 0) return;
            if (text.Length > 500) text = text[..500];
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open)
                throw new InvalidOperationException("chat_not_connected");
            object payload = type == "dm"
                ? new { t = "dm", to = to ?? 0, text }
                : new { t = "msg", text };
            await SendJsonAsync(ws, payload, CancellationToken.None).ConfigureAwait(false);
        }

        // ================= Цикл соединения =================

        private async Task RunAsync(CancellationToken ct)
        {
            int delaySec = 1;
            while (_wantConnected && !ct.IsCancellationRequested)
            {
                ClientWebSocket? ws = null;
                try
                {
                    SetState(ChatState.Connecting);
                    ws = new ClientWebSocket();
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                    await ws.ConnectAsync(new Uri(_url), ct).ConfigureAwait(false);
                    _ws = ws;
                    await SendJsonAsync(ws, new
                    {
                        t = "join",
                        room = _room,
                        uid = SelfUid,
                        name = SelfName,
                    }, ct).ConfigureAwait(false);
                    SetState(ChatState.Connected);
                    delaySec = 1;
                    await ReceiveLoopAsync(ws, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // сеть/сервер недоступны — тихо ретраим ниже
                }
                finally
                {
                    _ws = null;
                    try { ws?.Dispose(); } catch { /* ignore */ }
                }

                if (!_wantConnected || ct.IsCancellationRequested) break;
                SetState(ChatState.Disconnected);
                try { await Task.Delay(TimeSpan.FromSeconds(delaySec), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                delaySec = Math.Min(delaySec * 2, 15);
            }
            SetState(ChatState.Disconnected);
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
        {
            var buf = new byte[16 * 1024];
            using var ms = new MemoryStream();
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close)
                        return;
                    if (r.MessageType != WebSocketMessageType.Text)
                        continue;
                    ms.Write(buf, 0, r.Count);
                } while (!r.EndOfMessage);

                if (r.MessageType == WebSocketMessageType.Text)
                    HandleFrame(ms.ToArray());
            }
        }

        private void HandleFrame(byte[] data)
        {
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                string type = root.TryGetProperty("t", out var t) ? t.GetString() ?? "" : "";
                switch (type)
                {
                    case "users":
                    {
                        var list = new List<ChatUser>();
                        if (root.TryGetProperty("list", out var arr) &&
                            arr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var u in arr.EnumerateArray())
                            {
                                long uid = u.TryGetProperty("uid", out var ue) &&
                                    ue.TryGetInt64(out long uv) ? uv : 0;
                                string name = u.TryGetProperty("name", out var ne)
                                    ? ne.GetString() ?? "" : "";
                                if (uid > 0) list.Add(new ChatUser(uid, name));
                            }
                        }
                        UsersChanged?.Invoke(list);
                        break;
                    }
                    case "msg":
                    case "dm":
                    {
                        var msg = new ChatMessage
                        {
                            From = root.TryGetProperty("from", out var fe) &&
                                fe.TryGetInt64(out long fv) ? fv : 0,
                            Name = root.TryGetProperty("name", out var me)
                                ? me.GetString() ?? "" : "",
                            Text = root.TryGetProperty("text", out var te)
                                ? te.GetString() ?? "" : "",
                            To = root.TryGetProperty("to", out var toe) &&
                                toe.TryGetInt64(out long tv) ? tv : 0,
                            At = root.TryGetProperty("ts", out var se) &&
                                se.TryGetInt64(out long sv) && sv > 0
                                ? DateTimeOffset.FromUnixTimeMilliseconds(sv).UtcDateTime
                                : DateTime.UtcNow,
                            SelfUid = SelfUid,
                        };
                        // ЛС выключены в настройках — входящие не показываем.
                        if (msg.To > 0 && !DmAllowed) break;
                        if (msg.Text.Length == 0) break;
                        if (IsDuplicate(msg)) break;
                        MessageReceived?.Invoke(msg);
                        break;
                    }
                    case "err":
                    {
                        string text = root.TryGetProperty("text", out var ee)
                            ? ee.GetString() ?? "" : "";
                        if (text.Length > 0) ServerError?.Invoke(text);
                        break;
                    }
                    // welcome / pong — служебные, UI не нужны
                }
            }
            catch { /* битый кадр — игнорируем */ }
        }

        private static string MsgKey(ChatMessage m) =>
            m.From + "|" + m.To + "|" + m.At.Ticks + "|" + m.Text;

        /// <summary>Видели такое сообщение уже (история после реконнекта).</summary>
        private bool IsDuplicate(ChatMessage m)
        {
            string key = MsgKey(m);
            if (!_seenSet.Add(key)) return true;
            _seenKeys.Enqueue(key);
            while (_seenKeys.Count > 500)
                _seenSet.Remove(_seenKeys.Dequeue());
            return false;
        }

        private static async Task SendJsonAsync(ClientWebSocket ws, object payload, CancellationToken ct)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            // Один ws = один параллельный Send (требование WebSocket).
            await ws.SendAsync(new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }

        private void SetState(ChatState state)
        {
            if (State == state) return;
            State = state;
            try { StateChanged?.Invoke(state); } catch { /* ignore */ }
        }

        /// <summary>
        /// Приводит введённый адрес к ws://…/ws: принимает http(s):// и голый
        /// хост, добивает путь /ws. Пусто = дефолт (localhost).
        /// </summary>
        public static string NormalizeUrl(string url)
        {
            url = (url ?? "").Trim();
            if (url.Length == 0) return "ws://localhost:8787/ws";
            if (!url.Contains("://"))
                url = "ws://" + url;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                url = "ws://" + url["http://".Length..];
            else if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "wss://" + url["https://".Length..];
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return "";
            string path = uri.AbsolutePath.TrimEnd('/');
            if (path.Length == 0 || path == "/")
            {
                var builder = new UriBuilder(uri) { Path = "/ws" };
                return builder.Uri.ToString();
            }
            return uri.ToString();
        }

        public void Dispose()
        {
            try { DisconnectAsync().GetAwaiter().GetResult(); } catch { /* ignore */ }
            try { _sendLock.Dispose(); } catch { /* ignore */ }
        }
    }
}
