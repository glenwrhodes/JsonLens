using System.Globalization;
using System.Text.Json;

namespace JsonLens.Core.JsonPath;

/// <summary>Immutable linked list of path segments, shared between parent and child nodes.</summary>
internal sealed class PathLink
{
    public PathLink(PathLink? parent, PathSegment segment)
    {
        Parent = parent;
        Segment = segment;
        Depth = (parent?.Depth ?? 0) + 1;
    }

    public PathLink? Parent { get; }
    public PathSegment Segment { get; }
    public int Depth { get; }

    public static PathSegment[] ToArray(PathLink? link)
    {
        if (link is null) return Array.Empty<PathSegment>();
        var result = new PathSegment[link.Depth];
        for (var l = link; l is not null; l = l.Parent) result[l.Depth - 1] = l.Segment;
        return result;
    }
}

internal readonly record struct Node(JsonElement Value, PathLink? Path);

internal sealed class EvalContext
{
    private int _ticks;

    public EvalContext(JsonElement root, bool trackPaths, CancellationToken ct)
    {
        Root = root;
        TrackPaths = trackPaths;
        Ct = ct;
    }

    public JsonElement Root { get; }
    public bool TrackPaths { get; }
    public CancellationToken Ct { get; }

    /// <summary>Stop adding to the final nodelist once it holds this many nodes.</summary>
    public int OutputLimit { get; init; } = int.MaxValue;

    public void Tick()
    {
        if ((++_ticks & 0xFFF) == 0) Ct.ThrowIfCancellationRequested();
    }

    public Node Child(Node parent, PathSegment segment, JsonElement value) =>
        new(value, TrackPaths ? new PathLink(parent.Path, segment) : null);

    /// <summary>Segment for the member at <paramref name="memberIndex"/>; skips allocating the name when paths aren't tracked.</summary>
    public PathSegment MemberSegment(JsonProperty prop, int memberIndex) =>
        TrackPaths ? PathSegment.Member(prop.Name, memberIndex) : default;

    /// <summary>Same root and cancellation, but no path tracking (for queries inside filters).</summary>
    public EvalContext ForFilter() => _filterContext ??= new EvalContext(Root, false, Ct);
    private EvalContext? _filterContext;
}

internal sealed class QueryAst
{
    public QueryAst(bool relative, Segment[] segments)
    {
        Relative = relative;
        Segments = segments;
        IsSingular = segments.All(s => !s.Descendant && s.Selectors.Length == 1 &&
                                       s.Selectors[0] is NameSelector or IndexSelector);
    }

    public bool Relative { get; }
    public Segment[] Segments { get; }
    public bool IsSingular { get; }

    public List<Node> Evaluate(JsonElement start, EvalContext ctx)
    {
        var current = new List<Node> { new(start, null) };
        for (int i = 0; i < Segments.Length; i++)
        {
            int limit = i == Segments.Length - 1 ? ctx.OutputLimit : int.MaxValue;
            var next = new List<Node>();
            var segment = Segments[i];
            foreach (var node in current)
            {
                if (segment.Descendant)
                    Descend(node, segment.Selectors, next, ctx, limit);
                else
                    foreach (var sel in segment.Selectors) sel.Select(node, next, ctx);

                if (next.Count >= limit) break;
            }
            current = next;
            if (current.Count == 0) break;
        }
        return current;
    }

    /// <summary>Fast path for singular queries in filters: follow names and indexes directly.</summary>
    public bool TryEvaluateSingular(JsonElement start, out JsonElement result)
    {
        result = start;
        foreach (var seg in Segments)
        {
            switch (seg.Selectors[0])
            {
                case NameSelector n:
                    if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(n.Name, out result)) return false;
                    break;
                case IndexSelector ix:
                    if (result.ValueKind != JsonValueKind.Array) return false;
                    long len = result.GetArrayLength();
                    long idx = ix.Index < 0 ? len + ix.Index : ix.Index;
                    if (idx < 0 || idx >= len) return false;
                    result = result[(int)idx];
                    break;
                default:
                    return false;
            }
        }
        return true;
    }

    private static void Descend(Node node, Selector[] selectors, List<Node> output, EvalContext ctx, int limit)
    {
        ctx.Tick();
        foreach (var sel in selectors) sel.Select(node, output, ctx);
        if (output.Count >= limit) return;

        switch (node.Value.ValueKind)
        {
            case JsonValueKind.Object:
                int m = 0;
                foreach (var prop in node.Value.EnumerateObject())
                {
                    Descend(ctx.Child(node, ctx.MemberSegment(prop, m++), prop.Value), selectors, output, ctx, limit);
                    if (output.Count >= limit) return;
                }
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in node.Value.EnumerateArray())
                {
                    Descend(ctx.Child(node, PathSegment.At(i++), item), selectors, output, ctx, limit);
                    if (output.Count >= limit) return;
                }
                break;
        }
    }
}

internal sealed class Segment
{
    public Segment(bool descendant, Selector[] selectors)
    {
        Descendant = descendant;
        Selectors = selectors;
    }

    public bool Descendant { get; }
    public Selector[] Selectors { get; }
}

internal abstract class Selector
{
    public abstract void Select(Node input, List<Node> output, EvalContext ctx);
}

internal sealed class NameSelector : Selector
{
    public NameSelector(string name) => Name = name;
    public string Name { get; }

    public override void Select(Node input, List<Node> output, EvalContext ctx)
    {
        if (input.Value.ValueKind != JsonValueKind.Object) return;
        if (!ctx.TrackPaths)
        {
            // TryGetProperty matches the last definition of a repeated name, like JSON parsers do.
            if (input.Value.TryGetProperty(Name, out var child)) output.Add(new Node(child, null));
            return;
        }

        // Find the same member (the last one with this name) and remember its position.
        int i = 0, found = -1;
        JsonElement value = default;
        foreach (var prop in input.Value.EnumerateObject())
        {
            if (prop.NameEquals(Name))
            {
                found = i;
                value = prop.Value;
            }
            i++;
        }
        if (found >= 0) output.Add(ctx.Child(input, PathSegment.Member(Name, found), value));
    }
}

internal sealed class WildcardSelector : Selector
{
    public static readonly WildcardSelector Instance = new();

    public override void Select(Node input, List<Node> output, EvalContext ctx)
    {
        switch (input.Value.ValueKind)
        {
            case JsonValueKind.Object:
                int m = 0;
                foreach (var prop in input.Value.EnumerateObject())
                    output.Add(ctx.Child(input, ctx.MemberSegment(prop, m++), prop.Value));
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in input.Value.EnumerateArray())
                    output.Add(ctx.Child(input, PathSegment.At(i++), item));
                break;
        }
    }
}

internal sealed class IndexSelector : Selector
{
    public IndexSelector(long index) => Index = index;
    public long Index { get; }

    public override void Select(Node input, List<Node> output, EvalContext ctx)
    {
        if (input.Value.ValueKind != JsonValueKind.Array) return;
        long len = input.Value.GetArrayLength();
        long idx = Index < 0 ? len + Index : Index;
        if (idx >= 0 && idx < len)
            output.Add(ctx.Child(input, PathSegment.At((int)idx), input.Value[(int)idx]));
    }
}

internal sealed class SliceSelector : Selector
{
    public SliceSelector(long? start, long? end, long? step)
    {
        Start = start;
        End = end;
        Step = step;
    }

    public long? Start { get; }
    public long? End { get; }
    public long? Step { get; }

    public override void Select(Node input, List<Node> output, EvalContext ctx)
    {
        if (input.Value.ValueKind != JsonValueKind.Array) return;
        long len = input.Value.GetArrayLength();
        long step = Step ?? 1;
        if (step == 0) return;

        // Indexing an array with complex children is not O(1) in JsonDocument, so take a snapshot.
        JsonElement[]? items = null;
        JsonElement Item(long i) => (items ??= input.Value.EnumerateArray().ToArray())[i];

        if (step > 0)
        {
            long lower = Math.Min(Math.Max(Normalize(Start ?? 0, len), 0), len);
            long upper = Math.Min(Math.Max(Normalize(End ?? len, len), 0), len);
            for (long i = lower; i < upper; i += step)
                output.Add(ctx.Child(input, PathSegment.At((int)i), Item(i)));
        }
        else
        {
            long upper = Math.Min(Math.Max(Normalize(Start ?? len - 1, len), -1), len - 1);
            long lower = Math.Min(Math.Max(Normalize(End ?? -len - 1, len), -1), len - 1);
            for (long i = upper; lower < i; i += step)
                output.Add(ctx.Child(input, PathSegment.At((int)i), Item(i)));
        }
    }

    private static long Normalize(long i, long len) => i >= 0 ? i : len + i;
}

internal sealed class FilterSelector : Selector
{
    public FilterSelector(LogicalExpr expr) => Expr = expr;
    public LogicalExpr Expr { get; }

    public override void Select(Node input, List<Node> output, EvalContext ctx)
    {
        switch (input.Value.ValueKind)
        {
            case JsonValueKind.Object:
                int m = 0;
                foreach (var prop in input.Value.EnumerateObject())
                {
                    ctx.Tick();
                    if (Expr.Eval(prop.Value, ctx))
                        output.Add(ctx.Child(input, ctx.MemberSegment(prop, m), prop.Value));
                    m++;
                }
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in input.Value.EnumerateArray())
                {
                    ctx.Tick();
                    if (Expr.Eval(item, ctx))
                        output.Add(ctx.Child(input, PathSegment.At(i), item));
                    i++;
                }
                break;
        }
    }
}

// ---------------------------------------------------------------- filter expressions

internal abstract class LogicalExpr
{
    public abstract bool Eval(JsonElement current, EvalContext ctx);
}

internal sealed class OrExpr : LogicalExpr
{
    public OrExpr(LogicalExpr[] items) => Items = items;
    public LogicalExpr[] Items { get; }
    public override bool Eval(JsonElement current, EvalContext ctx) => Items.Any(e => e.Eval(current, ctx));
}

internal sealed class AndExpr : LogicalExpr
{
    public AndExpr(LogicalExpr[] items) => Items = items;
    public LogicalExpr[] Items { get; }
    public override bool Eval(JsonElement current, EvalContext ctx) => Items.All(e => e.Eval(current, ctx));
}

internal sealed class NotExpr : LogicalExpr
{
    public NotExpr(LogicalExpr inner) => Inner = inner;
    public LogicalExpr Inner { get; }
    public override bool Eval(JsonElement current, EvalContext ctx) => !Inner.Eval(current, ctx);
}

/// <summary>A query used as a test: true when it selects at least one node.</summary>
internal sealed class ExistsExpr : LogicalExpr
{
    public ExistsExpr(QueryAst query) => Query = query;
    public QueryAst Query { get; }

    public override bool Eval(JsonElement current, EvalContext ctx)
    {
        var start = Query.Relative ? current : ctx.Root;
        if (Query.IsSingular) return Query.TryEvaluateSingular(start, out _);
        var fctx = ctx.ForFilter();
        var limited = new EvalContext(fctx.Root, false, fctx.Ct) { OutputLimit = 1 };
        return Query.Evaluate(start, limited).Count > 0;
    }
}

internal enum CompareOp { Eq, Ne, Lt, Le, Gt, Ge }

internal sealed class ComparisonExpr : LogicalExpr
{
    public ComparisonExpr(Comparable left, CompareOp op, Comparable right)
    {
        Left = left;
        Op = op;
        Right = right;
    }

    public Comparable Left { get; }
    public CompareOp Op { get; }
    public Comparable Right { get; }

    public override bool Eval(JsonElement current, EvalContext ctx)
    {
        var a = Left.Value(current, ctx);
        var b = Right.Value(current, ctx);
        return Op switch
        {
            CompareOp.Eq => FValue.AreEqual(a, b),
            CompareOp.Ne => !FValue.AreEqual(a, b),
            CompareOp.Lt => FValue.IsLess(a, b),
            CompareOp.Le => FValue.IsLess(a, b) || FValue.AreEqual(a, b),
            CompareOp.Gt => FValue.IsLess(b, a),
            CompareOp.Ge => FValue.IsLess(b, a) || FValue.AreEqual(a, b),
            _ => false,
        };
    }
}

internal abstract class Comparable
{
    public abstract FValue Value(JsonElement current, EvalContext ctx);
}

internal sealed class LiteralComparable : Comparable
{
    public LiteralComparable(FValue value) => Literal = value;
    public FValue Literal { get; }
    public override FValue Value(JsonElement current, EvalContext ctx) => Literal;
}

internal sealed class QueryComparable : Comparable
{
    public QueryComparable(QueryAst query) => Query = query;
    public QueryAst Query { get; }

    public override FValue Value(JsonElement current, EvalContext ctx)
    {
        var start = Query.Relative ? current : ctx.Root;
        if (Query.IsSingular)
            return Query.TryEvaluateSingular(start, out var el) ? FValue.From(el) : FValue.Nothing;

        // RFC 9535 only allows singular queries here; we accept any query and use it when it selects exactly one node.
        var fctx = ctx.ForFilter();
        var limited = new EvalContext(fctx.Root, false, fctx.Ct) { OutputLimit = 2 };
        var nodes = Query.Evaluate(start, limited);
        return nodes.Count == 1 ? FValue.From(nodes[0].Value) : FValue.Nothing;
    }
}

internal enum FKind { Nothing, Null, True, False, Number, String, Array, Object }

/// <summary>A value in a comparison: a JSON value, a literal, or Nothing (an empty query result).</summary>
internal readonly struct FValue
{
    private FValue(FKind kind, double number = 0, string? text = null, JsonElement element = default)
    {
        Kind = kind;
        Number = number;
        Text = text;
        Element = element;
    }

    public FKind Kind { get; }
    public double Number { get; }
    public string? Text { get; }
    public JsonElement Element { get; }

    public static readonly FValue Nothing = new(FKind.Nothing);
    public static readonly FValue Null = new(FKind.Null);
    public static readonly FValue True = new(FKind.True);
    public static readonly FValue False = new(FKind.False);
    public static FValue FromNumber(double d) => new(FKind.Number, number: d);
    public static FValue FromString(string s) => new(FKind.String, text: s);

    public static FValue From(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Null => Null,
        JsonValueKind.True => True,
        JsonValueKind.False => False,
        JsonValueKind.Number => FromNumber(ToDouble(el)),
        JsonValueKind.String => FromString(el.GetString()!),
        JsonValueKind.Array => new FValue(FKind.Array, element: el),
        JsonValueKind.Object => new FValue(FKind.Object, element: el),
        _ => Nothing,
    };

    public static double ToDouble(JsonElement number) =>
        number.TryGetDouble(out var d)
            ? d
            : double.Parse(number.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);

    public static bool AreEqual(FValue a, FValue b)
    {
        if (a.Kind != b.Kind) return false;
        return a.Kind switch
        {
            FKind.Number => a.Number == b.Number,
            FKind.String => string.Equals(a.Text, b.Text, StringComparison.Ordinal),
            FKind.Array or FKind.Object => DeepEquals(a.Element, b.Element),
            _ => true, // Nothing, null, true, false
        };
    }

    public static bool IsLess(FValue a, FValue b)
    {
        if (a.Kind == FKind.Number && b.Kind == FKind.Number) return a.Number < b.Number;
        if (a.Kind == FKind.String && b.Kind == FKind.String) return string.CompareOrdinal(a.Text, b.Text) < 0;
        return false;
    }

    public static bool DeepEquals(JsonElement x, JsonElement y)
    {
        if (x.ValueKind != y.ValueKind) return false;
        switch (x.ValueKind)
        {
            case JsonValueKind.Number:
                return ToDouble(x) == ToDouble(y);
            case JsonValueKind.String:
                return x.GetString() == y.GetString();
            case JsonValueKind.Array:
            {
                if (x.GetArrayLength() != y.GetArrayLength()) return false;
                using var ex = x.EnumerateArray();
                using var ey = y.EnumerateArray();
                while (ex.MoveNext() && ey.MoveNext())
                    if (!DeepEquals(ex.Current, ey.Current)) return false;
                return true;
            }
            case JsonValueKind.Object:
            {
                int countX = 0;
                foreach (var p in x.EnumerateObject())
                {
                    countX++;
                    if (!y.TryGetProperty(p.Name, out var other) || !DeepEquals(p.Value, other)) return false;
                }
                int countY = 0;
                foreach (var _ in y.EnumerateObject()) countY++;
                return countX == countY;
            }
            default:
                return true;
        }
    }
}
