using System.Text.Json;

namespace JsonLens.Core.Tree;

/// <summary>
/// The tree as a flat list of the rows that are currently visible, in display order.
/// Expanding a row inserts its visible descendants after it; collapsing removes them.
/// Containers with more than <see cref="GroupSize"/> children are split into group rows
/// (<c>[0 … 99]</c>, or <c>[0 … 9999]</c> holding further groups for very big containers),
/// so no single expand ever adds more than about <see cref="GroupSize"/> rows.
/// </summary>
public sealed class FlatTree
{
    public const int GroupSize = 100;

    private readonly List<TreeRow> _rows = new();
    private int _suspend;
    private bool _dirty;

    public FlatTree(JsonElement root, bool expandRoot = true)
    {
        Root = TreeRow.CreateRoot(root);
        _rows.Add(Root);
        if (expandRoot) Expand(Root);
    }

    public TreeRow Root { get; }

    /// <summary>Visible rows in display order.</summary>
    public IReadOnlyList<TreeRow> Rows => _rows;

    /// <summary>Raised after the visible rows change (treat as a full reset).</summary>
    public event EventHandler? RowsChanged;

    public int IndexOf(TreeRow row) => _rows.IndexOf(row);

    public bool Expand(TreeRow row)
    {
        if (!row.IsExpandable || row.IsExpanded) return false;
        int idx = _rows.IndexOf(row);
        row.IsExpanded = true;
        if (idx >= 0)
        {
            var added = new List<TreeRow>();
            AppendVisibleChildren(row, added);
            _rows.InsertRange(idx + 1, added);
            Changed();
        }
        return true;
    }

    public bool Collapse(TreeRow row)
    {
        if (!row.IsExpanded) return false;
        int idx = _rows.IndexOf(row);
        row.IsExpanded = false;
        if (idx >= 0)
        {
            int end = idx + 1;
            while (end < _rows.Count && _rows[end].Depth > row.Depth) end++;
            _rows.RemoveRange(idx + 1, end - idx - 1);
            Changed();
        }
        return true;
    }

    public void Toggle(TreeRow row)
    {
        if (row.IsExpanded) Collapse(row);
        else Expand(row);
    }

    /// <summary>
    /// Expands breadth-first from the root until about <paramref name="maxVisibleRows"/> rows are visible,
    /// so "expand all" on a huge file stays usable. Returns true if everything was expanded.
    /// </summary>
    public bool ExpandAll(int maxVisibleRows = 200_000)
    {
        // Start from a clean state so rows expanded earlier can't push us over the budget.
        CollapseDown(Root);
        int visible = 1;
        bool complete = true;
        var queue = new Queue<TreeRow>();
        queue.Enqueue(Root);
        while (queue.Count > 0)
        {
            var r = queue.Dequeue();
            if (!r.IsExpandable) continue;
            var children = GetChildren(r);
            if (visible + children.Count > maxVisibleRows)
            {
                complete = false;
                break;
            }
            r.IsExpanded = true;
            visible += children.Count;
            foreach (var c in children) queue.Enqueue(c);
        }
        Rebuild();
        return complete;
    }

    /// <summary>Collapses everything except the root.</summary>
    public void CollapseAll()
    {
        CollapseDown(Root);
        Root.IsExpanded = true;
        Rebuild();
    }

    private static void CollapseDown(TreeRow r)
    {
        r.IsExpanded = false;
        if (r.Children is null) return;
        foreach (var c in r.Children) CollapseDown(c);
    }

    /// <summary>
    /// Expands every row on the way to the node at <paramref name="path"/> (including group rows)
    /// and returns its row, or null if the path doesn't exist.
    /// </summary>
    public TreeRow? Reveal(IReadOnlyList<PathSegment> path)
    {
        _suspend++;
        try
        {
            if (!Root.IsExpanded && path.Count > 0) Expand(Root);
            var row = Root;
            foreach (var seg in path)
            {
                int childIndex = FindChildIndex(row, seg);
                if (childIndex < 0) return null;

                var cur = row;
                while (true)
                {
                    Expand(cur);
                    var children = GetChildren(cur);
                    if (children.Count > 0 && children[0].IsGroup)
                    {
                        var group = children.FirstOrDefault(g => g.GroupStart <= childIndex && childIndex < g.GroupEnd);
                        if (group is null) return null;
                        cur = group;
                        continue;
                    }
                    int offset = cur.IsGroup ? cur.GroupStart : 0;
                    row = children[childIndex - offset];
                    break;
                }
            }
            return row;
        }
        finally
        {
            Resume();
        }
    }

    private int FindChildIndex(TreeRow row, PathSegment seg)
    {
        if (seg.IsIndex)
            return row.Kind == RowKind.Array && seg.Index < row.ChildCount ? seg.Index : -1;
        if (row.Kind != RowKind.Object) return -1;
        var entries = GetEntries(row);
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].Name == seg.Name) return i;
        return -1;
    }

    /// <summary>Child rows of a value row or group row, created on first use.</summary>
    internal List<TreeRow> GetChildren(TreeRow row)
    {
        if (row.Children is not null) return row.Children;

        var owner = row.ValueRow;
        int start = row.IsGroup ? row.GroupStart : 0;
        int end = row.IsGroup ? row.GroupEnd : row.ChildCount;
        int depth = row.Depth + 1;
        int len = end - start;
        var list = new List<TreeRow>(Math.Min(len, GroupSize));

        if (len > GroupSize)
        {
            long bucket = GroupSize;
            while ((len + bucket - 1) / bucket > GroupSize) bucket *= GroupSize;
            for (long s = start; s < end; s += bucket)
                list.Add(TreeRow.CreateGroup(row, owner, depth, (int)s, (int)Math.Min(end, s + bucket)));
        }
        else if (len > 0)
        {
            var entries = GetEntries(owner);
            for (int i = start; i < end; i++)
                list.Add(TreeRow.CreateValue(row, depth, entries[i]));
        }

        row.Children = list;
        return list;
    }

    private static ChildEntry[] GetEntries(TreeRow container)
    {
        if (container.Entries is not null) return container.Entries;
        var entries = new ChildEntry[container.ChildCount];
        int i = 0;
        if (container.Kind == RowKind.Object)
        {
            foreach (var p in container.Element.EnumerateObject()) entries[i] = new ChildEntry(p.Name, i++, p.Value);
        }
        else if (container.Kind == RowKind.Array)
        {
            foreach (var e in container.Element.EnumerateArray()) entries[i] = new ChildEntry(null, i++, e);
        }
        container.Entries = entries;
        return entries;
    }

    private void AppendVisibleChildren(TreeRow row, List<TreeRow> output)
    {
        foreach (var child in GetChildren(row))
        {
            output.Add(child);
            if (child.IsExpanded) AppendVisibleChildren(child, output);
        }
    }

    private void Rebuild()
    {
        _rows.Clear();
        _rows.Add(Root);
        if (Root.IsExpanded) AppendVisibleChildren(Root, _rows);
        Changed();
    }

    private void Changed()
    {
        if (_suspend > 0)
        {
            _dirty = true;
            return;
        }
        RowsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Resume()
    {
        if (--_suspend == 0 && _dirty)
        {
            _dirty = false;
            RowsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
