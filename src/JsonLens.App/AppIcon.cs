using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JsonLens.App;

/// <summary>Draws the "{ }" logo as the window and taskbar icon, so no binary .ico is needed.</summary>
internal static class AppIcon
{
    public static BitmapSource Create(int size = 64)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var accent = new SolidColorBrush(Color.FromRgb(0x0E, 0x7A, 0xCC));
            dc.DrawRoundedRectangle(accent, null, new Rect(0, 0, size, size), size * 0.22, size * 0.22);
            var text = new FormattedText("{ }", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                size * 0.5, Brushes.White, 1.0);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2 - size * 0.02));
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }
}
