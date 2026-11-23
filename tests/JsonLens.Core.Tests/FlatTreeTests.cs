using JsonLens.Core.Tree;
using static JsonLens.Core.Tests.TestData;

namespace JsonLens.Core.Tests;

public class FlatTreeTests
{
    private static string Keys(FlatTree t) => string.Join(" ", t.Rows.Select(r => new string('.', r.Depth) + r.KeyText));

    [Fact]
    public void Starts_with_root_expanded()
    {
        var tree = new FlatTree(Parse(Bookstore));
        Assert.Equal("$ .store", Keys(tree));
        Assert.True(tree.Root.IsExpanded);
        Assert.Equal(RowKind.Object, tree.Rows[1].Kind);
        Assert.Equal("1 key", tree.Root.Summary);
    }

    [Fact]
    public void Expand_and_collapse_insert_and_remove_descendants()
    {
        var tree = new FlatTree(Parse(Bookstore));
        int changes = 0;
        tree.RowsChanged += (_, _) => changes++;

        var store = tree.Rows[1];
        Assert.True(tree.Expand(store));
        Assert.Equal("$ .store ..book ..bicycle", Keys(tree));

        var book = tree.Rows[2];
        tree.Expand(book);
        Assert.Equal(8, tree.Rows.Count); // $, store, book, 4 books, bicycle
        Assert.Equal("[3]", tree.Rows[6].KeyText);
        Assert.Equal("bicycle", tree.Rows[7].KeyText);

        tree.Collapse(store);
        Assert.Equal(2, tree.Rows.Count);
        Assert.Equal(3, changes);

        Assert.False(tree.Collapse(store));
        Assert.False(tree.Expand(tree.Rows[0])); // already expanded
    }

    [Fact]
    public void Collapsing_a_parent_keeps_child_expansion_for_next_time()
    {
        var tree = new FlatTree(Parse(Bookstore));
        var store = tree.Rows[1];
        tree.Expand(store);
        tree.Expand(tree.Rows[2]); // book
        tree.Collapse(store);
        tree.Expand(store);
        Assert.Equal(8, tree.Rows.Count);
    }

    [Fact]
    public void Scalars_are_not_expandable()
    {
        var tree = new FlatTree(Parse("""{"a": 1, "b": [], "c": {}}"""));
        Assert.All(tree.Rows.Skip(1), r => Assert.False(r.IsExpandable));
        Assert.False(tree.Expand(tree.Rows[1]));
        Assert.Equal("[]", tree.Rows[2].ValueText);
        Assert.Equal("{}", tree.Rows[3].ValueText);
    }

    [Fact]
    public void Row_text_for_each_kind()
    {
        var tree = new FlatTree(Parse("""{"s": "a\nb \"q\"", "n": 1.50, "t": true, "f": false, "z": null, "o": {"x": 1}, "arr": [1, 2]}"""));
        var rows = tree.Rows.Skip(1).ToDictionary(r => r.KeyText);
        Assert.Equal("\"a\\nb \\\"q\\\"\"", rows["s"].ValueText);
        Assert.Equal("1.50", rows["n"].ValueText);
        Assert.Equal("true", rows["t"].ValueText);
        Assert.Equal("false", rows["f"].ValueText);
        Assert.Equal("null", rows["z"].ValueText);
        Assert.Equal("{…}", rows["o"].ValueText);
        Assert.Equal("1 key", rows["o"].Summary);
        Assert.Equal("2 items", rows["arr"].Summary);
        Assert.Equal(RowKind.Boolean, rows["f"].Kind);
    }

    [Fact]
    public void Empty_key_is_shown_as_quotes() =>
        Assert.Equal("\"\"", new FlatTree(Parse("""{"": 1}""")).Rows[1].KeyText);

    [Fact]
    public void Long_strings_are_cut_in_the_preview()
    {
        var tree = new FlatTree(Parse("[\"" + new string('x', 5000) + "\"]"));
        var text = tree.Rows[1].ValueText;
        Assert.True(text.Length <= TreeRow.MaxPreviewChars + 2);
        Assert.EndsWith("…\"", text);
    }

    [Fact]
    public void Big_arrays_are_grouped_by_hundreds()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 250)) + "]";
        var tree = new FlatTree(Parse(json));
        Assert.Equal("$ .[0 … 99] .[100 … 199] .[200 … 249]", Keys(tree));
        Assert.True(tree.Rows[1].IsGroup);
        Assert.Equal("100 items", tree.Rows[1].Summary);
        Assert.Equal("50 items", tree.Rows[3].Summary);

        tree.Expand(tree.Rows[3]);
        Assert.Equal(4 + 50, tree.Rows.Count);
        Assert.Equal("[200]", tree.Rows[4].KeyText);
        Assert.Equal(2, tree.Rows[4].Depth);
        Assert.Equal("$[200]", tree.Rows[4].PathText);
    }

    [Fact]
    public void Very_big_arrays_nest_groups()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 25_000)) + "]";
        var tree = new FlatTree(Parse(json));
        Assert.Equal("$ .[0 … 9999] .[10000 … 19999] .[20000 … 24999]", Keys(tree));

        tree.Expand(tree.Rows[3]);
        Assert.Equal(4 + 50, tree.Rows.Count); // 5000 items -> 50 groups of 100
        Assert.Equal("[20000 … 20099]", tree.Rows[4].KeyText);
    }

    [Fact]
    public void Big_objects_are_grouped_too()
    {
        var json = "{" + string.Join(",", Enumerable.Range(0, 150).Select(i => $"\"k{i}\": {i}")) + "}";
        var tree = new FlatTree(Parse(json));
        Assert.Equal(3, tree.Rows.Count);
        tree.Expand(tree.Rows[2]);
        Assert.Equal("k100", tree.Rows[3].KeyText);
        Assert.Equal("$.k100", tree.Rows[3].PathText);
    }

    [Fact]
    public void Reveal_expands_the_way_down_including_groups()
    {
        var json = "{\"data\": [" + string.Join(",", Enumerable.Range(0, 25_000).Select(i => $"{{\"id\": {i}, \"tags\": [\"t{i}\"]}}")) + "]}";
        var tree = new FlatTree(Parse(json));
        int changes = 0;
        tree.RowsChanged += (_, _) => changes++;

        var row = tree.Reveal(new[] { PathSegment.Member("data"), PathSegment.At(12_345), PathSegment.Member("tags"), PathSegment.At(0) });
        Assert.NotNull(row);
        Assert.Equal("\"t12345\"", row!.ValueText);
        Assert.Equal("$.data[12345].tags[0]", row.PathText);
        Assert.True(tree.IndexOf(row) > 0);
        Assert.Equal(1, changes); // many expands, one notification

        // Every ancestor is expanded.
        for (var r = row.Parent; r is not null; r = r.Parent) Assert.True(r.IsExpanded);
    }

    [Fact]
    public void Reveal_of_root_and_missing_paths()
    {
        var tree = new FlatTree(Parse(Bookstore));
        Assert.Same(tree.Root, tree.Reveal(Array.Empty<PathSegment>()));
        Assert.Null(tree.Reveal(new[] { PathSegment.Member("nope") }));
        Assert.Null(tree.Reveal(new[] { PathSegment.Member("store"), PathSegment.At(0) }));
        Assert.Null(tree.Reveal(new[] { PathSegment.Member("store"), PathSegment.Member("book"), PathSegment.At(4) }));
    }

    [Fact]
    public void Reveal_finds_every_search_match()
    {
        var root = Parse(Bookstore);
        var tree = new FlatTree(root);
        foreach (var m in JsonSearcher.Search(root, "e").Matches)
        {
            var row = tree.Reveal(m.Path);
            Assert.NotNull(row);
            Assert.Equal(m.PathText, row!.PathText);
            Assert.Equal(m.Value.GetRawText(), row.Element.GetRawText());
        }
    }

    [Fact]
    public void Expand_all_and_collapse_all()
    {
        var tree = new FlatTree(Parse(Bookstore));
        Assert.True(tree.ExpandAll());
        Assert.Equal((int)JsonDoc.CountNodes(tree.Root.Element), tree.Rows.Count);

        tree.CollapseAll();
        Assert.Equal(2, tree.Rows.Count);
        Assert.True(tree.Root.IsExpanded);
        Assert.False(tree.Rows[1].IsExpanded);
    }

    [Fact]
    public void Expand_all_stops_at_the_row_budget()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 5000).Select(i => $"[{i},{i}]")) + "]";
        var tree = new FlatTree(Parse(json));
        Assert.False(tree.ExpandAll(maxVisibleRows: 1000));
        Assert.True(tree.Rows.Count <= 1000);
        Assert.True(tree.Rows.Count > 100);
    }

    [Fact]
    public void Expand_all_budget_holds_when_rows_were_already_expanded()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 5000).Select(i => $"[{i},{i}]")) + "]";
        var tree = new FlatTree(Parse(json));
        // Open a deep path near the end first, which the breadth-first expand would not reach.
        tree.Reveal(new[] { PathSegment.At(4999), PathSegment.At(1) });
        Assert.False(tree.ExpandAll(maxVisibleRows: 1000));
        Assert.True(tree.Rows.Count <= 1000, $"{tree.Rows.Count} rows");
    }

    [Fact]
    public void Group_paths_point_at_their_container()
    {
        var json = "{\"a\": [" + string.Join(",", Enumerable.Range(0, 300)) + "]}";
        var tree = new FlatTree(Parse(json));
        tree.Expand(tree.Rows[1]);
        var group = tree.Rows[2];
        Assert.True(group.IsGroup);
        Assert.Equal("$.a", group.PathText);
        Assert.Same(tree.Rows[1], group.ValueRow);
    }
}
