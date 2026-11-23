using System.Collections.Concurrent;
using System.Text.Json;

namespace JsonLens.Core;

/// <summary>
/// Helpers for spreading work over big containers. A parsed <see cref="JsonDocument"/> is safe to
/// read from several threads at once, so the children of a big array or object can be processed
/// in parallel, in contiguous ranges, and the per-range results put back together in order.
/// </summary>
internal static class ParallelWalk
{
    /// <summary>Containers with at least this many children are split across threads.</summary>
    public const int Threshold = 512;

    /// <summary>One child of a container: its member name (objects only), its position and its value.</summary>
    public readonly record struct Child(string? Name, int Position, JsonElement Value);

    public static int ChildCount(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Array => e.GetArrayLength(),
        JsonValueKind.Object => CountMembers(e),
        _ => 0,
    };

    private static int CountMembers(JsonElement obj)
    {
        int n = 0;
        foreach (var _ in obj.EnumerateObject()) n++;
        return n;
    }

    /// <summary>The children of a container, with member names, in document order.</summary>
    public static Child[] Children(JsonElement container, int count)
    {
        var children = new Child[count];
        int i = 0;
        if (container.ValueKind == JsonValueKind.Object)
            foreach (var p in container.EnumerateObject()) children[i] = new Child(p.Name, i++, p.Value);
        else
            foreach (var e in container.EnumerateArray()) children[i] = new Child(null, i++, e);
        return children;
    }

    /// <summary>Contiguous ranges sized so every thread gets several, for load balancing.</summary>
    public static List<(int Start, int End)> Ranges(int count)
    {
        int size = Math.Max(64, count / (Environment.ProcessorCount * 4));
        var ranges = new List<(int, int)>();
        for (int s = 0; s < count; s += size) ranges.Add((s, Math.Min(count, s + size)));
        return ranges;
    }

    /// <summary>Total number of values under (and including) <paramref name="element"/>.</summary>
    public static long CountNodes(JsonElement element, CancellationToken ct)
    {
        int n = ChildCount(element);
        if (n == 0) return 1;
        if (n < Threshold)
        {
            long count = 1;
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var p in element.EnumerateObject()) count += CountNodes(p.Value, ct);
            else
                foreach (var e in element.EnumerateArray()) count += CountNodes(e, ct);
            return count;
        }

        var values = new JsonElement[n];
        int i = 0;
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var p in element.EnumerateObject()) values[i++] = p.Value;
        else
            foreach (var e in element.EnumerateArray()) values[i++] = e;

        long total = 1;
        Parallel.ForEach(Partitioner.Create(0, n, Math.Max(64, n / (Environment.ProcessorCount * 4))),
            new ParallelOptions { CancellationToken = ct },
            () => 0L,
            (range, _, local) =>
            {
                for (int k = range.Item1; k < range.Item2; k++) local += JsonDoc.CountNodes(values[k]);
                return local;
            },
            local => Interlocked.Add(ref total, local));
        return total;
    }
}
