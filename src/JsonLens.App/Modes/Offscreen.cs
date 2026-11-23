using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace JsonLens.App.Modes;

/// <summary>Lays out and renders WPF elements that are not in any window.</summary>
internal static class Offscreen
{
    public static void Layout(FrameworkElement element, Size size)
    {
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();
    }

    /// <summary>Runs queued dispatcher work (deferred scrolls, bindings) and lays out again.</summary>
    public static void Settle(FrameworkElement element, Size size)
    {
        Layout(element, size);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Layout(element, size);
    }

    public static RenderTargetBitmap Render(Visual visual, int width, int height)
    {
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>Counts distinct colours in a sample of pixels: a blank or broken render has very few.</summary>
    public static int DistinctColours(BitmapSource bmp)
    {
        int stride = bmp.PixelWidth * 4;
        var pixels = new byte[stride * bmp.PixelHeight];
        bmp.CopyPixels(pixels, stride, 0);
        var set = new HashSet<int>();
        for (int i = 0; i < pixels.Length; i += 4 * 7)
            set.Add(BitConverter.ToInt32(pixels, i));
        return set.Count;
    }
}
