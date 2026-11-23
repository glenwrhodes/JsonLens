using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using JsonLens.App.Views;

namespace JsonLens.App.Modes;

/// <summary>
/// The real <see cref="MainView"/> plus an overlay for the recording: a mouse pointer, a click ripple,
/// a text caret and a key-press hint. Rendered off-screen by <see cref="FrameRecorder"/>.
/// </summary>
internal sealed class DemoHost : Grid
{
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Path _pointer;
    private readonly Ellipse _ripple;
    private readonly Rectangle _caret;
    private readonly Border _keyHint;
    private readonly TextBlock _keyHintText;

    public DemoHost(MainView view)
    {
        View = view;
        Background = (Brush)Application.Current.FindResource("PanelBrush");
        Children.Add(view);
        Children.Add(_overlay);

        _ripple = new Ellipse
        {
            Stroke = (Brush)Application.Current.FindResource("AccentBrush"),
            StrokeThickness = 2.5,
            Width = 0,
            Height = 0,
            Opacity = 0,
        };
        _overlay.Children.Add(_ripple);

        _caret = new Rectangle { Width = 1.6, Height = 16, Fill = Brushes.White, Visibility = Visibility.Collapsed };
        _overlay.Children.Add(_caret);

        _keyHintText = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        };
        _keyHint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x2B, 0x2B, 0x2E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5E)),
            BorderThickness = new Thickness(1, 1, 1, 3),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 6, 14, 6),
            Child = _keyHintText,
            Opacity = 0,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.5 },
        };
        _overlay.Children.Add(_keyHint);

        _pointer = new Path
        {
            Data = Geometry.Parse("M 0,0 L 0,17.5 L 4.6,13.2 L 7.7,20.2 L 10.6,18.9 L 7.5,12.1 L 13.4,12.1 Z"),
            Fill = Brushes.White,
            Stroke = Brushes.Black,
            StrokeThickness = 1.1,
            StrokeLineJoin = PenLineJoin.Round,
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1.5, Opacity = 0.55 },
        };
        _overlay.Children.Add(_pointer);
        PointerPosition = new Point(800, 520);
    }

    public MainView View { get; }

    private Point _pointerPosition;
    public Point PointerPosition
    {
        get => _pointerPosition;
        set
        {
            _pointerPosition = value;
            Canvas.SetLeft(_pointer, value.X);
            Canvas.SetTop(_pointer, value.Y);
        }
    }

    /// <summary>Ripple progress 0..1 (0 = hidden).</summary>
    public void SetRipple(double t)
    {
        if (t <= 0 || t >= 1)
        {
            _ripple.Opacity = 0;
            return;
        }
        double r = 4 + 18 * t;
        _ripple.Width = _ripple.Height = r * 2;
        Canvas.SetLeft(_ripple, PointerPosition.X - r);
        Canvas.SetTop(_ripple, PointerPosition.Y - r);
        _ripple.Opacity = 0.9 * (1 - t);
    }

    public void SetPressed(bool pressed)
    {
        _pointer.RenderTransform = pressed ? new ScaleTransform(0.88, 0.88) : Transform.Identity;
    }

    /// <summary>Shows a fake text caret at the end of the text box's text (or hides it with null).</summary>
    public void SetCaret(TextBox? box, bool visible)
    {
        if (box is null || !visible)
        {
            _caret.Visibility = Visibility.Collapsed;
            return;
        }
        var rect = box.GetRectFromCharacterIndex(box.Text.Length);
        if (rect.IsEmpty)
        {
            _caret.Visibility = Visibility.Collapsed;
            return;
        }
        var p = box.TranslatePoint(new Point(rect.Left, rect.Top), this);
        _caret.Height = Math.Max(12, rect.Height);
        Canvas.SetLeft(_caret, Math.Round(p.X));
        Canvas.SetTop(_caret, Math.Round(p.Y));
        _caret.Visibility = Visibility.Visible;
    }

    public void SetKeyHint(string? text, double opacity)
    {
        if (text is not null) _keyHintText.Text = text;
        _keyHint.Opacity = opacity;
        // Bottom-left, above the status bar.
        Canvas.SetLeft(_keyHint, 24);
        Canvas.SetTop(_keyHint, ActualHeight - 24 - 24 - 40);
    }

    /// <summary>Centre (or another relative point) of an element, in host coordinates.</summary>
    public Point PointOn(FrameworkElement element, double rx = 0.5, double ry = 0.5) =>
        element.TranslatePoint(new Point(element.ActualWidth * rx, element.ActualHeight * ry), this);
}
