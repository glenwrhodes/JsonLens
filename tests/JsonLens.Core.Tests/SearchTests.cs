using static JsonLens.Core.Tests.TestData;

namespace JsonLens.Core.Tests;

public class SearchTests
{
    [Fact]
    public void Finds_values_ignoring_case_in_document_order()
    {
        var result = JsonSearcher.Search(Parse(Bookstore), "of");
        Assert.Equal(new[] { "$.store.book[0].title", "$.store.book[1].title", "$.store.book[3].title" },
            result.Matches.Select(m => m.PathText));
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Match_case_option()
    {
        Assert.Single(JsonSearcher.Search(Parse(Bookstore), "Moby", new SearchOptions { MatchCase = true }).Matches);
        Assert.Empty(JsonSearcher.Search(Parse(Bookstore), "moby", new SearchOptions { MatchCase = true }).Matches);
        Assert.Single(JsonSearcher.Search(Parse(Bookstore), "moby").Matches);
    }

    [Fact]
    public void Finds_keys_and_reports_the_member_once()
    {
        // "isbn" is a key on two books; "price" matches keys only.
        var isbn = JsonSearcher.Search(Parse(Bookstore), "isbn");
        Assert.Equal(new[] { "$.store.book[2].isbn", "$.store.book[3].isbn" }, isbn.Matches.Select(m => m.PathText));

        // Key and value both match: still one result.
        var both = JsonSearcher.Search(Parse("""{"red": "red"}"""), "red");
        Assert.Single(both.Matches);
    }

    [Fact]
    public void Keys_or_values_only()
    {
        var root = Parse("""{"color": "red", "shade": "colorful"}""");
        Assert.Equal(new[] { "$.color" }, JsonSearcher.Search(root, "color", new SearchOptions { SearchValues = false }).Matches.Select(m => m.PathText));
        Assert.Equal(new[] { "$.shade" }, JsonSearcher.Search(root, "color", new SearchOptions { SearchKeys = false }).Matches.Select(m => m.PathText));
    }

    [Fact]
    public void Finds_numbers_booleans_and_null_by_their_text()
    {
        var root = Parse("""{"a": 12.99, "b": true, "c": null, "d": "nullable"}""");
        Assert.Equal(new[] { "$.a" }, JsonSearcher.Search(root, "2.9").Matches.Select(m => m.PathText));
        Assert.Equal(new[] { "$.b" }, JsonSearcher.Search(root, "TRUE").Matches.Select(m => m.PathText));
        Assert.Equal(new[] { "$.c", "$.d" }, JsonSearcher.Search(root, "null").Matches.Select(m => m.PathText));
    }

    [Fact]
    public void Matches_unescaped_text()
    {
        var root = Parse("""{"city": "Montréal", "q": "a \"quoted\" word"}""");
        Assert.Single(JsonSearcher.Search(root, "montréal").Matches);
        Assert.Single(JsonSearcher.Search(root, "\"quoted\"").Matches);
    }

    [Fact]
    public void Empty_term_finds_nothing() =>
        Assert.Empty(JsonSearcher.Search(Parse(Bookstore), "").Matches);

    [Fact]
    public void Stops_at_the_result_limit()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 500).Select(i => $"\"x{i}\"")) + "]";
        var result = JsonSearcher.Search(Parse(json), "x", new SearchOptions { MaxResults = 100 });
        Assert.Equal(100, result.Matches.Count);
        Assert.True(result.Truncated);
        Assert.Equal("$[99]", result.Matches[^1].PathText);
    }

    [Fact]
    public void Can_be_cancelled()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 20_000).Select(i => $"\"x{i}\"")) + "]";
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => JsonSearcher.Search(Parse(json), "zzz", null, cts.Token));
    }

    [Fact]
    public void Match_values_are_the_found_nodes()
    {
        var m = JsonSearcher.Search(Parse(Bookstore), "Tolkien").Matches.Single();
        Assert.Equal("J. R. R. Tolkien", m.Value.GetString());
        Assert.Equal("$.store.book[3].author", m.PathText);
    }
}
