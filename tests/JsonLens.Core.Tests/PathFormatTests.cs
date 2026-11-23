using JsonLens.Core.JsonPath;
using static JsonLens.Core.Tests.TestData;

namespace JsonLens.Core.Tests;

public class PathFormatTests
{
    private static PathSegment M(string n) => PathSegment.Member(n);
    private static PathSegment I(int i) => PathSegment.At(i);

    [Fact]
    public void Root_is_dollar_and_empty_pointer()
    {
        Assert.Equal("$", PathFormat.ToJsonPath(Array.Empty<PathSegment>()));
        Assert.Equal("", PathFormat.ToJsonPointer(Array.Empty<PathSegment>()));
    }

    [Fact]
    public void Plain_names_use_dots_and_indexes_use_brackets() =>
        Assert.Equal("$.store.book[0].title", PathFormat.ToJsonPath(new[] { M("store"), M("book"), I(0), M("title") }));

    [Theory]
    [InlineData("odd key", "$['odd key']")]
    [InlineData("it's", "$['it\\'s']")]
    [InlineData("back\\slash", "$['back\\\\slash']")]
    [InlineData("", "$['']")]
    [InlineData("1st", "$['1st']")]
    [InlineData("a-b", "$['a-b']")]
    [InlineData("line\nbreak", "$['line\\nbreak']")]
    [InlineData("\u0001", "$['\\u0001']")]
    [InlineData("_private", "$._private")]
    [InlineData("café", "$.café")]
    [InlineData("名前", "$.名前")]
    [InlineData("x9", "$.x9")]
    public void Names_are_escaped_when_needed(string name, string expected) =>
        Assert.Equal(expected, PathFormat.ToJsonPath(new[] { M(name) }));

    [Fact]
    public void Pointer_escapes_tilde_and_slash() =>
        Assert.Equal("/a~1b/m~0n/3/", PathFormat.ToJsonPointer(new[] { M("a/b"), M("m~n"), I(3), M("") }));

    [Fact]
    public void Every_path_parses_back_to_the_same_node()
    {
        const string json = """
            {"plain": {"odd key": [10, {"it's": {"back\\slash": {"": {"1st": {"line\nbreak": {"café": {"\u0001": "deep"}}}}}}}]}}
            """;
        var root = Parse(json);
        foreach (var match in JsonPathQuery.Parse("$..*").Evaluate(root).Matches)
        {
            var text = PathFormat.ToJsonPath(match.Path);
            var again = JsonPathQuery.Parse(text).Evaluate(root).Matches;
            Assert.Single(again);
            Assert.Equal(match.Path, again[0].Path);
            Assert.Equal(match.Value.GetRawText(), again[0].Value.GetRawText());
        }
    }

    [Fact]
    public void Resolve_follows_a_path()
    {
        var root = Parse(Bookstore);
        Assert.True(JsonDoc.TryResolve(root, new[] { M("store"), M("book"), I(2), M("title") }, out var title));
        Assert.Equal("Moby Dick", title.GetString());
        Assert.False(JsonDoc.TryResolve(root, new[] { M("store"), M("book"), I(9) }, out _));
        Assert.False(JsonDoc.TryResolve(root, new[] { M("store"), I(0) }, out _));
    }
}
