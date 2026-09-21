using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NekoStrap.Roblox
{
    /// <summary>
    /// Мультиаккаунты как у Voidstrap/RAM-менеджеров: на каждый аккаунт храним
    /// его .ROBLOSECURITY куку локально, в зашифрованном виде (DPAPI текущего
    /// пользователя Windows). Пароли не спрашиваем и не храним вообще, кука
    /// никуда не отправляется кроме api Roblox, никогда не пишется в логи.
    /// Файл: accounts.json в BaseDir. Бэкап — обычная копия файла (работает
    /// только под тем же пользователем Windows, т.к. DPAPI привязан к нему).
    /// </summary>
    public sealed class AccountEntry
    {
        public long UserId { get; set; }
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Alias { get; set; } = "";
        public string CookieProtected { get; set; } = "";
        public DateTime AddedAt { get; set; }
        public DateTime LastUsedAt { get; set; }

        [JsonIgnore]
        public string Label => Alias.Length > 0 ? Alias
            : DisplayName.Length > 0 ? DisplayName
            : Name.Length > 0 ? Name : "id " + UserId;
    }

    public sealed class AccountStore
    {
        public List<AccountEntry> Accounts { get; } = new();
        public long ActiveId { get; set; }

        public AccountEntry? Active
        {
            get
            {
                if (ActiveId <= 0) return null;
                return Accounts.FirstOrDefault(a => a.UserId == ActiveId);
            }
        }

        public static string FilePath => Path.Combine(RobloxPaths.BaseDir, "accounts.json");

        public static AccountStore Load()
        {
            var store = new AccountStore();
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return store;
                var doc = JsonSerializer.Deserialize<StoreDto>(File.ReadAllText(path));
                if (doc == null) return store;
                if (doc.Accounts != null) store.Accounts.AddRange(doc.Accounts);
                store.ActiveId = doc.ActiveId;
                if (store.ActiveId > 0 && store.Active == null)
                    store.ActiveId = 0;
            }
            catch { /* битый файл — начинаем с пустого */ }
            return store;
        }

        public void Save()
        {
            try
            {
                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                var dto = new StoreDto { ActiveId = ActiveId, Accounts = Accounts };
                File.WriteAllText(path, JsonSerializer.Serialize(dto,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Добавляет аккаунт по куке: сначала проверяем куку через users API
        /// (заодно узнаём id/ник), потом шифруем и кладём в стор.
        /// Возвращает entry либо текст ошибки.
        /// </summary>
        public async Task<(AccountEntry? entry, string error)> AddAsync(string cookie, string alias, CancellationToken ct)
        {
            cookie = cookie.Trim();
            if (cookie.Length == 0)
                return (null, "Вставь куку .ROBLOSECURITY.");
            if (cookie.Length < 100)
                return (null, "Кука слишком короткая — похоже, скопировалась не полностью.");

            (long id, string name, string display, string err) = await RobloxAuth.GetAuthenticatedUserAsync(cookie, ct);
            if (err.Length > 0)
                return (null, err);

            string prot;
            try
            {
                prot = Convert.ToBase64String(Dpapi.Protect(System.Text.Encoding.UTF8.GetBytes(cookie)));
            }
            catch (Exception ex)
            {
                return (null, "Не вышло зашифровать куку: " + ex.Message);
            }

            var existing = Accounts.FirstOrDefault(a => a.UserId == id);
            if (existing != null)
            {
                existing.CookieProtected = prot;
                existing.Name = name;
                existing.DisplayName = display;
                if (alias.Trim().Length > 0) existing.Alias = alias.Trim();
                Save();
                return (existing, "");
            }

            var entry = new AccountEntry
            {
                UserId = id,
                Name = name,
                DisplayName = display,
                Alias = alias.Trim(),
                CookieProtected = prot,
                AddedAt = DateTime.UtcNow,
            };
            Accounts.Add(entry);
            if (Accounts.Count == 1)
                ActiveId = id;
            Save();
            return (entry, "");
        }

        public void Remove(AccountEntry entry)
        {
            Accounts.Remove(entry);
            if (ActiveId == entry.UserId)
                ActiveId = Accounts.Count > 0 ? Accounts[0].UserId : 0;
            Save();
        }

        public void SetActive(AccountEntry? entry)
        {
            ActiveId = entry?.UserId ?? 0;
            Save();
        }

        public bool TryGetCookie(AccountEntry entry, out string cookie, out string error)
        {
            cookie = "";
            error = "";
            try
            {
                byte[] raw = Convert.FromBase64String(entry.CookieProtected);
                cookie = System.Text.Encoding.UTF8.GetString(Dpapi.Unprotect(raw));
                if (cookie.Length == 0)
                {
                    error = "Пустая кука после расшифровки.";
                    return false;
                }
                return true;
            }
            catch
            {
                error = "Не расшифровать куку (файл с другого пользователя Windows?). Удали и добавь аккаунт заново.";
                return false;
            }
        }

        /// <summary>Импорт бэкапа: вливаем чужие записи по UserId, своих не трогаем.</summary>
        public (int added, int updated) Import(string path)
        {
            int added = 0, updated = 0;
            var doc = JsonSerializer.Deserialize<StoreDto>(File.ReadAllText(path));
            if (doc?.Accounts == null) return (0, 0);
            foreach (var a in doc.Accounts)
            {
                if (a.UserId <= 0 || a.CookieProtected.Length == 0) continue;
                var existing = Accounts.FirstOrDefault(x => x.UserId == a.UserId);
                if (existing == null)
                {
                    Accounts.Add(a);
                    added++;
                }
                else
                {
                    existing.CookieProtected = a.CookieProtected;
                    if (a.Name.Length > 0) existing.Name = a.Name;
                    if (a.DisplayName.Length > 0) existing.DisplayName = a.DisplayName;
                    updated++;
                }
            }
            if (ActiveId <= 0 && doc.ActiveId > 0 && Accounts.Any(x => x.UserId == doc.ActiveId))
                ActiveId = doc.ActiveId;
            Save();
            return (added, updated);
        }

        private sealed class StoreDto
        {
            public long ActiveId { get; set; }
            public List<AccountEntry>? Accounts { get; set; }
        }
    }

    /// <summary>DPAPI текущего пользователя без внешних пакетов (crypt32 P/Invoke).</summary>
    internal static class Dpapi
    {
        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DATA_BLOB pDataIn, string? szDataDescr, ref DATA_BLOB pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(
            ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, ref DATA_BLOB pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        public static byte[] Protect(byte[] plain)
        {
            var inBlob = new DATA_BLOB { cbData = plain.Length, pbData = Marshal.AllocHGlobal(plain.Length) };
            var entropy = new DATA_BLOB();
            var outBlob = new DATA_BLOB();
            try
            {
                Marshal.Copy(plain, 0, inBlob.pbData, plain.Length);
                if (!CryptProtectData(ref inBlob, null, ref entropy, IntPtr.Zero, IntPtr.Zero,
                        CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
                    throw new InvalidOperationException("CryptProtectData, код " + Marshal.GetLastWin32Error());
                byte[] result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(inBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }

        public static byte[] Unprotect(byte[] cipher)
        {
            var inBlob = new DATA_BLOB { cbData = cipher.Length, pbData = Marshal.AllocHGlobal(cipher.Length) };
            var entropy = new DATA_BLOB();
            var outBlob = new DATA_BLOB();
            try
            {
                Marshal.Copy(cipher, 0, inBlob.pbData, cipher.Length);
                if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero,
                        CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
                    throw new InvalidOperationException("CryptUnprotectData, код " + Marshal.GetLastWin32Error());
                byte[] result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(inBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }
    }
}
