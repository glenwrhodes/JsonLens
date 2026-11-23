using JsonLens.Core.JsonPath;
using static JsonLens.Core.Tests.TestData;

namespace JsonLens.Core.Tests;

public class JsonPathTests
{
    // ---------------------------------------------------------------- RFC 9535 bookstore examples (table 2)

    [Fact]
    public void Authors_of_all_books() =>
        Assert.Equal(new[] { "\"Nigel Rees\"", "\"Evelyn Waugh\"", "\"Herman Melville\"", "\"J. R. R. Tolkien\"" },
            Values(Bookstore, "$.store.book[*].author"));

    [Fact]
    public void All_authors_by_descendant() =>
        Assert.Equal(4, Values(Bookstore, "$..author").Length);

    [Fact]
    public void Everything_in_store() =>
        Assert.Equal(new[] { "$.store.book", "$.store.bicycle" }, Paths(Bookstore, "$.store.*"));

    [Fact]
    public void All_prices_in_store() =>
        Assert.Equal(new[] { "8.95", "12.99", "8.99", "22.99", "399" }.OrderBy(x => x),
            Values(Bookstore, "$.store..price").OrderBy(x => x));

    [Fact]
    public void Third_book() =>
        Assert.Equal(new[] { "$.store.book[2]" }, Paths(Bookstore, "$..book[2]"));

    [Fact]
    public void Third_book_author() =>
        Assert.Equal(new[] { "\"Herman Melville\"" }, Values(Bookstore, "$..book[2].author"));

    [Fact]
    public void Third_book_publisher_is_empty() =>
        Assert.Empty(Values(Bookstore, "$..book[2].publisher"));

    [Fact]
    public void Last_book() =>
        Assert.Equal(new[] { "$.store.book[3]" }, Paths(Bookstore, "$..book[-1]"));

    [Theory]
    [InlineData("$..book[0,1]")]
    [InlineData("$..book[:2]")]
    public void First_two_books(string query) =>
        Assert.Equal(new[] { "$.store.book[0]", "$.store.book[1]" }, Paths(Bookstore, query));

    [Fact]
    public void Books_with_isbn() =>
        Assert.Equal(new[] { "$.store.book[2]", "$.store.book[3]" }, Paths(Bookstore, "$..book[?@.isbn]"));

    [Fact]
    public void Books_cheaper_than_10() =>
        Assert.Equal(new[] { "$.store.book[0]", "$.store.book[2]" }, Paths(Bookstore, "$..book[?@.price<10]"));

    [Fact]
    public void Old_style_parenthesised_filter_works() =>
        Assert.Equal(new[] { "$.store.book[0]", "$.store.book[2]" }, Paths(Bookstore, "$..book[?(@.price < 10)]"));

    [Fact]
    public void All_member_values_and_array_elements()
    {
        // Every node except the root.
        var root = Parse(Bookstore);
        long expected = JsonDoc.CountNodes(root) - 1;
        Assert.Equal(expected, Values(Bookstore, "$..*").Length);
    }

    [Fact]
    public void Root_alone_selects_the_document() =>
        Assert.Equal(new[] { "$" }, Paths(Bookstore, "$"));

    [Fact]
    public void Bracket_names_and_whitespace()
    {
        Assert.Equal(new[] { "\"red\"" }, Values(Bookstore, "$['store'] [\"bicycle\"] ['color']"));
        Assert.Equal(new[] { "\"red\"" }, Values(Bookstore, " $ . store . bicycle.color ".Replace(" . ", ".")));
    }

    [Fact]
    public void Filter_with_and_on_strings() =>
        Assert.Equal(new[] { "\"Moby Dick\"" },
            Values(Bookstore, "$.store.book[?@.category == 'fiction' && @.price < 10].title"));

    [Fact]
    public void Filter_comparing_to_absolute_query() =>
        Assert.Equal(new[] { "$.store.book[0]" }, Paths(Bookstore, "$.store.book[?@.author == $.store.book[0].author]"));

    // ---------------------------------------------------------------- RFC 9535 slice examples

    private const string Letters = """["a", "b", "c", "d", "e", "f", "g"]""";

    [Theory]
    [InlineData("$[1:3]", "b,c")]
    [InlineData("$[5:]", "f,g")]
    [InlineData("$[1:5:2]", "b,d")]
    [InlineData("$[5:1:-2]", "f,d")]
    [InlineData("$[::-1]", "g,f,e,d,c,b,a")]
    [InlineData("$[-2:]", "f,g")]
    [InlineData("$[:-5]", "a,b")]
    [InlineData("$[0:100]", "a,b,c,d,e,f,g")]
    [InlineData("$[3:3]", "")]
    [InlineData("$[::0]", "")]
    [InlineData("$[ 1 : 3 ]", "b,c")]
    public void Slices(string query, string expected)
    {
        var got = string.Join(",", Values(Letters, query).Select(v => v.Trim('"')));
        Assert.Equal(expected, got);
    }

    [Theory]
    [InlineData("$[0]", "a")]
    [InlineData("$[-1]", "g")]
    [InlineData("$[-7]", "a")]
    [InlineData("$[7]", "")]
    [InlineData("$[-8]", "")]
    [InlineData("$[0,0,6]", "a,a,g")]
    public void Indexes(string query, string expected) =>
        Assert.Equal(expected, string.Join(",", Values(Letters, query).Select(v => v.Trim('"'))));

    // ---------------------------------------------------------------- RFC 9535 filter examples (table 12)

    [Theory]
    [InlineData("$.a[?@.b == 'kilo']", "{\"b\":\"kilo\"}")]
    [InlineData("$.a[?(@.b == 'kilo')]", "{\"b\":\"kilo\"}")]
    [InlineData("$.a[?@>3.5]", "5|4|6")]
    [InlineData("$.a[?@.b]", "{\"b\":\"j\"}|{\"b\":\"k\"}|{\"b\":{}}|{\"b\":\"kilo\"}")]
    [InlineData("$.o[?@<3, ?@<3]", "1|2|1|2")]
    [InlineData("$.a[?@<2 || @.b == \"k\"]", "1|{\"b\":\"k\"}")]
    [InlineData("$.o[?@>1 && @<4]", "2|3")]
    [InlineData("$.o[?@.u || @.x]", "{\"u\":6}")]
    [InlineData("$.a[?@.b == $.x]", "3|5|1|2|4|6")]
    [InlineData("$.a[?!@.b]", "3|5|1|2|4|6")]
    [InlineData("$.a[?!(@.b == 'j')]", "3|5|1|2|4|6|{\"b\":\"k\"}|{\"b\":{}}|{\"b\":\"kilo\"}")]
    public void Filters(string query, string expected) =>
        Assert.Equal(expected, string.Join("|", Values(FilterDoc, query)));

    [Fact]
    public void Every_element_equals_itself() =>
        Assert.Equal(10, Values(FilterDoc, "$.a[?@ == @]").Length);

    [Fact]
    public void Existence_test_with_wildcard_selects_non_empty_containers() =>
        Assert.Equal(new[] { "$.a", "$.o" }, Paths(FilterDoc, "$[?@.*]"));

    [Fact]
    public void Deep_equality_of_containers()
    {
        const string json = """{"x": {"k": [1, 2, {"z": null}]}, "items": [{"k": [1, 2, {"z": null}]}, {"k": [1, 2]}, {"k": [1.0, 2e0, {"z": null}]}]}""";
        Assert.Equal(new[] { "$.items[0]", "$.items[2]" }, Paths(json, "$.items[?@.k == $.x.k]"));
    }

    [Fact]
    public void Comparisons_between_mismatched_types_are_false()
    {
        const string json = """[1, "1", true, null, [1], {"a": 1}]""";
        Assert.Equal(new[] { "1" }, Values(json, "$[?@ == 1]"));
        Assert.Equal(new[] { "\"1\"" }, Values(json, "$[?@ == '1']"));
        Assert.Equal(new[] { "1" }, Values(json, "$[?@ < 2]"));
        Assert.Equal(new[] { "\"1\"" }, Values(json, "$[?@ < '2']"));
        Assert.Equal(new[] { "true" }, Values(json, "$[?@ == true]"));
        Assert.Equal(new[] { "null" }, Values(json, "$[?@ == null]"));
        Assert.Equal(5, Values(json, "$[?@ != null]").Length);
        Assert.Equal(new[] { "1", "\"1\"" }, Values(json, "$[?@ <= 1 || @ <= '1']"));
    }

    [Fact]
    public void Operator_precedence_and_binds_tighter_than_or()
    {
        const string json = "[1, 2, 3, 4, 5]";
        Assert.Equal(new[] { "1", "4", "5" }, Values(json, "$[?@ == 1 || @ > 3 && @ <= 5]"));
        Assert.Equal(new[] { "4", "5" }, Values(json, "$[?(@ == 1 || @ > 3) && @ >= 4]"));
    }

    [Fact]
    public void Numbers_compare_numerically_whatever_the_spelling()
    {
        const string json = "[100, 1e2, 100.0, 99.99, -1E-2]";
        Assert.Equal(3, Values(json, "$[?@ == 100]").Length);
        Assert.Equal(new[] { "-1E-2" }, Values(json, "$[?@ < 0]"));
    }

    // ---------------------------------------------------------------- names, escapes, descendants

    [Fact]
    public void Names_that_need_brackets()
    {
        const string json = """{"a b": 1, "it's": 2, "quo\"te": 3, "": 4, "1st": 5, "é": 6, "tab\there": 7}""";
        Assert.Equal(new[] { "1" }, Values(json, "$['a b']"));
        Assert.Equal(new[] { "2" }, Values(json, "$['it\\'s']"));
        Assert.Equal(new[] { "2" }, Values(json, "$[\"it's\"]"));
        Assert.Equal(new[] { "3" }, Values(json, "$['quo\"te']"));
        Assert.Equal(new[] { "4" }, Values(json, "$['']"));
        Assert.Equal(new[] { "5" }, Values(json, "$['1st']"));
        Assert.Equal(new[] { "6" }, Values(json, "$.é"));
        Assert.Equal(new[] { "6" }, Values(json, "$['\\u00e9']"));
        Assert.Equal(new[] { "7" }, Values(json, "$['tab\\there']"));
    }

    [Fact]
    public void Descendant_order_is_document_order()
    {
        const string json = """{"a": {"n": 1, "b": {"n": 2}}, "c": [{"n": 3}, {"n": 4}], "n": 0}""";
        // RFC 9535: each visited node (root first) applies the selector, then its descendants are visited.
        Assert.Equal(new[] { "$.n", "$.a.n", "$.a.b.n", "$.c[0].n", "$.c[1].n" }, Paths(json, "$..n"));
        Assert.Equal(new[] { "$.c[0]", "$.c[1]" }, Paths(json, "$..[?@.n > 2]"));
        Assert.Equal(new[] { "$.c[1]" }, Paths(json, "$..c[1]"));
    }

    [Fact]
    public void Wildcards_on_scalars_select_nothing()
    {
        Assert.Empty(Values("""{"a": 1}""", "$.a.*"));
        Assert.Empty(Values("""{"a": 1}""", "$.a[0]"));
        Assert.Empty(Values("""{"a": [1]}""", "$.a.x"));
    }

    [Fact]
    public void Result_limit_truncates()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 50)) + "]";
        var result = JsonPathQuery.Parse("$[*]").Evaluate(Parse(json), maxResults: 10);
        Assert.Equal(10, result.Matches.Count);
        Assert.True(result.Truncated);

        var all = JsonPathQuery.Parse("$[*]").Evaluate(Parse(json), maxResults: 50);
        Assert.Equal(50, all.Matches.Count);
        Assert.False(all.Truncated);
    }

    [Fact]
    public void Evaluation_can_be_cancelled()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 20000).Select(i => $"{{\"v\":{i}}}")) + "]";
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            JsonPathQuery.Parse("$..v").Evaluate(Parse(json), cancellationToken: cts.Token));
    }

    [Fact]
    public void Singular_detection()
    {
        Assert.True(JsonPathQuery.Parse("$.a[0]['b']").IsSingular);
        Assert.False(JsonPathQuery.Parse("$.a[*]").IsSingular);
        Assert.False(JsonPathQuery.Parse("$..a").IsSingular);
        Assert.False(JsonPathQuery.Parse("$.a[0,1]").IsSingular);
    }

    // ---------------------------------------------------------------- errors

    [Theory]
    [InlineData("", 0)]
    [InlineData("store.book", 0)]
    [InlineData("$.store[", 7)]
    [InlineData("$.store.book[0", 12)]
    [InlineData("$.", 2)]
    [InlineData("$.1abc", 2)]
    [InlineData("$['abc]", 2)]
    [InlineData("$[abc]", 2)]
    [InlineData("$[]", 2)]
    [InlineData("$[?@.price < ]", 13)]
    [InlineData("$[?@.price = 1]", 11)]
    [InlineData("$[?length(@) > 1]", 3)]
    [InlineData("$[?'x']", 3)]
    [InlineData("$[?@.a == bogus]", 10)]
    [InlineData("$[?(@.a == 1]", 12)]
    [InlineData("$[?(@.a == 1", 3)]
    [InlineData("$[?@ == {}]", 8)]
    [InlineData("$.a extra", 4)]
    [InlineData("$[-]", 3)]
    [InlineData("$['\\q']", 3)]
    [InlineData("$[?@.a == 01]", 10)]
    [InlineData("$[?@.a == -01]", 10)]
    [InlineData("$[?@.a == 00.5]", 10)]
    [InlineData("$[01]", 2)]
    [InlineData("$[-0]", 2)]
    [InlineData("$[00:2]", 2)]
    [InlineData("$[0:02]", 4)]
    public void Errors_report_the_position(string query, int position)
    {
        var ex = Assert.Throws<JsonPathException>(() => JsonPathQuery.Parse(query));
        Assert.Equal(position, ex.Position);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Leading_zero_message_is_specific()
    {
        var ex = Assert.Throws<JsonPathException>(() => JsonPathQuery.Parse("$[?@.a == 01]"));
        Assert.Contains("leading zero", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Zero_and_zero_fractions_and_exponents_are_still_valid()
    {
        // RFC 9535: int = "0" / (["-"] DIGIT1 *DIGIT); number = (int / "-0") [frac] [exp]; exp digits may start with 0.
        const string json = "[0, 0.5, -0.5, 10, 100]";
        Assert.Equal(new[] { "0" }, Values(json, "$[?@ == 0]"));
        Assert.Equal(new[] { "0" }, Values(json, "$[?@ == -0]"));
        Assert.Equal(new[] { "0.5" }, Values(json, "$[?@ == 0.5]"));
        Assert.Equal(new[] { "-0.5" }, Values(json, "$[?@ == -0.5]"));
        Assert.Equal(new[] { "10" }, Values(json, "$[?@ == 1e01]"));
        Assert.Equal(new[] { "100" }, Values(json, "$[?@ == 1E+02]"));
        Assert.Equal(new[] { "0" }, Values(json, "$[0]"));
        Assert.Equal(new[] { "10", "100" }, Values(json, "$[-2:]"));
        Assert.Equal(new[] { "0", "0.5" }, Values(json, "$[0:2]"));
    }

    [Fact]
    public void TryParse_reports_errors_without_throwing()
    {
        Assert.False(JsonPathQuery.TryParse("$[", out var q, out var err));
        Assert.Null(q);
        Assert.NotNull(err);
        Assert.True(JsonPathQuery.TryParse("$.a", out q, out err));
        Assert.NotNull(q);
        Assert.Null(err);
    }
}
