namespace NekoStrap.Roblox
{
    /// <summary>
    /// FPS-анлокер без инъекций: лимит через флаг DFIntTaskSchedulerTargetFps
    /// (тот же метод, что у Bloxstrap/Voidstrap). Вживляется в ClientAppSettings
    /// поверх флагов пользователя при каждом запуске.
    /// </summary>
    internal static class FpsManager
    {
        public const string FlagName = "DFIntTaskSchedulerTargetFps";

        /// <summary>limit &lt;= 0 — убрать флаг (стоковые 60 FPS).</summary>
        public static void Apply(string? versionGuid, int limit)
        {
            try
            {
                var flags = FastFlagStore.Load(versionGuid);
                if (limit <= 0)
                {
                    if (!flags.Remove(FlagName)) return;
                }
                else
                {
                    string want = limit.ToString();
                    if (flags.TryGetValue(FlagName, out var cur) && cur == want) return;
                    flags[FlagName] = want;
                }
                FastFlagStore.Save(versionGuid, flags);
            }
            catch { /* без установки нечего делать */ }
        }
    }
}
