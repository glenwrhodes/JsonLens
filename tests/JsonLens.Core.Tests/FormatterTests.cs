using System.Text.Json;
using static JsonLens.Core.Tests.TestData;

namespace JsonLens.Core.Tests;

public class FormatterTests
{
    [Fact]
    public void Minify_removes_all_insignificant_whitespace() =>
        Assert.Equal("""{"a":[1,2,{"b":null}],"c":"x y"}""",
            JsonFormatter.Format("""{ "a" : [ 1, 2, { "b" : null } ], "c" : "x y" }""", pretty: false));

    [Fact]
    public void Pretty_indents_with_two_spaces_and_lf()
    {
        var pretty = JsonFormatter.Format("""{"a":[1,{"b":true}]}""", pretty: true);
        Assert.Equal("{\n  \"a\": [\n    1,\n    {\n      \"b\": true\n    }\n  ]\n}", pretty);
    }

    [Theory]
    [InlineData("1.10")]
    [InlineData("1e400")]
    [InlineData("-0")]
    [InlineData("123456789012345678901234567890")]
    [InlineData("0.1000000000000000055511151231257827")]
    [InlineData("2E-7")]
    public void Numbers_are_kept_exactly(string number)
    {
        var json = $"[{number}]";
        Assert.Equal(json, JsonFormatter.Format(json, pretty: false));
        Assert.Contains(number, JsonFormatter.Format(json, pretty: true));
    }

    [Fact]
    public void Non_ascii_stays_readable_and_escapes_stay_valid()
    {
        var min = JsonFormatter.Format("""{"name":"Montréal ✓ 名前","q":"say \"hi\"\n","emoji":"🦌"}""", pretty: false);
        Assert.Contains("Montréal ✓ 名前", min);
        Assert.Contains("\\\"hi\\\"\\n", min);
        // Still valid JSON with the same content (emoji may be written as a \u surrogate pair).
        using var doc = JsonDocument.Parse(min);
        Assert.Equal("say \"hi\"\n", doc.RootElement.GetProperty("q").GetString());
        Assert.Equal("🦌", doc.RootElement.GetProperty("emoji").GetString());
    }

    [Fact]
    public void Duplicate_keys_are_preserved() =>
        Assert.Equal("""{"a":1,"a":2}""", JsonFormatter.Format("""{"a":1, "a":2}""", pretty: false));

    [Fact]
    public void Pretty_and_minified_round_trip_to_the_same_document()
    {
        var root = Parse(Bookstore);
        var pretty = JsonFormatter.Pretty(root);
        var min = JsonFormatter.Minify(root);
        Assert.True(min.Length < pretty.Length);
        Assert.Equal(min, JsonFormatter.Format(pretty, pretty: false));
        Assert.Equal(pretty, JsonFormatter.Format(min, pretty: true));
    }

    [Fact]
    public void Limited_format_stops_early_and_flags_truncation()
    {
        var big = "[" + string.Join(",", Enumerable.Range(0, 100_000).Select(i => $"\"item {i}\"")) + "]";
        var root = Parse(big);
        var text = JsonFormatter.Format(root, pretty: true, maxChars: 5_000, out bool truncated);
        Assert.True(truncated);
        Assert.True(text.Length <= 5_000);
        Assert.StartsWith("[\n  \"item 0\",", text);

        var small = JsonFormatter.Format(Parse("[1,2]"), pretty: false, maxChars: 5_000, out truncated);
        Assert.False(truncated);
        Assert.Equal("[1,2]", small);
    }

    [Fact]
    public void Limited_format_never_splits_a_multibyte_character()
    {
        var root = Parse("[\"" + new string('é', 4000) + "\"]");
        for (int limit = 100; limit < 110; limit++)
        {
            var text = JsonFormatter.Format(root, pretty: false, maxChars: limit, out bool truncated);
            Assert.True(truncated);
            Assert.DoesNotContain('�', text);
        }
    }

    [Fact]
    public void WriteTo_stream_matches_string_output()
    {
        var root = Parse(Bookstore);
        using var ms = new MemoryStream();
        JsonFormatter.WriteTo(root, ms, pretty: false);
        Assert.Equal(JsonFormatter.Minify(root), System.Text.Encoding.UTF8.GetString(ms.ToArray()));
    }
}
