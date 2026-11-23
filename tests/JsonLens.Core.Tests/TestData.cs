using System.Text.Json;
using JsonLens.Core;
using JsonLens.Core.JsonPath;

namespace JsonLens.Core.Tests;

internal static class TestData
{
    /// <summary>The example document from RFC 9535, section 1.5.</summary>
    public const string Bookstore = """
        { "store": {
            "book": [
              { "category": "reference", "author": "Nigel Rees", "title": "Sayings of the Century", "price": 8.95 },
              { "category": "fiction", "author": "Evelyn Waugh", "title": "Sword of Honour", "price": 12.99 },
              { "category": "fiction", "author": "Herman Melville", "title": "Moby Dick", "isbn": "0-553-21311-3", "price": 8.99 },
              { "category": "fiction", "author": "J. R. R. Tolkien", "title": "The Lord of the Rings", "isbn": "0-395-19395-8", "price": 22.99 }
            ],
            "bicycle": { "color": "red", "price": 399 }
          }
        }
        """;

    /// <summary>The filter example document from RFC 9535, section 2.3.5.3.</summary>
    public const string FilterDoc = """
        { "a": [3, 5, 1, 2, 4, 6, {"b": "j"}, {"b": "k"}, {"b": {}}, {"b": "kilo"}],
          "o": {"p": 1, "q": 2, "r": 3, "s": 5, "t": {"u": 6}},
          "e": "f" }
        """;

    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    /// <summary>Runs a query and returns each result as minified JSON.</summary>
    public static string[] Values(string json, string query) =>
        JsonPathQuery.Parse(query).Evaluate(Parse(json)).Matches
            .Select(m => JsonFormatter.Minify(m.Value)).ToArray();

    public static string[] Paths(string json, string query) =>
        JsonPathQuery.Parse(query).Evaluate(Parse(json)).Matches.Select(m => m.PathText).ToArray();
}
