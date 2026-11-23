using System.Text.Json;

namespace JsonLens.Core;

public sealed record SearchOptions
{
    public bool MatchCase { get; init; }
    public bool SearchKeys { get; init; } = true;
    public bool SearchValues { get; init; } = true;
    public int MaxResults { get; init; } = 10_000;
}

public sealed class SearchResult
{
    public SearchResult(IReadOnlyList<JsonMatch> matches, bool truncated)
    {
        Matches = matches;
        Truncated = truncated;
    }

    public static SearchResult Empty { get; } = new(Array.Empty<JsonMatch>(), false);

    public IReadOnlyList<JsonMatch> Matches { get; }

    /// <summary>True when the search stopped at <see cref="SearchOptions.MaxResults"/>.</summary>
    public bool Truncated { get; }
}

/// <summary>
/// Plain-text search over member names and scalar values, in document order.
/// A node is reported once even if both its key and its value match.
/// </summary>
public static class JsonSearcher
{
    public static SearchResult Search(JsonElement root, string term, SearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SearchOptions();
        if (string.IsNullOrEmpty(term)) return SearchResult.Empty;

        var state = new State(term, options, cancellationToken);
        var path = new List<PathSegment>();
        Walk(root, path, keyMatched: false, state);
        return new SearchResult(state.Matches, state.Truncated);
    }

    private sealed class State
    {
        public State(string term, SearchOptions options, CancellationToken ct)
        {
            Term = term;
            Options = options;
            Comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            Ct = ct;
        }

        public readonly string Term;
        public readonly SearchOptions Options;
        public readonly StringComparison Comparison;
        public readonly CancellationToken Ct;
        public readonly List<JsonMatch> Matches = new();
        public bool Truncated;
        public int Visited;

        public bool Full => Matches.Count >= Options.MaxResults;
    }

    /// <returns>False when the walk should stop (result limit reached).</returns>
    private static bool Walk(JsonElement element, List<PathSegment> path, bool keyMatched, State s)
    {
        if ((++s.Visited & 0xFFF) == 0) s.Ct.ThrowIfCancellationRequested();

        bool valueMatched = s.Options.SearchValues && ScalarMatches(element, s);
        if (keyMatched || valueMatched)
        {
            if (s.Full)
            {
                s.Truncated = true;
                return false;
            }
            s.Matches.Add(new JsonMatch(path.ToArray(), element));
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                int m = 0;
                foreach (var prop in element.EnumerateObject())
                {
                    bool km = s.Options.SearchKeys && prop.Name.Contains(s.Term, s.Comparison);
                    path.Add(PathSegment.Member(prop.Name, m++));
                    bool go = Walk(prop.Value, path, km, s);
                    path.RemoveAt(path.Count - 1);
                    if (!go) return false;
                }
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    path.Add(PathSegment.At(i++));
                    bool go = Walk(item, path, false, s);
                    path.RemoveAt(path.Count - 1);
                    if (!go) return false;
                }
                break;
        }
        return true;
    }

    private static bool ScalarMatches(JsonElement element, State s)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString()!.Contains(s.Term, s.Comparison);
            case JsonValueKind.Number:
                return element.GetRawText().Contains(s.Term, s.Comparison);
            case JsonValueKind.True:
                return "true".Contains(s.Term, s.Comparison);
            case JsonValueKind.False:
                return "false".Contains(s.Term, s.Comparison);
            case JsonValueKind.Null:
                return "null".Contains(s.Term, s.Comparison);
            default:
                return false;
        }
    }
}
