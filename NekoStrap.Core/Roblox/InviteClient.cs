using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace NekoStrap.Roblox
{
    /// <summary>Пропуск на конкретный сервер: код → placeId + jobId.</summary>
    public sealed record InviteCodeInfo(
        string Code,
        long PlaceId,
        string JobId,
        int MaxUses,
        int Uses,
        DateTime ExpiresAt);

    /// <summary>Итог одной операции с кодом. Ошибка — локализуемый код.</summary>
    public sealed record InviteReply(
        bool Ok,
        string Error,
        InviteCodeInfo? Info,
        int Left)
    {
        public static readonly InviteReply Offline = new(false, InviteErrors.Offline, null, -1);
        public static readonly InviteReply Timeout = new(false, InviteErrors.Timeout, null, -1);
        public static readonly InviteReply Cancelled = new(false, InviteErrors.Cancelled, null, -1);
    }

    /// <summary>Коды ошибок инвайтов — их переводит Lang, не сервер.</summary>
    public static class InviteErrors
    {
        public const string Offline = "invite_offline";
        public const string Timeout = "invite_timeout";
        public const string Cancelled = "invite_cancelled";
        public const string Unsupported = "invite_unsupported";
        public const string Gone = "code_gone";
        public const string Expired = "code_expired";
        public const string Used = "code_used";
        public const string BadCode = "bad_code";
        public const string RateLimit = "rate_limit";
        public const string BadPlace = "bad_place";
        public const string CodeFull = "code_full";
        public const string Internal = "invite_internal";
    }

    /// <summary>
    /// Одноразовые запросы к серверу чата по кодам приглашений. Сокет
    /// открывается на время одного запроса и закрывается — коды не зависят
    /// от состояния чата (он может быть выключен настройкой).
    ///
    /// Протокол три в один: create (выдать) → join (узнать сервер, вход не
    /// списывается) → claim (списать вход, когда игрок реально вошёл в игру).
    /// Так упавший запуск не сжигает одноразовый код.
    /// </summary>
    internal static class InviteClient
    {
        /// <summary>Длина кода.</summary>
        public const int CodeLength = 4;

        /// <summary>
        /// Алфавит без 0/O/1/I: на слух и на глаз код должен читаться
        /// однозначно (SHARED по телефону и в дискорде).
        /// </summary>
        public const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

        /// <summary>null = ещё не проверяли, false = сервер старой версии.</summary>
        public static bool? ServerSupported { get; private set; }

        /// <summary>Код в нормальном виде: только наш алфавит, верхний регистр.</summary>
        public static string NormalizeCode(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder(CodeLength);
            foreach (char c in raw.ToUpperInvariant())
            {
                if (Alphabet.IndexOf(c) >= 0) sb.Append(c);
                if (sb.Length == CodeLength) break;
            }
            return sb.ToString();
        }

        public static bool IsValidCode(string raw) => NormalizeCode(raw).Length == CodeLength;

        public static Task<InviteReply> CreateAsync(
            long placeId, string jobId, int maxUses, int ttlSec,
            CancellationToken ct = default)
            => AskAsync(
                new { t = "code_create", place = placeId, job = jobId ?? "", maxUses, ttlSec },
                "code", ct);

        public static Task<InviteReply> PeekAsync(string code, CancellationToken ct = default)
            => AskAsync(new { t = "code_join", code = NormalizeCode(code) }, "code_info", ct);

        /// <summary>Списывает вход. Вызывать только после успешного захода в игру.</summary>
        public static Task<InviteReply> ClaimAsync(string code, CancellationToken ct = default)
            => AskAsync(new { t = "code_claim", code = NormalizeCode(code) }, "code_claimed", ct);

        // ================= Транспорт =================

        private const int TimeoutMs = 10_000;

        private static async Task<InviteReply> AskAsync(object payload, string expect, CancellationToken ct)
        {
            string url = ChatClient.NormalizeUrl(LauncherConfig.DefaultChatServerUrl);
            if (url.Length == 0) return InviteReply.Offline;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeoutMs);

            try
            {
                using var ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await ws.ConnectAsync(new Uri(url), timeout.Token).ConfigureAwait(false);

                byte[] outBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
                await ws.SendAsync(new ArraySegment<byte>(outBytes),
                    WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);

                var reply = await ReadReplyAsync(ws, expect, timeout.Token).ConfigureAwait(false);
                if (reply.Ok) ServerSupported = true;
                return reply;
            }
            catch (OperationCanceledException)
            {
                return ct.IsCancellationRequested ? InviteReply.Cancelled : InviteReply.Timeout;
            }
            catch (WebSocketException)
            {
                return InviteReply.Offline;
            }
            catch (Exception)
            {
                return new(false, InviteErrors.Internal, null, -1);
            }
        }

        private static async Task<InviteReply> ReadReplyAsync(
            ClientWebSocket ws, string expect, CancellationToken ct)
        {
            var buf = new byte[16 * 1024];
            using var ms = new MemoryStream();
            while (true)
            {
                ms.SetLength(0);
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close)
                        return InviteReply.Offline;
                    if (r.MessageType != WebSocketMessageType.Text)
                        continue;
                    ms.Write(buf, 0, r.Count);
                } while (!r.EndOfMessage);

                if (r.MessageType != WebSocketMessageType.Text) continue;

                var reply = ParseReply(ms.ToArray(), expect);
                if (reply != null) return reply;
            }
        }

        /// <summary>null — кадр нас не касается (pong/прочее), читаем дальше.</summary>
        private static InviteReply? ParseReply(byte[] data, string expect)
        {
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                string type = root.TryGetProperty("t", out var t) ? t.GetString() ?? "" : "";

                if (type == "err")
                {
                    string code = root.TryGetProperty("code", out var ce) ? ce.GetString() ?? "" : "";
                    // Старый сервер не знает про коды: сообщаем, что фичи нет,
                    // чтобы UI честно показал «недоступно», а не «не найдено».
                    if (code == "unknown_type") ServerSupported = false;
                    if (code.Length == 0) code = InviteErrors.Internal;
                    return new(false, code, null, -1);
                }
                if (type != expect) return null;
                if (!TryInfo(root, out var info)) return null;

                int left = -1;
                if (type == "code_claimed")
                {
                    left = root.TryGetProperty("left", out var le) && le.TryGetInt32(out int lv) ? lv : -1;
                }
                return new(true, "", info, left);
            }
            catch
            {
                return new(false, InviteErrors.Internal, null, -1);
            }
        }

        private static bool TryInfo(JsonElement root, out InviteCodeInfo info)
        {
            info = null!;
            string code = root.TryGetProperty("code", out var ce) ? ce.GetString() ?? "" : "";
            long place = root.TryGetProperty("place", out var pe) && pe.TryGetInt64(out long pv) ? pv : 0;
            string job = root.TryGetProperty("job", out var je) ? je.GetString() ?? "" : "";
            int maxUses = root.TryGetProperty("maxUses", out var me) && me.TryGetInt32(out int mv) ? mv : 0;
            int uses = root.TryGetProperty("uses", out var ue) && ue.TryGetInt32(out int uv) ? uv : 0;
            DateTime expires = DateTime.MinValue;
            if (root.TryGetProperty("expires", out var xe) && xe.TryGetInt64(out long xv) && xv > 0)
                expires = DateTimeOffset.FromUnixTimeMilliseconds(xv).UtcDateTime;

            if (code.Length == 0) return false;
            // claim подтверждает списание и может не нести place/job.
            if (place == 0 && !root.TryGetProperty("place", out _)) place = 0;
            info = new InviteCodeInfo(code, place, job, maxUses, uses, expires);
            return true;
        }
    }
}
