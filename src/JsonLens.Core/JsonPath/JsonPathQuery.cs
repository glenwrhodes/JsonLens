using System.Text.Json;

namespace JsonLens.Core.JsonPath;

/// <summary>A query that couldn't be parsed. <see cref="Position"/> is the 0-based character offset of the problem.</summary>
public sealed class JsonPathException : Exception
{
    public JsonPathException(string message, int position) : base(message)
    {
        Position = position;
    }

    public int Position { get; }
}

public sealed class QueryResult
{
    public QueryResult(IReadOnlyList<JsonMatch> matches, bool truncated)
    {
        Matches = matches;
        Truncated = truncated;
    }

    public IReadOnlyList<JsonMatch> Matches { get; }

    /// <summary>True when there were more results than the limit passed to Evaluate.</summary>
    public bool Truncated { get; }
}

/// <summary>
/// A parsed JSONPath query (the core of RFC 9535): <c>$</c>, <c>.name</c>, <c>['name']</c>, <c>[n]</c>,
/// <c>[-n]</c>, <c>*</c>, <c>..</c>, slices <c>[a:b:c]</c>, unions <c>[0,2]</c> and filters
/// <c>[?@.price &lt; 10 &amp;&amp; @.tag == 'x']</c> with <c>== != &lt; &lt;= &gt; &gt;=</c>, existence tests,
/// <c>&amp;&amp;</c>, <c>||</c>, <c>!</c> and parentheses. Function extensions are not supported.
/// </summary>
public sealed class JsonPathQuery
{
    private readonly QueryAst _ast;

    private JsonPathQuery(string text, QueryAst ast)
    {
        Text = text;
        _ast = ast;
    }

    public string Text { get; }

    /// <summary>True when the query can select at most one node (only names and indexes).</summary>
    public bool IsSingular => _ast.IsSingular;

    /// <exception cref="JsonPathException">The query is not valid.</exception>
    public static JsonPathQuery Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ast = new Parser(text).ParseRootQuery();
        return new JsonPathQuery(text, ast);
    }

    public static bool TryParse(string text, out JsonPathQuery? query, out JsonPathException? error)
    {
        try
        {
            query = Parse(text);
            error = null;
            return true;
        }
        catch (JsonPathException ex)
        {
            query = null;
            error = ex;
            return false;
        }
    }

    public QueryResult Evaluate(JsonElement root, int maxResults = 100_000, CancellationToken cancellationToken = default)
    {
        if (maxResults < 0) throw new ArgumentOutOfRangeException(nameof(maxResults));
        var ctx = new EvalContext(root, trackPaths: true, cancellationToken) { OutputLimit = maxResults + 1 };
        var nodes = _ast.Evaluate(root, ctx);

        bool truncated = nodes.Count > maxResults;
        int count = Math.Min(nodes.Count, maxResults);
        var matches = new JsonMatch[count];
        for (int i = 0; i < count; i++)
        {
            matches[i] = new JsonMatch(PathLink.ToArray(nodes[i].Path), nodes[i].Value);
        }
        return new QueryResult(matches, truncated);
    }

    public override string ToString() => Text;
}
