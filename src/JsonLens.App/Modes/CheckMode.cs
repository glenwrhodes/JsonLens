using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using JsonLens.App.Services;
using JsonLens.App.ViewModels;
using JsonLens.App.Views;
using JsonLens.Core;
using JsonLens.Core.Tree;
using PathSegment = JsonLens.Core.PathSegment;

namespace JsonLens.App.Modes;

/// <summary>
/// --check: starts the app without a window, builds the real view off-screen, drives it through the
/// main features on the built-in sample and on the large demo file, and prints PASS/FAIL per step.
/// </summary>
public sealed class CheckMode
{
    private static readonly Size ViewSize = new(1400, 860);

    private readonly ConsoleOutput _out;
    private readonly CommandLine _cl;
    private int _passed;
    private int _failed;

    public CheckMode(ConsoleOutput output, CommandLine cl)
    {
        _out = output;
        _cl = cl;
    }

    public int Run()
    {
        var total = Stopwatch.StartNew();
        _out.Line($"JSON Lens {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} self-check (no window)");
        _out.Line("© 2026 Tiny Moosh Games Inc.");
        _out.Line();

        var services = new RecordingAppServices();
        using var vm = new MainViewModel(services, synchronous: true);
        MainView? view = null;

        Step("Theme resources load", () =>
        {
            var accent = Application.Current.FindResource("AccentBrush") as SolidColorBrush;
            Require(accent is not null, "AccentBrush missing");
            Require(Application.Current.FindResource("CodeFont") is FontFamily, "CodeFont missing");
            return "dark theme dictionary is loaded";
        });

        Step("Main view builds and lays out", () =>
        {
            view = new MainView { DataContext = vm };
            Offscreen.Settle(view, ViewSize);
            Require(view.ActualWidth > 0, "view has no size");
            Require(view.EmptyState.Visibility == Visibility.Visible, "empty state should show before a file is opened");
            var placeholder = view.SearchBox.Template.FindName("Placeholder", view.SearchBox) as System.Windows.Controls.TextBlock;
            Require(placeholder is { Text.Length: > 0, ActualWidth: > 0 }, "search box placeholder is not shown");
            return $"{ViewSize.Width}x{ViewSize.Height}, empty state shown";
        });
        if (view is null) return Finish(total);

        Step("Built-in sample opens", () =>
        {
            vm.LoadText(ReadSample(), "sample.json");
            Offscreen.Settle(view, ViewSize);
            Require(vm.HasDocument, "no document");
            Require(vm.Rows.Count >= 2, "tree has no rows");
            Require(view.ContainerFor(vm.Rows[1]) is not null, "tree rows were not realized in the list");
            return $"{vm.Document!.NodeCount} nodes, {vm.Rows.Count} visible rows";
        });

        Step("Expand and collapse", () =>
        {
            var store = vm.Rows.First(r => r.KeyText == "store");
            int before = vm.Rows.Count;
            vm.ToggleRow(store);
            Offscreen.Settle(view, ViewSize);
            Require(vm.Rows.Count == before + 2, $"expected {before + 2} rows, got {vm.Rows.Count}");
            vm.ToggleRow(store);
            Require(vm.Rows.Count == before, "collapse didn't remove the children");
            return "store: +2 rows, then back";
        });

        Step("Search finds and reveals", () =>
        {
            vm.SearchText = "tolkien";
            Offscreen.Settle(view, ViewSize);
            Require(vm.SearchResult.Matches.Count == 1, $"expected 1 match, got {vm.SearchResult.Matches.Count}");
            Require(vm.SelectedPath == "$.store.book[3].author", $"selected {vm.SelectedPath}");
            Require(Equals(view.TreeList.SelectedItem, vm.SelectedRow), "list selection not in sync");
            Require(view.ContainerFor(vm.SelectedRow!) is not null, "match is not scrolled into view");
            vm.SearchText = "";
            return $"\"tolkien\" -> {vm.SelectedPath}";
        });

        Step("JSONPath query and result navigation", () =>
        {
            vm.QueryText = "$..book[?@.price < 10].title";
            vm.RunQueryAsync().GetAwaiter().GetResult();
            Require(!vm.HasQueryError, vm.QueryError ?? "");
            Require(vm.QueryResults.Count == 2, $"expected 2 results, got {vm.QueryResults.Count}");
            vm.SelectedResult = vm.QueryResults[1];
            Offscreen.Settle(view, ViewSize);
            Require(vm.SelectedPath == "$.store.book[2].title", $"selected {vm.SelectedPath}");
            return $"2 results; result 2 -> {vm.SelectedPath}";
        });

        Step("JSONPath errors point at the problem", () =>
        {
            vm.QueryText = "$.store.book[?@.price < ]";
            vm.RunQueryAsync().GetAwaiter().GetResult();
            Require(vm.HasQueryError, "no error reported");
            Require(vm.QueryErrorPosition == 24, $"error position {vm.QueryErrorPosition}, expected 24");
            Offscreen.Settle(view, ViewSize);
            view.PositionErrorPointer();
            double x = System.Windows.Controls.Canvas.GetLeft(view.ErrorPointer);
            Require(x > 100, $"error pointer not moved under the text (x={x:0})");
            string message = vm.QueryError!;
            int position = vm.QueryErrorPosition;
            vm.QueryText = "";
            Require(!vm.HasQueryError, "error not cleared after editing the query");
            return $"at character {position + 1}: \"{message}\"";
        });

        Step("Copy path, pointer and value", () =>
        {
            vm.RevealPath(new[] { PathSegment.Member("edge cases"), PathSegment.Member("it's") });
            vm.CopyPathCommand.Execute(null);
            Require(services.ClipboardText == "$['edge cases']['it\\'s']", $"copied {services.ClipboardText}");
            Require(vm.ToastOpacity > 0 && vm.ToastText.Contains("Copied path"), "no toast");
            vm.CopyPointerCommand.Execute(null);
            Require(services.ClipboardText == "/edge cases/it's", $"pointer {services.ClipboardText}");
            vm.CopyValueCommand.Execute(null);
            Require(services.ClipboardText == "has a quote", $"value {services.ClipboardText}");
            return "$['edge cases']['it\\'s']";
        });

        Step("Duplicate keys go to the exact value", () =>
        {
            // Search lands on the first (overridden) duplicate, not the one a text path would reach.
            vm.SearchText = "first";
            Offscreen.Settle(view, ViewSize);
            var row = vm.SelectedRow!;
            Require(row.IsShadowed && vm.DetailText == "\"first\"", $"selected {vm.SelectedPath} = {vm.DetailText}");
            Require(vm.SelectedWarning.Contains("Duplicate key"), "no duplicate warning in the details pane");
            Require(view.DuplicateWarning.Visibility == Visibility.Visible, "warning banner hidden");
            vm.CopyPathCommand.Execute(null);
            Require(services.ClipboardText == "$['edge cases']['duplicate key'].x", $"copied {services.ClipboardText}");
            Require(vm.IsToastWarning, "copying an overridden duplicate's path should warn");
            vm.SearchText = "";

            // Query results for both duplicates open the right rows.
            vm.QueryText = "$['edge cases']['duplicate key'].*";
            vm.RunQueryAsync().GetAwaiter().GetResult();
            Require(vm.QueryResults.Count == 2, $"{vm.QueryResults.Count} results");
            vm.SelectedResult = vm.QueryResults[0];
            Require(vm.DetailText == "\"first\"", $"result 1 opened {vm.DetailText}");
            vm.SelectedResult = vm.QueryResults[1];
            Require(vm.DetailText == "\"second\"" && !vm.SelectedPathIsAmbiguous, $"result 2 opened {vm.DetailText}");

            // A typed path means the last duplicate, as in JSON parsers.
            vm.QueryText = "$['edge cases']['duplicate key'].x";
            vm.RunQueryAsync().GetAwaiter().GetResult();
            Require(vm.QueryResults.Count == 1 && vm.QueryResults[0].Preview == "\"second\"", "name selector should pick the last duplicate");
            vm.QueryText = "";
            vm.RunQueryAsync().GetAwaiter().GetResult();
            return "overridden duplicate marked, warned and opened exactly";
        });

        Step("Pretty and minify", () =>
        {
            vm.SelectRow(vm.Tree!.Root, scroll: true);
            vm.IsMinified = false;
            var pretty = vm.DetailText;
            vm.IsMinified = true;
            var min = vm.DetailText;
            Require(pretty.Contains('\n') && !min.Contains('\n'), "formats look wrong");
            Require(min.Length < pretty.Length, "minified isn't shorter");
            Require(min.Contains("123456789012345678901234567890") && min.Contains("1.10") && min.Contains("6.02e23"), "numbers changed");
            Require(JsonFormatter.Format(pretty, pretty: false) == min, "pretty and minified differ in content");
            vm.CopyValueCommand.Execute(null);
            Require(services.ClipboardText == min, "copy value isn't the minified text");
            vm.IsMinified = false;
            return $"{pretty.Length:N0} chars pretty, {min.Length:N0} minified, numbers exact";
        });

        string demoPath = _cl.DemoFile ?? AppPaths.DefaultDemoFile;
        Step("Large demo file is available", () =>
        {
            bool existed = File.Exists(demoPath);
            var sw = Stopwatch.StartNew();
            DemoData.EnsureFile(demoPath);
            return existed
                ? $"{AppPaths.FormatBytes(new FileInfo(demoPath).Length)} at {demoPath}"
                : $"generated {AppPaths.FormatBytes(new FileInfo(demoPath).Length)} in {sw.Elapsed.TotalSeconds:0.0} s at {demoPath}";
        });

        Step("Large file opens", () =>
        {
            vm.OpenFileAsync(demoPath).GetAwaiter().GetResult();
            Require(vm.LoadError is null, vm.LoadError ?? "");
            Require(vm.Document is { NodeCount: > 1_000_000 }, $"only {vm.Document?.NodeCount} nodes");
            Offscreen.Settle(view, ViewSize);
            return vm.DocInfo;
        });

        Step("Large file: reveal a deep node", () =>
        {
            var sw = Stopwatch.StartNew();
            var row = vm.RevealPath(new[] { PathSegment.Member("orders"), PathSegment.At(39_999), PathSegment.Member("shipping"), PathSegment.Member("eta") });
            Offscreen.Settle(view, ViewSize);
            Require(row is not null, "not found");
            Require(view.ContainerFor(row!) is not null, "row not scrolled into view");
            return $"{vm.SelectedPath} in {sw.ElapsedMilliseconds} ms ({vm.Rows.Count:N0} visible rows)";
        });

        Step("Large file: search", () =>
        {
            var sw = Stopwatch.StartNew();
            vm.SearchText = "moose is friendly";
            Require(vm.SearchResult.Matches.Count > 100, $"{vm.SearchResult.Matches.Count} matches");
            vm.NextMatch();
            Offscreen.Settle(view, ViewSize);
            Require(vm.SelectedPath.EndsWith(".note", StringComparison.Ordinal), $"selected {vm.SelectedPath}");
            var status = vm.SearchStatus;
            vm.SearchText = "";
            return $"{status} in {sw.ElapsedMilliseconds} ms";
        });

        Step("Large file: JSONPath filter", () =>
        {
            vm.QueryText = "$.orders[?@.total > 900 && @.status == 'delivered'].id";
            vm.RunQueryAsync().GetAwaiter().GetResult();
            Require(vm.QueryResults.Count > 10, $"{vm.QueryResults.Count} results");
            vm.SelectedResult = vm.QueryResults[^1];
            Offscreen.Settle(view, ViewSize);
            Require(view.ContainerFor(vm.SelectedRow!) is not null, "result not revealed");
            return vm.QueryStatus;
        });

        Step("Large file: expand all is capped", () =>
        {
            var sw = Stopwatch.StartNew();
            vm.ExpandAllCommand.Execute(null);
            Require(vm.Rows.Count <= 200_000, $"{vm.Rows.Count} rows");
            vm.CollapseAllCommand.Execute(null);
            Offscreen.Settle(view, ViewSize);
            Require(vm.Rows.Count < 20, $"collapse left {vm.Rows.Count} rows");
            return $"capped and collapsed in {sw.ElapsedMilliseconds} ms";
        });

        Step("Renders a frame off-screen", () =>
        {
            Offscreen.Settle(view, ViewSize);
            var bmp = Offscreen.Render(view, (int)ViewSize.Width, (int)ViewSize.Height);
            int colours = Offscreen.DistinctColours(bmp);
            Require(colours > 40, $"only {colours} colours: the render looks blank");
            return $"{bmp.PixelWidth}x{bmp.PixelHeight}, {colours} colours sampled";
        });

        Step("App icon draws", () =>
        {
            var icon = AppIcon.Create();
            int colours = Offscreen.DistinctColours(icon);
            Require(icon.PixelWidth == 64 && colours > 3, $"icon looks blank ({colours} colours)");
            return "64x64";
        });

        Step("Command line parsing", () =>
        {
            var cl = CommandLine.Parse(new[] { "--frames", "out", "--fps", "24", "--size", "1280x720" });
            Require(cl.Mode == RunMode.Frames && cl.FramesDir == "out" && cl.Fps == 24 && cl.Width == 1280 && cl.Height == 720, "frames options");
            Require(CommandLine.Parse(new[] { "--bogus" }).Error is not null, "unknown option accepted");
            Require(CommandLine.Parse(new[] { "a.json" }).FilePath == "a.json", "file argument");
            return "ok";
        });

        return Finish(total);
    }

    private int Finish(Stopwatch total)
    {
        _out.Line();
        if (_failed == 0)
            _out.Line($"All {_passed} checks passed in {total.Elapsed.TotalSeconds:0.0} s.");
        else
            _out.Line($"{_failed} of {_passed + _failed} checks FAILED ({total.Elapsed.TotalSeconds:0.0} s).");
        return _failed == 0 ? 0 : 1;
    }

    private void Step(string name, Func<string> body)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            string detail = body();
            _passed++;
            _out.Line($"PASS  {name}  ({sw.ElapsedMilliseconds} ms)  {detail}");
        }
        catch (Exception ex)
        {
            _failed++;
            string message = ex is CheckFailed ? ex.Message : ex.ToString();
            _out.Line($"FAIL  {name}  {message}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new CheckFailed(message);
    }

    private sealed class CheckFailed : Exception
    {
        public CheckFailed(string message) : base(message) { }
    }

    internal static string ReadSample()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("JsonLens.sample.json")
            ?? throw new InvalidOperationException("embedded sample.json is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
