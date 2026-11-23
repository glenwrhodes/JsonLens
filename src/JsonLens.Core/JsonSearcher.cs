using System.Text.Json;

namespace JsonLens.Core;

public sealed record SearchOptions
{
    public bool MatchCase { get; init; }
    public bool SearchKeys { get; init; } = true;
    public bool SearchValues { get; init; } = true;
    public int MaxResults { get; init; } = 10_000;

    /// <summary>
    /// Use the plain one-thread walk with no subtree pre-check. Results are identical either way;
    /// tests use this as the reference to prove it.
    /// </summary>
    internal bool Sequential { get; init; }
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

        var settings = new Settings(term, options, cancellationToken);
        // Collect one match more than the limit: if it exists, the result is truncated.
        int limit = options.MaxResults >= int.MaxValue - 1 ? int.MaxValue : options.MaxResults + 1;
        var sink = new Sink(limit);
        Walk(root, new List<PathSegment>(), keyMatched: false, settings, sink);

        var matches = sink.Matches;
        bool truncated = matches.Count > options.MaxResults;
        if (truncated) matches.RemoveRange(options.MaxResults, matches.Count - options.MaxResults);
        return new SearchResult(matches, truncated);
    }

    /// <summary>What to look for; shared (read-only) by every thread of a search.</summary>
    private sealed class Settings
    {
        public Settings(string term, SearchOptions options, CancellationToken ct)
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
    }

    /// <summary>Matches found by one thread, up to a limit.</summary>
    private sealed class Sink
    {
        public Sink(int limit) => Limit = limit;

        public readonly int Limit;
        public readonly List<JsonMatch> Matches = new();
        private int _visited;

        public bool Full => Matches.Count >= Limit;
        public int Remaining => Limit - Matches.Count;

        public void Tick(CancellationToken ct)
        {
            if ((++_visited & 0xFFF) == 0) ct.ThrowIfCancellationRequested();
        }
    }

    /// <returns>False when the walk should stop (result limit reached).</returns>
    private static bool Walk(JsonElement element, List<PathSegment> path, bool keyMatched, Settings s, Sink sink)
    {
        sink.Tick(s.Ct);

        bool valueMatched = s.Options.SearchValues && ScalarMatches(element, s);
        if (keyMatched || valueMatched)
        {
            sink.Matches.Add(new JsonMatch(path.ToArray(), element));
            if (sink.Full) return false;
        }

        if (!s.Options.Sequential && HasAtLeast(element, ParallelWalk.Threshold))
            return WalkChildrenInParallel(element, path, s, sink);

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                int m = 0;
                foreach (var prop in element.EnumerateObject())
                {
                    bool km = s.Options.SearchKeys && prop.Name.Contains(s.Term, s.Comparison);
                    path.Add(PathSegment.Member(prop.Name, m++));
                    bool go = Walk(prop.Value, path, km, s, sink);
                    path.RemoveAt(path.Count - 1);
                    if (!go) return false;
                }
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    path.Add(PathSegment.At(i++));
                    bool go = Walk(item, path, false, s, sink);
                    path.RemoveAt(path.Count - 1);
                    if (!go) return false;
                }
                break;
        }
        return true;
    }

    /// <summary>
    /// Searches the children of a big container on several threads, in contiguous ranges, and
    /// appends each range's matches in order, so the result is exactly what the sequential walk
    /// finds. Ranges run in batches so a search that hits the result limit early stops early.
    /// </summary>
    private static bool WalkChildrenInParallel(JsonElement container, List<PathSegment> path, Settings s, Sink sink)
    {
        bool isObject = container.ValueKind == JsonValueKind.Object;
        var children = ParallelWalk.Children(container, ParallelWalk.ChildCount(container));
        var prefix = path.ToArray();
        var ranges = ParallelWalk.Ranges(children.Length);
        int batch = Environment.ProcessorCount * 2;

        for (int b = 0; b < ranges.Count; b += batch)
        {
            int count = Math.Min(batch, ranges.Count - b);
            var found = new Sink[count];
            int remaining = sink.Remaining;
            Parallel.For(0, count, new ParallelOptions { CancellationToken = s.Ct }, k =>
            {
                var (start, end) = ranges[b + k];
                var local = new Sink(remaining);
                var localPath = new List<PathSegment>(prefix.Length + 8);
                localPath.AddRange(prefix);
                for (int c = start; c < end; c++)
                {
                    var child = children[c];
                    bool km = isObject && s.Options.SearchKeys && child.Name!.Contains(s.Term, s.Comparison);
                    localPath.Add(isObject ? PathSegment.Member(child.Name!, c) : PathSegment.At(c));
                    bool go;
                    if (IsContainer(child.Value) && !MayContainMatch(child.Value, s))
                    {
                        // Nothing inside can match, so only the child's own key can.
                        go = true;
                        if (km)
                        {
                            local.Matches.Add(new JsonMatch(localPath.ToArray(), child.Value));
                            go = !local.Full;
                        }
                    }
                    else
                    {
                        go = Walk(child.Value, localPath, km, s, local);
                    }
                    localPath.RemoveAt(localPath.Count - 1);
                    if (!go) break;
                }
                found[k] = local;
            });

            foreach (var local in found)
            {
                foreach (var m in local.Matches)
                {
                    sink.Matches.Add(m);
                    if (sink.Full) return false;
                }
            }
        }
        return true;
    }

    private static bool IsContainer(JsonElement e) => e.ValueKind is JsonValueKind.Object or JsonValueKind.Array;

    /// <summary>
    /// Cheap exact pre-check for a whole subtree. Without backslashes (escapes) in its raw JSON,
    /// every member name, string, number and literal inside appears verbatim in the raw text, so if
    /// the raw text doesn't contain the term, nothing inside can match. With escapes, we can't tell.
    /// </summary>
    private static bool MayContainMatch(JsonElement container, Settings s)
    {
        string raw = container.GetRawText();
        return raw.Contains('\\') || raw.Contains(s.Term, s.Comparison);
    }

    /// <summary>True when the container has at least <paramref name="n"/> children (stops counting there).</summary>
    private static bool HasAtLeast(JsonElement e, int n)
    {
        if (e.ValueKind == JsonValueKind.Array) return e.GetArrayLength() >= n;
        if (e.ValueKind != JsonValueKind.Object) return false;
        int count = 0;
        foreach (var _ in e.EnumerateObject())
            if (++count >= n) return true;
        return false;
    }

    private static bool ScalarMatches(JsonElement element, Settings s)
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
