using System.Collections;
using System.Collections.Specialized;
using JsonLens.Core.Tree;

namespace JsonLens.App.ViewModels;

/// <summary>
/// Exposes the tree's visible rows to a virtualized ListBox without copying them. Changes are
/// reported as one Reset; the view keeps its scroll position and selection across it.
/// (ObservableCollection would raise one event per inserted row, which is far too slow for big expands.)
/// </summary>
public sealed class RowList : IList, IReadOnlyList<TreeRow>, INotifyCollectionChanged
{
    private static readonly IReadOnlyList<TreeRow> Empty = Array.Empty<TreeRow>();
    private IReadOnlyList<TreeRow> _rows = Empty;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>Raised just before a reset, so the view can remember its scroll offset.</summary>
    public event EventHandler? Resetting;

    public void SetSource(IReadOnlyList<TreeRow>? rows)
    {
        Resetting?.Invoke(this, EventArgs.Empty);
        _rows = rows ?? Empty;
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void Refresh() => SetSource(_rows);

    public int Count => _rows.Count;
    public TreeRow this[int index] => _rows[index];

    object? IList.this[int index]
    {
        get => _rows[index];
        set => throw new NotSupportedException();
    }

    public bool IsReadOnly => true;
    public bool IsFixedSize => false;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public int IndexOf(object? value)
    {
        if (value is not TreeRow row) return -1;
        if (_rows is List<TreeRow> list) return list.IndexOf(row);
        for (int i = 0; i < _rows.Count; i++) if (ReferenceEquals(_rows[i], row)) return i;
        return -1;
    }

    public bool Contains(object? value) => IndexOf(value) >= 0;

    public void CopyTo(Array array, int index)
    {
        for (int i = 0; i < _rows.Count; i++) array.SetValue(_rows[i], index + i);
    }

    public IEnumerator<TreeRow> GetEnumerator() => _rows.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _rows.GetEnumerator();

    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}
