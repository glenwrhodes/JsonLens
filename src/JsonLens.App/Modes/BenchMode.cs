using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime;
using System.Security.Cryptography;
using System.Text;
using JsonLens.Core;
using JsonLens.Core.JsonPath;
using JsonLens.Core.Tree;

namespace JsonLens.App.Modes;

/// <summary>
/// --bench: opens a file the way the app does and times each phase, then the tree, search and
/// JSONPath steps, and reports memory. Result fingerprints (a hash of every result's path and
/// value) make it easy to confirm an optimization didn't change any results.
/// </summary>
public sealed class BenchMode
{
    private readonly ConsoleOutput _out;
    private readonly CommandLine _cl;

    public BenchMode(ConsoleOutput output, CommandLine cl)
    {
        _out = output;
        _cl = cl;
    }

    private static readonly (string Label, string Term, bool MatchCase)[] Searches =
    {
        ("rare phrase", "moose is friendly", false),
        ("common word (stops at 10,000)", "aurora", false),
        ("absent (full scan)", "no-such-text-xyz", false),
        ("match case", "Halifax", true),
        ("non-ASCII", "montréal", false),
    };

    private static readonly string[] Queries =
    {
        "$.orders[?@.total > 900 && @.status == 'delivered'].id",
        "$..customers[?@.tier == 'gold'].name",
        "$.orders[-1]",
    };

    public int Run()
    {
        string path = _cl.FilePath!;
        if (!File.Exists(path))
        {
            if (_cl.GenerateOrders is not { } orders)
            {
                _out.Line($"File not found: {path} (add --orders N to generate it with the demo generator)");
                return 1;
            }
            _out.Line($"Generating {path} with {orders:N0} orders …");
            var gen = Stopwatch.StartNew();
            DemoData.EnsureFile(path, orders);
            _out.Line($"  generated {AppPaths.FormatBytes(new FileInfo(path).Length)} in {gen.Elapsed.TotalSeconds:0.0} s");
        }

        var asm = Assembly.GetExecutingAssembly();
        bool optimized = asm.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled != true;
        _out.Line($"JSON Lens {asm.GetName().Version?.ToString(3)} bench  ·  {(optimized ? "Release" : "DEBUG build: timings not representative")}");
        _out.Line($"File: {path}  ({AppPaths.FormatBytes(new FileInfo(path).Length)})");
        _out.Line($".NET {Environment.Version}  ·  {Environment.ProcessorCount} logical CPUs  ·  {(GCSettings.IsServerGC ? "server" : "workstation")} GC, {GCSettings.LatencyMode}");
        _out.Line($"Windows power throttling: {(App.PowerThrottlingDisabled ? "off for this process" : "ON (--eco or unsupported): timings can be 3-4x slower on hybrid CPUs")}");
        _out.Line();

        // ------------------------------------------------------------ open, exactly as the app does
        var proc = Process.GetCurrentProcess();
        var before = Snapshot(proc);
        var open = Stopwatch.StartNew();
        using var doc = JsonDoc.Load(path, new Progress<LoadProgress>(p => { }));
        open.Stop();
        var after = Snapshot(proc);
        _out.Line("Open");
        Row("total", open.Elapsed, $"{doc.NodeCount:N0} nodes  ·  {Throughput(doc.ByteSize, open.Elapsed)}");
        foreach (var (name, time) in doc.Phases)
            Row("  " + name, time, Throughput(doc.ByteSize, time));
        _out.Line($"  memory after open: GC heap {Mb(after.Heap)}, working set {Mb(after.WorkingSet)}, private {Mb(after.Private)}, peak working set {Mb(after.PeakWorkingSet)}");
        _out.Line($"  GCs during open: gen0 {after.Gen0 - before.Gen0}, gen1 {after.Gen1 - before.Gen1}, gen2 {after.Gen2 - before.Gen2}, pause {(after.Pause - before.Pause).TotalMilliseconds:N0} ms");
        var mem = GC.GetGCMemoryInfo();
        _out.Line($"  machine memory in use: {mem.MemoryLoadBytes / 1073741824.0:N1} of {mem.TotalAvailableMemoryBytes / 1073741824.0:N1} GB");
        _out.Line();

        // ------------------------------------------------------------ tree
        _out.Line("Tree");
        var biggest = doc.Root.ValueKind == System.Text.Json.JsonValueKind.Object
            ? doc.Root.EnumerateObject().Select((p, i) => (Name: p.Name, Index: i, Count: Count(p.Value))).MaxBy(x => x.Count)
            : default;
        Time("open tree + expand largest container + reveal its last item", () =>
        {
            var tree = new FlatTree(doc.Root);
            if (biggest.Name is null) return "";
            var path = new[] { PathSegment.Member(biggest.Name, biggest.Index), PathSegment.At(Math.Max(0, biggest.Count - 1)) };
            var row = tree.Reveal(path);
            return $"{row?.PathText}  ({tree.Rows.Count:N0} visible rows)";
        });
        _out.Line();

        // ------------------------------------------------------------ search
        _out.Line("Search");
        foreach (var (label, term, matchCase) in Searches)
        {
            Time($"{label}: \"{term}\"", () =>
            {
                var r = JsonSearcher.Search(doc.Root, term, new SearchOptions { MatchCase = matchCase });
                return $"{r.Matches.Count:N0}{(r.Truncated ? "+" : "")} matches  ·  {Fingerprint(r.Matches)}";
            });
        }
        _out.Line();

        // ------------------------------------------------------------ JSONPath
        _out.Line("JSONPath");
        foreach (var q in Queries)
        {
            var query = JsonPathQuery.Parse(q);
            Time(q, () =>
            {
                var r = query.Evaluate(doc.Root);
                return $"{r.Matches.Count:N0} results  ·  {Fingerprint(r.Matches)}";
            });
        }
        _out.Line();

        var end = Snapshot(proc);
        _out.Line($"Peak working set for the whole run: {Mb(end.PeakWorkingSet)}");
        return 0;
    }

    private void Time(string label, Func<string> body)
    {
        TimeSpan best = TimeSpan.MaxValue;
        string result = "";
        for (int i = 0; i < _cl.Runs; i++)
        {
            var sw = Stopwatch.StartNew();
            result = body();
            sw.Stop();
            if (sw.Elapsed < best) best = sw.Elapsed;
        }
        Row(label, best, result);
    }

    private void Row(string label, TimeSpan time, string detail) =>
        _out.Line($"  {label,-58} {time.TotalMilliseconds,9:N0} ms   {detail}");

    private static int Count(System.Text.Json.JsonElement e) => e.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Array => e.GetArrayLength(),
        System.Text.Json.JsonValueKind.Object => e.EnumerateObject().Count(),
        _ => 0,
    };

    /// <summary>Short hash of every match's path and raw value, in order.</summary>
    private static string Fingerprint(IReadOnlyList<JsonMatch> matches)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var m in matches)
        {
            sha.AppendData(Encoding.UTF8.GetBytes(m.PathText));
            sha.AppendData(Encoding.UTF8.GetBytes(m.Value.GetRawText()));
            sha.AppendData(new byte[] { 0 });
        }
        return "#" + Convert.ToHexString(sha.GetHashAndReset())[..12];
    }

    private static string Throughput(long bytes, TimeSpan time) =>
        time.TotalSeconds > 0 ? $"{bytes / 1048576.0 / time.TotalSeconds:N0} MB/s" : "";

    private static string Mb(long bytes) => $"{bytes / 1048576.0:N0} MB";

    private readonly record struct Mem(long Heap, long WorkingSet, long PeakWorkingSet, long Private, int Gen0, int Gen1, int Gen2, TimeSpan Pause);

    private static Mem Snapshot(Process proc)
    {
        proc.Refresh();
        return new Mem(GC.GetTotalMemory(false), proc.WorkingSet64, proc.PeakWorkingSet64, proc.PrivateMemorySize64,
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration());
    }
}
