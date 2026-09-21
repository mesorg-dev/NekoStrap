namespace NekoStrap.Wpf;

/// <summary>Версия приложения из сборки (для About/футера/проверки обновлений).</summary>
internal static class AppInfo
{
    public static string Version => NekoStrap.Roblox.AppUpdater.CurrentVersion;
}
