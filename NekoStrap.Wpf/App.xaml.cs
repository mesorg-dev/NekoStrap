using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using NekoStrap.Utils;

namespace NekoStrap.Wpf;

/// <summary>
/// Точка входа: строго один экземпляр (второй запуск будит первое окно).
/// Логика — копия WinForms Program.cs.
/// </summary>
public partial class App : Application
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "NekoStrap.SingleInstance.v1", out bool created);
        if (!created)
        {
            ActivateExisting();
            Shutdown();
            return;
        }
        base.OnStartup(e);
        RegisterUiSounds();
        // Краш-репорт вместо молчаливого падения: следующий баг сам о себе расскажет.
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    /// <summary>
    /// Все UI-звуки из WinForms-версии: клики кнопок, тики наведения,
    /// «токи» тумблеров, шелест перехода по вкладкам.
    /// </summary>
    private static void RegisterUiSounds()
    {
        // Клик — все кнопки, кроме тумблеров (у тех свой звук).
        // RadioButton сайдбара тоже кликает (переход озвучен отдельно).
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent,
            new RoutedEventHandler((s, _) =>
            {
                if (s is CheckBox) return;
                ClickSound.PlayClick();
            }));
        // Тумблеры — «ток» вкл/выкл.
        EventManager.RegisterClassHandler(typeof(CheckBox), ToggleButton.CheckedEvent,
            new RoutedEventHandler((_, _) => ClickSound.PlayToggle(true)));
        EventManager.RegisterClassHandler(typeof(CheckBox), ToggleButton.UncheckedEvent,
            new RoutedEventHandler((_, _) => ClickSound.PlayToggle(false)));
        // Переход по вкладкам — шелест.
        EventManager.RegisterClassHandler(typeof(RadioButton), ToggleButton.CheckedEvent,
            new RoutedEventHandler((_, _) => ClickSound.PlayOpen()));
        // Наведение — тихий тик (троттлинг внутри PlayHover).
        EventManager.RegisterClassHandler(typeof(UIElement), UIElement.MouseEnterEvent,
            new MouseEventHandler((s, _) =>
            {
                if (s is Button && s is not ToggleButton)
                    ClickSound.PlayHover();
                else if (s is RadioButton)
                    ClickSound.PlayHover();
            }));
    }

    private static DateTime _lastErrorShown = DateTime.MinValue;

    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Глушим каскад: один диалог за 3 секунды (ошибка в шаблоне иначе
        // спамит на каждую попытку layout'а — десятки окон).
        e.Handled = true;
        if (DateTime.UtcNow - _lastErrorShown < TimeSpan.FromSeconds(3)) return;
        _lastErrorShown = DateTime.UtcNow;
        try
        {
            MessageBox.Show(Describe(e.Exception), "NekoStrap — ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { /* ignore */ }
    }

    private static void OnDomainException(object? sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            string text = e.ExceptionObject is Exception ex ? Describe(ex) : "Неизвестная ошибка.";
            MessageBox.Show(text, "NekoStrap — ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { /* ignore */ }
    }

    private static string Describe(Exception ex)
    {
        string trace = ex.StackTrace ?? "";
        string[] lines = trace.Split('\n');
        string shortTrace = string.Join("\n", lines.Take(6)).Trim();
        return $"{ex.GetType().Name}: {ex.Message}\n\n{shortTrace}";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static void ActivateExisting()
    {
        try
        {
            IntPtr hwnd = FindWindow(null, "NekoStrap");
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SW_RESTORE);
                ShowWindow(hwnd, SW_SHOW);
                SetForegroundWindow(hwnd);
            }
        }
        catch { /* ignore */ }
    }
}
