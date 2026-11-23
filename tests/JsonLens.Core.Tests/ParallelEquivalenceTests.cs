using System.Text;
using System.Text.Json;

namespace JsonLens.Core.Tests;

/// <summary>
/// The parallel search (with its subtree pre-check) and the parallel node count must give exactly
/// what the plain one-thread versions give: same matches, same order, same truncation.
/// </summary>
public class ParallelEquivalenceTests
{
    private static readonly Lazy<JsonDocument> Demo = new(() => JsonDocument.Parse(DemoData.Generate(orders: 3000)));

    /// <summary>Arrays and objects big enough to be split, with escapes, odd keys and nesting.</summary>
    private static readonly Lazy<JsonDocument> Tricky = new(() =>
    {
        var sb = new StringBuilder("{\"items\": [");
        for (int i = 0; i < 1500; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(i % 7 == 0
                ? $"{{\"id\": {i}, \"name\": \"caf\\u00e9 {i}\", \"q\": \"say \\\"hi\\\"\", \"tags\": [\"x\", \"y{i % 5}\"]}}"
                : $"{{\"id\": {i}, \"name\": \"item {i}\", \"ok\": {(i % 2 == 0 ? "true" : "false")}, \"none\": null, \"n\": {i}.5}}");
        }
        sb.Append("], \"map\": {");
        for (int i = 0; i < 800; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(i % 100 == 0 ? $"\"key\\u0020{i}\": {{\"v\": {i}}}" : $"\"key{i}\": {{\"v\": {i}, \"w\": [1, 2, 3]}}");
        }
        sb.Append("}, \"grid\": [");
        for (int r = 0; r < 600; r++)
        {
            if (r > 0) sb.Append(',');
            sb.Append('[').Append(string.Join(",", Enumerable.Range(0, 600).Select(c => (r * 600 + c) % 997 == 0 ? "\"needle\"" : (c % 3).ToString()))).Append(']');
        }
        sb.Append("], \"dup\": {\"a\": 1, \"a\": 2}}");
        return JsonDocument.Parse(sb.ToString());
    });

    private static void AssertSame(JsonElement root, string term, SearchOptions options)
    {
        var sequential = JsonSearcher.Search(root, term, options with { Sequential = true });
        var parallel = JsonSearcher.Search(root, term, options);
        Assert.Equal(sequential.Truncated, parallel.Truncated);
        Assert.Equal(sequential.Matches.Count, parallel.Matches.Count);
        for (int i = 0; i < sequential.Matches.Count; i++)
        {
            Assert.Equal(sequential.Matches[i].Path, parallel.Matches[i].Path);
            Assert.Equal(sequential.Matches[i].Value.GetRawText(), parallel.Matches[i].Value.GetRawText());
        }
    }

    [Theory]
    [InlineData("moose is friendly", false)]
    [InlineData("aurora", false)]
    [InlineData("AURORA", true)]
    [InlineData("Aurora", true)]
    [InlineData("no-such-text", false)]
    [InlineData("montréal", false)]
    [InlineData("tracking", false)] // a key
    [InlineData("12", false)]       // numbers and ids
    [InlineData("null", false)]
    [InlineData("true", false)]
    [InlineData("\": \"", false)]   // looks like JSON syntax
    [InlineData("e", false)]        // matches almost everything
    public void Demo_data_searches_match_the_sequential_walk(string term, bool matchCase) =>
        AssertSame(Demo.Value.RootElement, term, new SearchOptions { MatchCase = matchCase });

    [Theory]
    [InlineData("café")]      // only reachable through \u escapes
    [InlineData("\"hi\"")]    // escaped quotes
    [InlineData("key 0")]     // escaped key
    [InlineData("key 1")]
    [InlineData("key7")]      // keys in a big object
    [InlineData("needle")]    // nested big arrays
    [InlineData("y3")]
    [InlineData(".5")]
    [InlineData("false")]
    [InlineData("a")]
    public void Tricky_searches_match_the_sequential_walk(string term)
    {
        AssertSame(Tricky.Value.RootElement, term, new SearchOptions());
        AssertSame(Tricky.Value.RootElement, term, new SearchOptions { MatchCase = true });
        AssertSame(Tricky.Value.RootElement, term, new SearchOptions { SearchKeys = false });
        AssertSame(Tricky.Value.RootElement, term, new SearchOptions { SearchValues = false });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(100)]
    [InlineData(361)]   // one less than the number of "needle" matches
    [InlineData(362)]   // exactly the number of "needle" matches
    [InlineData(363)]
    [InlineData(5000)]
    public void Result_limits_and_truncation_match(int max)
    {
        AssertSame(Tricky.Value.RootElement, "needle", new SearchOptions { MaxResults = max });
        AssertSame(Demo.Value.RootElement, "aurora", new SearchOptions { MaxResults = max });
    }

    [Fact]
    public void Needle_count_is_what_the_limit_tests_assume()
    {
        var all = JsonSearcher.Search(Tricky.Value.RootElement, "needle", new SearchOptions { Sequential = true });
        Assert.Equal(362, all.Matches.Count);
        Assert.False(all.Truncated);
        Assert.False(JsonSearcher.Search(Tricky.Value.RootElement, "needle", new SearchOptions { MaxResults = 362 }).Truncated);
        Assert.True(JsonSearcher.Search(Tricky.Value.RootElement, "needle", new SearchOptions { MaxResults = 361 }).Truncated);
    }

    [Fact]
    public void Parallel_search_can_be_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => JsonSearcher.Search(Demo.Value.RootElement, "zzz", null, cts.Token));
    }

    [Fact]
    public void Parallel_node_count_matches_the_sequential_count()
    {
        foreach (var doc in new[] { Demo.Value, Tricky.Value })
            Assert.Equal(JsonDoc.CountNodes(doc.RootElement), ParallelWalk.CountNodes(doc.RootElement, default));
        using var scalar = JsonDocument.Parse("42");
        Assert.Equal(1, ParallelWalk.CountNodes(scalar.RootElement, default));
    }
}
