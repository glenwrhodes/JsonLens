using System.Globalization;
using System.Text;
using System.Text.Json;

namespace JsonLens.Core;

/// <summary>One step in a path to a node: either an object member name or an array index.</summary>
public readonly record struct PathSegment
{
    private PathSegment(string? name, int index)
    {
        Name = name;
        Index = index;
    }

    /// <summary>Member name, or null when this segment is an array index.</summary>
    public string? Name { get; }

    /// <summary>Array index (only meaningful when <see cref="IsIndex"/>).</summary>
    public int Index { get; }

    public bool IsIndex => Name is null;

    public static PathSegment Member(string name) => new(name ?? throw new ArgumentNullException(nameof(name)), -1);

    public static PathSegment At(int index) =>
        index < 0 ? throw new ArgumentOutOfRangeException(nameof(index)) : new PathSegment(null, index);

    public override string ToString() => IsIndex ? $"[{Index}]" : Name!;
}

/// <summary>A node found by a search or query: where it is and what it is.</summary>
public sealed class JsonMatch
{
    public JsonMatch(IReadOnlyList<PathSegment> path, JsonElement value)
    {
        Path = path;
        Value = value;
    }

    public IReadOnlyList<PathSegment> Path { get; }
    public JsonElement Value { get; }

    private string? _pathText;
    public string PathText => _pathText ??= PathFormat.ToJsonPath(Path);
}

/// <summary>Turns paths into text: normalized-style JSONPath and RFC 6901 JSON Pointer.</summary>
public static class PathFormat
{
    /// <summary>
    /// JSONPath for the node, e.g. <c>$.store.book[0]['odd key']</c>. Member names that are valid
    /// RFC 9535 shorthand names use dot notation; anything else uses a single-quoted bracket name.
    /// The output always parses back with <see cref="JsonPath.JsonPathQuery"/> to the same node.
    /// </summary>
    public static string ToJsonPath(IEnumerable<PathSegment> path)
    {
        var sb = new StringBuilder("$");
        foreach (var seg in path)
        {
            if (seg.IsIndex)
            {
                sb.Append('[').Append(seg.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
            }
            else if (IsShorthandName(seg.Name!))
            {
                sb.Append('.').Append(seg.Name);
            }
            else
            {
                sb.Append("['");
                AppendEscapedSingleQuoted(sb, seg.Name!);
                sb.Append("']");
            }
        }
        return sb.ToString();
    }

    /// <summary>RFC 6901 JSON Pointer, e.g. <c>/store/book/0</c>. The root is the empty string.</summary>
    public static string ToJsonPointer(IEnumerable<PathSegment> path)
    {
        var sb = new StringBuilder();
        foreach (var seg in path)
        {
            sb.Append('/');
            if (seg.IsIndex)
                sb.Append(seg.Index.ToString(CultureInfo.InvariantCulture));
            else
                sb.Append(seg.Name!.Replace("~", "~0").Replace("/", "~1"));
        }
        return sb.ToString();
    }

    /// <summary>RFC 9535 member-name-shorthand: name-first (ALPHA / "_" / non-ASCII) then name-chars (also DIGIT).</summary>
    public static bool IsShorthandName(string name)
    {
        if (name.Length == 0 || !IsNameFirst(name[0])) return false;
        for (int i = 1; i < name.Length; i++)
        {
            char c = name[i];
            if (!IsNameFirst(c) && !(c >= '0' && c <= '9')) return false;
        }
        return true;
    }

    internal static bool IsNameFirst(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || c >= '\u0080';

    private static void AppendEscapedSingleQuoted(StringBuilder sb, string s)
    {
        foreach (char c in s)
        {
            switch (c)
            {
                case '\'': sb.Append("\\'"); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
    }
}
