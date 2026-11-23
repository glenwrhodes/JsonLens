using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace JsonLens.Core.Tree;

public enum RowKind { Object, Array, String, Number, Boolean, Null, Group }

/// <summary>
/// One row of the tree. A row is either a JSON value (with its key or index), or a group row
/// such as <c>[0 … 99]</c> that stands for a range of a big container's children.
/// Rows are created lazily, only when their parent is expanded.
/// </summary>
public sealed class TreeRow : INotifyPropertyChanged
{
    public const int MaxPreviewChars = 160;

    private bool _isExpanded;

    // For value rows.
    private TreeRow(TreeRow? parent, int depth, string? name, int index, JsonElement element)
    {
        Parent = parent;
        Depth = depth;
        Name = name;
        Index = index;
        Element = element;
        Kind = element.ValueKind switch
        {
            JsonValueKind.Object => RowKind.Object,
            JsonValueKind.Array => RowKind.Array,
            JsonValueKind.String => RowKind.String,
            JsonValueKind.Number => RowKind.Number,
            JsonValueKind.True or JsonValueKind.False => RowKind.Boolean,
            _ => RowKind.Null,
        };
        if (Kind == RowKind.Array) ChildCount = element.GetArrayLength();
        else if (Kind == RowKind.Object) ChildCount = CountMembers(element);
    }

    // For group rows.
    private TreeRow(TreeRow parent, TreeRow owner, int depth, int start, int end)
    {
        Parent = parent;
        Owner = owner;
        Depth = depth;
        Index = -1;
        Kind = RowKind.Group;
        GroupStart = start;
        GroupEnd = end;
        ChildCount = end - start;
        Element = owner.Element;
    }

    internal static TreeRow CreateRoot(JsonElement root) => new(null, 0, null, -1, root);

    /// <summary>The row above this one in the tree (a value row or a group row). Null for the root.</summary>
    public TreeRow? Parent { get; }

    /// <summary>For a group row: the container row whose children it groups.</summary>
    public TreeRow? Owner { get; }

    public int Depth { get; }

    /// <summary>Member name when this value is inside an object.</summary>
    public string? Name { get; }

    /// <summary>Array index when this value is inside an array, otherwise -1.</summary>
    public int Index { get; }

    public JsonElement Element { get; }
    public RowKind Kind { get; }

    /// <summary>Children of an object/array, or how many children a group covers.</summary>
    public int ChildCount { get; }

    public int GroupStart { get; }
    public int GroupEnd { get; }

    public bool IsGroup => Kind == RowKind.Group;
    public bool IsRoot => Parent is null;
    public bool IsExpandable => ChildCount > 0;

    public bool IsExpanded
    {
        get => _isExpanded;
        internal set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Child rows, created the first time the row is expanded.</summary>
    internal List<TreeRow>? Children { get; set; }

    /// <summary>The children of a container, cached when needed (index lookups into big arrays).</summary>
    internal ChildEntry[]? Entries { get; set; }

    // ------------------------------------------------------------ display

    /// <summary>Key column: member name, [index], $ for the root, or the range for a group.</summary>
    public string KeyText
    {
        get
        {
            if (IsGroup) return $"[{GroupStart} … {GroupEnd - 1}]";
            if (IsRoot) return "$";
            if (Name is not null) return Name;
            return "[" + Index.ToString(CultureInfo.InvariantCulture) + "]";
        }
    }

    public bool HasName => Name is not null;

    /// <summary>Short value text: scalars in JSON form, containers as a summary.</summary>
    public string ValueText => Kind switch
    {
        RowKind.String => Preview(Element.GetString()!),
        RowKind.Number => Element.GetRawText(),
        RowKind.Boolean => Element.ValueKind == JsonValueKind.True ? "true" : "false",
        RowKind.Null => "null",
        RowKind.Object => ChildCount == 0 ? "{}" : "{…}",
        RowKind.Array => ChildCount == 0 ? "[]" : "[…]",
        _ => "",
    };

    /// <summary>Grey note after containers: "3 keys", "120 items".</summary>
    public string Summary => Kind switch
    {
        RowKind.Object => ChildCount == 1 ? "1 key" : $"{ChildCount:N0} keys",
        RowKind.Array => ChildCount == 1 ? "1 item" : $"{ChildCount:N0} items",
        RowKind.Group => $"{ChildCount:N0} items",
        _ => "",
    };

    /// <summary>The path to this row's value. A group row returns its container's path.</summary>
    public IReadOnlyList<PathSegment> Path
    {
        get
        {
            var segs = new List<PathSegment>();
            for (var r = this; r is not null; r = r.Parent)
            {
                if (r.IsGroup || r.IsRoot) continue;
                segs.Add(r.Name is not null ? PathSegment.Member(r.Name) : PathSegment.At(r.Index));
            }
            segs.Reverse();
            return segs;
        }
    }

    public string PathText => PathFormat.ToJsonPath(Path);

    /// <summary>The value row this row belongs to (itself, or the container for a group row).</summary>
    public TreeRow ValueRow => Owner ?? this;

    /// <summary>A string as a short, single-line, quoted JSON-style preview.</summary>
    public static string Preview(string s)
    {
        var sb = new StringBuilder(Math.Min(s.Length, MaxPreviewChars) + 4);
        sb.Append('"');
        foreach (char c in s)
        {
            if (sb.Length >= MaxPreviewChars) { sb.Append('…'); break; }
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static int CountMembers(JsonElement obj)
    {
        int n = 0;
        foreach (var _ in obj.EnumerateObject()) n++;
        return n;
    }

    internal static TreeRow CreateValue(TreeRow parent, int depth, ChildEntry entry) =>
        new(parent, depth, entry.Name, entry.Name is null ? entry.Index : -1, entry.Value);

    internal static TreeRow CreateGroup(TreeRow parent, TreeRow owner, int depth, int start, int end) =>
        new(parent, owner, depth, start, end);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public override string ToString() => $"{KeyText}: {ValueText}";
}

internal readonly record struct ChildEntry(string? Name, int Index, JsonElement Value);
