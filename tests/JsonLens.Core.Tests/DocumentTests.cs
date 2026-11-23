using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using JsonLens.Core.JsonPath;
using JsonLens.Core.Tree;

namespace JsonLens.Core.Tests;

public class DocumentTests
{
    [Fact]
    public void Counts_nodes_and_size()
    {
        using var doc = JsonDoc.Parse("""{"a": [1, 2, {"b": null}], "c": "x"}""");
        Assert.Equal(7, doc.NodeCount); // root, a, 1, 2, {b}, null, c
        Assert.True(doc.ByteSize > 0);
    }

    [Fact]
    public void Invalid_json_reports_line_and_column()
    {
        var ex = Assert.Throws<JsonLoadException>(() => JsonDoc.Parse("{\n  \"a\": 1,\n  \"b\": tru\n}"));
        Assert.Equal(3, ex.Line);
        Assert.NotNull(ex.Column);
        Assert.StartsWith("Invalid JSON at line 3", ex.Message);
    }

    [Fact]
    public void Accepts_bom_comments_and_trailing_commas()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("// note\n{\"a\": [1, 2,],}")).ToArray();
        using var doc = JsonDoc.Parse(bytes, null);
        Assert.Equal(2, doc.Root.GetProperty("a").GetArrayLength());
    }

    [Fact]
    public void Accepts_utf16_with_bom()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("{\"é\": 1}")).ToArray();
        using var doc = JsonDoc.Parse(bytes, null);
        Assert.Equal(1, doc.Root.GetProperty("é").GetInt32());
    }

    [Fact]
    public void Load_reports_progress_in_order_and_counts_like_before()
    {
        var path = Path.Combine(Path.GetTempPath(), "JsonLensTests-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            // Bigger than one read chunk (8 MB) so reading reports several steps.
            DemoData.EnsureFile(path, orders: 12_000);
            var reports = new List<LoadProgress>();
            using var doc = JsonDoc.Load(path, new SyncProgress(reports.Add));

            var phases = reports.Select(r => r.Phase).Distinct().ToArray();
            Assert.Equal(new[] { "Reading", "Parsing", "Counting" }, phases);
            var reading = reports.Where(r => r.Phase == "Reading").Select(r => r.Fraction).ToArray();
            Assert.True(reading.Length >= 3, $"{reading.Length} reading reports");
            Assert.Equal(reading.OrderBy(f => f), reading);
            Assert.Equal(1.0, reading[^1]);

            Assert.Equal(JsonDoc.CountNodes(doc.Root), doc.NodeCount);
            Assert.Equal(new FileInfo(path).Length, doc.ByteSize);
            using var reference = JsonDoc.Parse(File.ReadAllBytes(path), path);
            Assert.Equal(reference.NodeCount, doc.NodeCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_can_be_cancelled()
    {
        var path = Path.Combine(Path.GetTempPath(), "JsonLensTests-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "[1, 2, 3]");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => JsonDoc.Load(path, null, cts.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Loading_does_not_lock_the_file_against_writers()
    {
        var path = Path.Combine(Path.GetTempPath(), "JsonLensTests-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"a\": 1}");
            using var doc = JsonDoc.Load(path);
            File.WriteAllText(path, "{\"a\": 2}"); // an editor saving while the file is open in JSON Lens
            Assert.Equal(1, doc.Root.GetProperty("a").GetInt32());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Progress&lt;T&gt; posts to the thread pool; this one reports synchronously, in order.</summary>
    private sealed class SyncProgress : IProgress<LoadProgress>
    {
        private readonly Action<LoadProgress> _report;
        public SyncProgress(Action<LoadProgress> report) => _report = report;
        public void Report(LoadProgress value) => _report(value);
    }

    [Fact]
    public void Demo_data_is_identical_every_time_and_valid()
    {
        var a = DemoData.Generate(orders: 500);
        var b = DemoData.Generate(orders: 500);
        Assert.Equal(SHA256.HashData(a), SHA256.HashData(b));
        Assert.NotEqual(SHA256.HashData(a), SHA256.HashData(DemoData.Generate(orders: 500, seed: 7)));

        using var doc = JsonDoc.Parse(a, null);
        Assert.Equal(500, doc.Root.GetProperty("orders").GetArrayLength());
        Assert.Equal(250, doc.Root.GetProperty("customers").GetArrayLength());
    }

    [Fact]
    public void Demo_file_is_created_once()
    {
        var dir = Path.Combine(Path.GetTempPath(), "JsonLensTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "demo.json");
            DemoData.EnsureFile(path, orders: 200);
            var first = File.GetLastWriteTimeUtc(path);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(DemoData.Generate(200), bytes);
            DemoData.EnsureFile(path, orders: 200);
            Assert.Equal(first, File.GetLastWriteTimeUtc(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Demo_queries_used_in_the_video_have_results()
    {
        using var doc = JsonDoc.Parse(DemoData.Generate(orders: 2000), null);
        var big = JsonPathQuery.Parse("$.orders[?@.total > 900 && @.status == 'delivered'].id").Evaluate(doc.Root);
        Assert.NotEmpty(big.Matches);
        Assert.NotEmpty(JsonSearcher.Search(doc.Root, "aurora").Matches);
    }

    /// <summary>Speed smoke test: ~1.37 million nodes should parse, count, search and query in seconds, not minutes.</summary>
    [Fact]
    public void Large_document_is_fast_enough()
    {
        var bytes = DemoData.Generate(orders: DemoData.DefaultOrders);
        var sw = Stopwatch.StartNew();
        using var doc = JsonDoc.Parse(bytes, null);
        Assert.True(doc.NodeCount > 1_000_000, $"only {doc.NodeCount} nodes");

        var tree = new FlatTree(doc.Root);
        tree.Expand(tree.Rows.First(r => r.KeyText == "orders"));
        var row = tree.Reveal(new[] { PathSegment.Member("orders"), PathSegment.At(39_999), PathSegment.Member("id") });
        Assert.Equal("140000", row!.ValueText);

        var search = JsonSearcher.Search(doc.Root, "moose is friendly");
        Assert.NotEmpty(search.Matches);

        var query = JsonPathQuery.Parse("$.orders[?@.gift == true && @.total > 500].id").Evaluate(doc.Root);
        Assert.NotEmpty(query.Matches);

        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"took {sw.Elapsed}");
    }
}
