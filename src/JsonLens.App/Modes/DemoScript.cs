using System.Windows;
using System.Windows.Controls;
using JsonLens.App.ViewModels;
using JsonLens.App.Views;
using JsonLens.Core.Tree;

namespace JsonLens.App.Modes;

/// <summary>
/// The ~40 second demo: open the large file, browse the tree, search, run JSONPath filters,
/// copy a path, switch pretty/minified, collapse. Each enumerated item is one video frame.
/// Actions go through the same view-model calls the real buttons and keys use.
/// </summary>
internal sealed class DemoScript
{
    public const double TargetSeconds = 40;

    private readonly DemoHost _host;
    private readonly MainViewModel _vm;
    private readonly int _fps;
    private readonly Size _size;
    private readonly string _demoPath;

    private int _frames;
    private FrameworkElement? _hot;
    private TextBox? _focusBox;
    private int _typingUntilFrame;
    private int _rippleStart = -1000;
    private string? _keyHint;
    private int _keyHintStart = -1000;
    private int _keyHintFrames;

    public DemoScript(DemoHost host, MainViewModel vm, int fps, Size size, string demoPath)
    {
        _host = host;
        _vm = vm;
        _fps = fps;
        _size = size;
        _demoPath = demoPath;
    }

    private MainView View => _host.View;

    public IEnumerable<int> Run()
    {
        foreach (var step in Steps())
            foreach (var f in step)
                yield return f;

        // Pad to the target length with a still ending.
        int target = Frames(TargetSeconds);
        while (_frames < target)
            foreach (var f in OneFrame())
                yield return f;
    }

    private IEnumerable<IEnumerable<int>> Steps()
    {
        // 1. Empty window, then open the big demo file.
        yield return Hold(0.8);
        yield return MoveTo(() => _host.PointOn(View.EmptyDemoButton), 0.9, hot: () => View.EmptyDemoButton);
        yield return Click(() => _vm.OpenFileAsync(_demoPath).GetAwaiter().GetResult());
        yield return Hold(1.4);

        // 2. Browse: orders -> [0 … 9999] -> [0 … 99] -> [2] -> items, then select values.
        yield return ExpandRow(() => Row("orders", 1), 0.8);
        yield return Hold(0.5);
        yield return ExpandRow(() => Row("[0 … 9999]", 2), 0.5);
        yield return Hold(0.35);
        yield return ExpandRow(() => Row("[0 … 99]", 3), 0.5);
        yield return Hold(0.35);
        yield return ExpandRow(() => Row("[2]", 4), 0.5);
        yield return Hold(0.5);
        yield return ExpandRow(() => Row("items", 5), 0.5);
        yield return Hold(0.6);
        yield return SelectRow(() => Row("total", 5), 0.6);
        yield return Hold(0.9);
        yield return SelectRow(() => Row("[2]", 4), 0.6);
        yield return Hold(1.0);

        // 3. Search, then step through matches with Enter.
        yield return MoveTo(() => _host.PointOn(View.SearchBox, 0.35), 0.7, hot: () => View.SearchBox);
        yield return Click(() => Focus(View.SearchBox));
        yield return Type(View.SearchBox, s => _vm.SearchText = s, "moose is friendly", 15);
        yield return Hold(1.0);
        yield return PressKey("Enter", _vm.NextMatch);
        yield return Hold(0.9);
        yield return PressKey("Enter", _vm.NextMatch);
        yield return Hold(0.9);

        // 4. JSONPath filter, then open a result.
        yield return MoveTo(() => _host.PointOn(View.QueryBox, 0.3), 0.8, hot: () => View.QueryBox);
        yield return Click(() => Focus(View.QueryBox));
        yield return Type(View.QueryBox, s => _vm.QueryText = s, "$.orders[?@.total > 900 && @.status == 'delivered']", 26);
        yield return Hold(0.4);
        yield return PressKey("Enter", () => _vm.RunQueryAsync().GetAwaiter().GetResult());
        yield return Hold(1.2);
        yield return MoveTo(() => _host.PointOn(Result(2), 0.3), 0.8, hot: () => Result(2));
        yield return Click(() => { Focus(null); _vm.SelectedResult = _vm.QueryResults[2]; });
        yield return Hold(1.3);

        // 5. Copy the path.
        yield return MoveTo(() => _host.PointOn(View.CopyPathButton), 0.7, hot: () => View.CopyPathButton);
        yield return Click(() => _vm.CopyPathCommand.Execute(null));
        yield return Hold(1.6);

        // 6. Minify, then back to pretty.
        yield return MoveTo(() => _host.PointOn(View.MinifyToggle), 0.6, hot: () => View.MinifyToggle);
        yield return Click(() => _vm.IsMinified = true);
        yield return Hold(1.3);
        yield return MoveTo(() => _host.PointOn(View.PrettyToggle), 0.45, hot: () => View.PrettyToggle);
        yield return Click(() => _vm.IsMinified = false);
        yield return Hold(1.0);

        // 7. A recursive-descent filter over customers.
        yield return MoveTo(() => _host.PointOn(View.QueryBox, 0.5), 0.7, hot: () => View.QueryBox);
        yield return Click(() => { Focus(View.QueryBox); _vm.QueryText = ""; });
        yield return Type(View.QueryBox, s => _vm.QueryText = s, "$..customers[?@.tier == 'gold'].name", 26);
        yield return PressKey("Enter", () => _vm.RunQueryAsync().GetAwaiter().GetResult());
        yield return Hold(0.9);
        yield return MoveTo(() => _host.PointOn(Result(0), 0.3), 0.7, hot: () => Result(0));
        yield return Click(() => { Focus(null); _vm.SelectedResult = _vm.QueryResults[0]; });
        yield return Hold(1.2);

        // 8. Collapse everything and rest.
        yield return MoveTo(() => _host.PointOn(View.CollapseAllButton), 0.8, hot: () => View.CollapseAllButton);
        yield return Click(() => _vm.CollapseAllCommand.Execute(null));
        yield return Hold(0.6);
        yield return MoveTo(() => new Point(_size.Width * 0.46, _size.Height * 0.62), 0.8, hot: null);
    }

    // ================================================================ building blocks

    private int Frames(double seconds) => (int)Math.Round(seconds * _fps);

    private IEnumerable<int> OneFrame()
    {
        _frames++;
        yield return _frames;
    }

    private IEnumerable<int> Hold(double seconds)
    {
        int n = Frames(seconds);
        for (int i = 0; i < n; i++)
            foreach (var f in OneFrame())
                yield return f;
    }

    private IEnumerable<int> MoveTo(Func<Point> target, double seconds, Func<FrameworkElement?>? hot)
    {
        SetHot(null);
        Settle();
        var from = _host.PointerPosition;
        var to = target();
        int n = Math.Max(1, Frames(seconds));
        for (int i = 1; i <= n; i++)
        {
            double t = (double)i / n;
            double e = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; // ease in-out cubic
            double arc = Math.Sin(Math.PI * t) * Math.Min(40, (to - from).Length * 0.08);
            _host.PointerPosition = new Point(from.X + (to.X - from.X) * e, from.Y + (to.Y - from.Y) * e - arc);
            foreach (var f in OneFrame()) yield return f;
        }
        SetHot(hot?.Invoke());
    }

    private IEnumerable<int> Click(Action action)
    {
        _host.SetPressed(true);
        _rippleStart = _frames;
        foreach (var f in Hold(0.07)) yield return f;
        action();
        _host.SetPressed(false);
        Settle();
        foreach (var f in Hold(0.2)) yield return f;
    }

    private IEnumerable<int> Type(TextBox box, Action<string> set, string text, double charsPerSecond)
    {
        Focus(box);
        int start = _frames;
        for (int i = 1; i <= text.Length; i++)
        {
            set(text[..i]);
            _typingUntilFrame = _frames + Frames(0.5);
            int until = start + (int)Math.Round(i / charsPerSecond * _fps);
            while (_frames < until)
                foreach (var f in OneFrame())
                    yield return f;
        }
    }

    private IEnumerable<int> PressKey(string label, Action action)
    {
        _keyHint = label;
        _keyHintStart = _frames;
        _keyHintFrames = Frames(1.0);
        foreach (var f in Hold(0.12)) yield return f;
        action();
        Settle();
    }

    private IEnumerable<int> ExpandRow(Func<TreeRow> row, double moveSeconds)
    {
        TreeRow? target = null;
        foreach (var f in MoveTo(() => ChevronPoint(target = row()), moveSeconds, hot: () => Container(target!)))
            yield return f;
        foreach (var f in Click(() =>
        {
            _vm.SelectRow(target!, scroll: false);
            _vm.ToggleRow(target!);
        }))
            yield return f;
    }

    private IEnumerable<int> SelectRow(Func<TreeRow> row, double moveSeconds)
    {
        TreeRow? target = null;
        foreach (var f in MoveTo(() => KeyPoint(target = row()), moveSeconds, hot: () => Container(target!)))
            yield return f;
        foreach (var f in Click(() => _vm.SelectRow(target!, scroll: false)))
            yield return f;
    }

    // ================================================================ per-frame overlay

    /// <summary>Updates the caret, click ripple and key hint for the frame about to be rendered.</summary>
    public void BeforeRender()
    {
        // Caret: solid while typing, blinking (every half second) otherwise.
        bool caretOn = _focusBox is not null &&
                       (_frames <= _typingUntilFrame || (_frames / Math.Max(1, _fps / 2)) % 2 == 0);
        _host.SetCaret(_focusBox, caretOn);

        double rt = (_frames - _rippleStart) / (double)Frames(0.35);
        _host.SetRipple(rt);

        int age = _frames - _keyHintStart;
        double opacity = 0;
        if (age >= 0 && age < _keyHintFrames)
        {
            int fade = Math.Max(1, Frames(0.2));
            opacity = age < fade ? (double)age / fade : age > _keyHintFrames - fade ? (double)(_keyHintFrames - age) / fade : 1;
        }
        _host.SetKeyHint(_keyHint, opacity);
    }

    // ================================================================ helpers

    private void Settle() => Offscreen.Settle(_host, _size);

    private void SetHot(FrameworkElement? element)
    {
        // The focused text box keeps its highlighted border while the pointer moves away.
        if (_hot is not null && !ReferenceEquals(_hot, _focusBox)) UiState.SetIsHot(_hot, false);
        _hot = element;
        if (_hot is not null) UiState.SetIsHot(_hot, true);
    }

    private void Focus(TextBox? box)
    {
        if (_focusBox is not null && !ReferenceEquals(_focusBox, _hot)) UiState.SetIsHot(_focusBox, false);
        _focusBox = box;
        if (box is not null) UiState.SetIsHot(box, true);
    }

    private TreeRow Row(string key, int depth) =>
        _vm.Rows.FirstOrDefault(r => r.KeyText == key && r.Depth == depth)
        ?? throw new InvalidOperationException($"Demo script: no visible row '{key}' at depth {depth}");

    private FrameworkElement Container(TreeRow row) =>
        View.ContainerFor(row) ?? throw new InvalidOperationException($"Demo script: row '{row.KeyText}' is not on screen");

    private FrameworkElement Result(int index)
    {
        Settle();
        var item = _vm.QueryResults[index];
        return View.ContainerFor(item) ?? throw new InvalidOperationException($"Demo script: result {index} is not on screen");
    }

    // ListBoxItem: 1 px left border + 6 px padding, then the indent, then the 18 px chevron.
    private Point ChevronPoint(TreeRow row) => Container(row).TranslatePoint(new Point(7 + row.Depth * 16 + 9, 11), _host);

    private Point KeyPoint(TreeRow row) => Container(row).TranslatePoint(new Point(7 + row.Depth * 16 + 18 + 16, 12), _host);
}
