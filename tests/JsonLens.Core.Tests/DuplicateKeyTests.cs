using JsonLens.Core.JsonPath;
using JsonLens.Core.Tree;

namespace JsonLens.Core.Tests;

/// <summary>
/// JSON allows the same member name twice in one object. JSON Lens shows every duplicate, and every
/// internal path (tree rows, search results, query results) points at the exact member it came from.
/// Text paths such as "$.a" carry no position, so they resolve to the last duplicate, like
/// JSON.parse, System.Text.Json and Python do.
/// </summary>
public class DuplicateKeyTests
{
    private const string Dup = """{"a": 1, "a": 2, "b": {"x": "first", "x": "second"}}""";

    private static void AssertResolvesToItself(JsonDoc doc, IReadOnlyList<PathSegment> path, System.Text.Json.JsonElement expected)
    {
        Assert.True(doc.TryResolve(path, out var actual), $"{PathFormat.ToJsonPath(path)} did not resolve");
        Assert.Equal(expected.GetRawText(), actual.GetRawText());
    }

    [Fact]
    public void Every_tree_row_path_resolves_to_that_rows_value()
    {
        using var doc = JsonDoc.Parse(Dup);
        var tree = new FlatTree(doc.Root);
        tree.ExpandAll();
        Assert.Equal(6, tree.Rows.Count); // $, a, a, b, x, x
        foreach (var row in tree.Rows)
            AssertResolvesToItself(doc, row.Path, row.Element);
    }

    [Fact]
    public void Reveal_lands_on_each_duplicate_found_by_search()
    {
        using var doc = JsonDoc.Parse(Dup);
        var tree = new FlatTree(doc.Root);
        foreach (var term in new[] { "first", "second" })
        {
            var match = JsonSearcher.Search(doc.Root, term).Matches.Single();
            var row = tree.Reveal(match.Path);
            Assert.NotNull(row);
            Assert.Equal($"\"{term}\"", row!.ValueText);
        }
    }

    [Fact]
    public void Search_result_paths_resolve_to_the_matched_value()
    {
        using var doc = JsonDoc.Parse(Dup);
        var matches = JsonSearcher.Search(doc.Root, "s").Matches; // "first", "second"
        Assert.Equal(2, matches.Count);
        foreach (var m in matches) AssertResolvesToItself(doc, m.Path, m.Value);

        var ones = JsonSearcher.Search(doc.Root, "1").Matches; // the first "a"
        AssertResolvesToItself(doc, ones.Single().Path, ones.Single().Value);
    }

    [Theory]
    [InlineData("$.b.*")]
    [InlineData("$..x")]
    [InlineData("$.*")]
    [InlineData("$..*")]
    [InlineData("$.b[?@ == 'first' || @ == 'second']")]
    public void Query_result_paths_resolve_to_the_selected_value(string query)
    {
        using var doc = JsonDoc.Parse(Dup);
        var matches = JsonPathQuery.Parse(query).Evaluate(doc.Root).Matches;
        Assert.NotEmpty(matches);
        foreach (var m in matches) AssertResolvesToItself(doc, m.Path, m.Value);
    }

    [Fact]
    public void Wildcards_select_every_duplicate_in_file_order() =>
        Assert.Equal(new[] { "1", "2" }, TestData.Values(Dup, "$[?@ == 1 || @ == 2]"));

    [Fact]
    public void Name_selectors_take_the_last_duplicate_like_parsers_do()
    {
        Assert.Equal(new[] { "2" }, TestData.Values(Dup, "$.a"));
        Assert.Equal(new[] { "\"second\"" }, TestData.Values(Dup, "$.b.x"));
        Assert.Equal(new[] { "\"second\"" }, TestData.Values(Dup, "$['b']['x']"));
        // Filters compare against the same value.
        Assert.Equal(new[] { "$.b" }, TestData.Paths(Dup, "$[?@.x == 'second']"));
    }

    [Fact]
    public void Text_paths_without_positions_lead_to_the_last_duplicate_everywhere()
    {
        using var doc = JsonDoc.Parse(Dup);
        var path = new[] { PathSegment.Member("b"), PathSegment.Member("x") };

        Assert.True(doc.TryResolve(path, out var resolved));
        Assert.Equal("second", resolved.GetString());

        var tree = new FlatTree(doc.Root);
        Assert.Equal("\"second\"", tree.Reveal(path)!.ValueText);
        Assert.Equal("2", tree.Reveal(new[] { PathSegment.Member("a") })!.ValueText);
    }

    [Fact]
    public void Rows_mark_duplicates_and_which_ones_are_overridden()
    {
        using var doc = JsonDoc.Parse("""{"a": 1, "b": 0, "a": 2, "a": 3, "c": {"a": 9}}""");
        var tree = new FlatTree(doc.Root);
        tree.ExpandAll();
        var rows = tree.Rows.Skip(1).ToArray(); // a, b, a, a, c, c.a

        Assert.Equal(new[] { 3, 1, 3, 3, 1, 1 }, rows.Select(r => r.NameCount));
        Assert.Equal(new[] { true, false, true, false, false, false }, rows.Select(r => r.IsShadowed));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 0 }, rows.Select(r => r.MemberIndex));
        Assert.Equal("duplicate key · overridden by a later one", rows[0].DuplicateNote);
        Assert.Equal("duplicate key · last of 3, the one parsers keep", rows[3].DuplicateNote);
        Assert.Equal("", rows[1].DuplicateNote);
        Assert.False(rows[5].IsDuplicateKey); // same name in a different object is fine
    }

    [Fact]
    public void Values_inside_an_overridden_duplicate_know_their_text_path_leads_elsewhere()
    {
        using var doc = JsonDoc.Parse("""{"a": {"x": 1}, "a": {"x": 2}}""");
        var tree = new FlatTree(doc.Root);
        tree.ExpandAll();
        var firstX = tree.Rows.First(r => r.KeyText == "x");
        var secondX = tree.Rows.Last(r => r.KeyText == "x");

        Assert.False(firstX.IsDuplicateKey);
        Assert.Same(tree.Rows[1], firstX.ShadowedOnPath); // the first "a"
        Assert.Null(secondX.ShadowedOnPath);

        // The exact path still works; the text path reaches the other value.
        AssertResolvesToItself(doc, firstX.Path, firstX.Element);
        Assert.True(doc.TryResolve(new[] { PathSegment.Member("a"), PathSegment.Member("x") }, out var viaText));
        Assert.Equal(2, viaText.GetInt32());
    }

    [Fact]
    public void A_member_position_must_match_the_name()
    {
        using var doc = JsonDoc.Parse(Dup);
        Assert.True(doc.TryResolve(new[] { PathSegment.Member("a", 0) }, out var first));
        Assert.Equal(1, first.GetInt32());
        Assert.False(doc.TryResolve(new[] { PathSegment.Member("b", 0) }, out _));
        Assert.False(doc.TryResolve(new[] { PathSegment.Member("a", 9) }, out _));
        Assert.Null(new FlatTree(doc.Root).Reveal(new[] { PathSegment.Member("b", 0) }));
    }

    [Fact]
    public void Paths_to_duplicates_still_print_as_plain_jsonpath()
    {
        using var doc = JsonDoc.Parse(Dup);
        var tree = new FlatTree(doc.Root);
        tree.ExpandAll();
        Assert.Equal(new[] { "$", "$.a", "$.a", "$.b", "$.b.x", "$.b.x" }, tree.Rows.Select(r => r.PathText));
    }
}
