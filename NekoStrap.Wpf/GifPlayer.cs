using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace NekoStrap.Wpf;

/// <summary>
/// Анимированные GIF-обои без сторонних пакетов: кадры качает GDI+
/// (ImageAnimator — он же знает задержки кадров), каждый кадр кладём
/// в Image.Source. Блюр/оверлей окна применяются сами (это эффекты
/// элемента, а не картинки). Поток не лочим — грузим через память.
/// GDI-утечек нет: каждый HBITMAP удаляем сразу после конвертации.
/// </summary>
internal sealed class GifPlayer : IDisposable
{
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private readonly System.Drawing.Image _image;
    private readonly MemoryStream _backing;
    private readonly Image _target;
    private bool _running;
    private bool _disposed;

    public bool IsAnimated { get; }

    private GifPlayer(System.Drawing.Image image, MemoryStream backing, Image target)
    {
        _image = image;
        _backing = backing;
        _target = target;
        IsAnimated = System.Drawing.ImageAnimator.CanAnimate(_image);
    }

    /// <summary>
    /// Создаёт плеер (и сразу показывает первый кадр). null — файл не GIF/битый.
    /// Анимация стартует методом Start (чтобы окно успело подписаться).
    /// </summary>
    public static GifPlayer? TryCreate(string path, Image target)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var backing = new MemoryStream(File.ReadAllBytes(path));
            var image = System.Drawing.Image.FromStream(backing);
            var player = new GifPlayer(image, backing, target);
            player.RenderFrame();
            return player;
        }
        catch
        {
            return null;
        }
    }

    public void Start()
    {
        if (_disposed || _running || !IsAnimated) return;
        _running = true;
        try { System.Drawing.ImageAnimator.Animate(_image, OnFrame); }
        catch { _running = false; }
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        try { System.Drawing.ImageAnimator.StopAnimate(_image, OnFrame); } catch { /* ignore */ }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_disposed || !_running) return;
        try
        {
            // Кадр переключаем на UI-потоке (там же живёт Image).
            _ = _target.Dispatcher.BeginInvoke(new Action(RenderFrame));
        }
        catch { /* ignore */ }
    }

    private void RenderFrame()
    {
        if (_disposed) return;
        try
        {
            System.Drawing.ImageAnimator.UpdateFrames(_image);
            IntPtr h = ((System.Drawing.Bitmap)_image).GetHbitmap();
            try
            {
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    h, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                _target.Source = src;
            }
            finally
            {
                DeleteObject(h);
            }
        }
        catch { /* ignore — оставим предыдущий кадр */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        try { _image.Dispose(); } catch { /* ignore */ }
        try { _backing.Dispose(); } catch { /* ignore */ }
    }
}
