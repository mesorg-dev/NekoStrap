using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NekoStrap.Roblox
{
    /// <summary>
    /// Вход по куке и запуск нужного аккаунта — тот же механизм, что у
    /// Voidstrap/RAM-менеджеров (проверено по их открытому коду):
    /// 1) кука проверяется через users API (заодно узнаём id/ник);
    /// 2) по куке берём одноразовый auth-ticket:
    ///    POST auth.roblox.com/v1/authentication-ticket/ + CSRF-ретрай,
    ///    тикет читаем из заголовка rbx-authentication-ticket;
    /// 3) плеер стартует с этим тикетом:
    ///    RobloxPlayerBeta.exe --app -t {ticket} -j "{PlaceLauncher.ashx?...}".
    /// Файлы клиента не трогаем, в реестр не лезем. Тикет и кука в логи не пишутся.
    /// </summary>
    internal static class RobloxAuth
    {
        private static readonly HttpClient _http = BuildClient();

        private static HttpClient BuildClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("NekoStrap/1.0");
            return c;
        }

        /// <summary>Проверка куки: кто это (id/ник/отображаемое имя) или текст ошибки.</summary>
        public static async Task<(long id, string name, string display, string error)> GetAuthenticatedUserAsync(
            string cookie, CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    "https://users.roblox.com/v1/users/authenticated");
                req.Headers.Add("Cookie", ".ROBLOSECURITY=" + cookie);
                using var resp = await _http.SendAsync(req, ct);
                string body = await resp.Content.ReadAsStringAsync(ct);
                if ((int)resp.StatusCode == 401)
                    return (0, "", "", "Кука не подошла (401) — разлогинена или скопирована не полностью.");
                if (!resp.IsSuccessStatusCode)
                    return (0, "", "", $"Roblox вернул {(int)resp.StatusCode} — попробуй позже.");
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                long id = root.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0;
                string name = root.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                string display = root.TryGetProperty("displayName", out var dEl) ? dEl.GetString() ?? "" : "";
                if (id <= 0)
                    return (0, "", "", "Неожиданный ответ users API.");
                return (id, name, display, "");
            }
            catch (TaskCanceledException)
            {
                return (0, "", "", "Таймаут сети — проверь интернет.");
            }
            catch (Exception ex)
            {
                return (0, "", "", "Сеть: " + ex.Message);
            }
        }

        /// <summary>Одноразовый тикет для запуска клиента под этой кукой.</summary>
        public static async Task<(string ticket, string error)> GetAuthTicketAsync(string cookie, CancellationToken ct)
        {
            try
            {
                string? csrf = null;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post,
                        "https://auth.roblox.com/v1/authentication-ticket/");
                    req.Headers.Add("Cookie", ".ROBLOSECURITY=" + cookie);
                    req.Headers.Referrer = new Uri("https://www.roblox.com/");
                    req.Headers.Add("Origin", "https://www.roblox.com");
                    if (csrf != null)
                        req.Headers.Add("X-CSRF-TOKEN", csrf);
                    req.Content = new StringContent("");
                    req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    using var resp = await _http.SendAsync(req, ct);
                    if ((int)resp.StatusCode == 403 &&
                        resp.Headers.TryGetValues("x-csrf-token", out var tokens))
                    {
                        csrf = tokens.FirstOrDefault();
                        if (csrf != null && attempt == 0) continue;
                    }
                    if ((int)resp.StatusCode == 401)
                        return ("", "Кука протухла (401) — обнови её: удали аккаунт и добавь заново.");
                    if (!resp.IsSuccessStatusCode)
                        return ("", $"Тикет не дали ({(int)resp.StatusCode}) — попробуй позже.");
                    if (resp.Headers.TryGetValues("rbx-authentication-ticket", out var tickets))
                    {
                        string t = tickets.FirstOrDefault() ?? "";
                        if (t.Length > 0) return (t, "");
                    }
                    return ("", "В ответе нет тикета.");
                }
                return ("", "CSRF не прошли.");
            }
            catch (TaskCanceledException)
            {
                return ("", "Таймаут сети — проверь интернет.");
            }
            catch (Exception ex)
            {
                return ("", "Сеть: " + ex.Message);
            }
        }

        /// <summary>Join-скрипт как у RAM: RequestJobGame при наличии JobId, иначе RequestGame.</summary>
        public static string BuildJoinScriptUrl(long placeId, string jobId)
        {
            string req = jobId.Length > 0 ? "RequestJobGame" : "RequestGame";
            string url = $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request={req}&placeId={placeId}";
            if (jobId.Length > 0)
                url += "&gameId=" + Uri.EscapeDataString(jobId);
            return url + "&isPlayTogetherGame=false";
        }

        public static Process? LaunchApp(string exePath, string ticket)
        {
            return Start(exePath, $"--app -t {ticket}");
        }

        public static Process? LaunchPlace(string exePath, string ticket, long placeId)
        {
            return Start(exePath, $"--app -t {ticket} -j \"{BuildJoinScriptUrl(placeId, "")}\"");
        }

        public static Process? LaunchServer(string exePath, string ticket, long placeId, string jobId)
        {
            return Start(exePath, $"--app -t {ticket} -j \"{BuildJoinScriptUrl(placeId, jobId)}\"");
        }

        private static Process? Start(string exePath, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exePath, args)
                {
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                    UseShellExecute = false
                };
                return Process.Start(psi);
            }
            catch
            {
                return null;
            }
        }
    }
}
