using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JsonLens.App.ViewModels;
using JsonLens.Core.Tree;

namespace JsonLens.App.Views;

/// <summary>
/// The whole UI, title bar included. It lives in <see cref="MainWindow"/> normally, and is rendered
/// off-screen (with no window) by --check and --frames.
/// </summary>
public partial class MainView : UserControl
{
    private MainViewModel? _vm;
    private bool _syncingSelection;
    private double _savedOffset;

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // ItemCollection raises its event after the ListBox has processed the reset, so we can restore state.
        ((INotifyCollectionChanged)TreeList.Items).CollectionChanged += OnTreeItemsChanged;
        Drop += OnDrop;
        PreviewKeyDown += OnPreviewKeyDown;
        QueryBox.SizeChanged += (_, _) => PositionErrorPointer();
    }

    public MainViewModel? ViewModel => _vm;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.ScrollRequested -= OnScrollRequested;
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.Rows.Resetting -= OnRowsResetting;
        }
        _vm = e.NewValue as MainViewModel;
        if (_vm is not null)
        {
            _vm.ScrollRequested += OnScrollRequested;
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.Rows.Resetting += OnRowsResetting;
        }
    }

    // ================================================================ tree list sync

    private ScrollViewer? TreeScroll => _treeScroll ??= FindDescendant<ScrollViewer>(TreeList);
    private ScrollViewer? _treeScroll;

    private void OnRowsResetting(object? sender, EventArgs e) => _savedOffset = TreeScroll?.VerticalOffset ?? 0;

    private void OnTreeItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Reset) return;
        TreeScroll?.ScrollToVerticalOffset(_savedOffset);
        SyncSelectionToList();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.SelectedRow):
                SyncSelectionToList();
                break;
            case nameof(MainViewModel.QueryErrorPosition):
                PositionErrorPointer();
                break;
            case nameof(MainViewModel.QueryResults):
                FindDescendant<ScrollViewer>(ResultsList)?.ScrollToTop();
                break;
        }
    }

    private void SyncSelectionToList()
    {
        if (_vm is null) return;
        _syncingSelection = true;
        try
        {
            TreeList.SelectedItem = _vm.SelectedRow;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void TreeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || _vm is null) return;
        if (TreeList.SelectedItem is TreeRow row) _vm.SelectedRow = row;
    }

    /// <summary>Scrolls so the row is visible; rows that are off screen are brought to about a third from the top.</summary>
    private void OnScrollRequested(TreeRow row)
    {
        SyncSelectionToList();
        var sv = TreeScroll;
        if (_vm is null || sv is null) return;
        int index = _vm.Rows.IndexOf(row);
        if (index < 0) return;

        // Item-based scrolling: offsets and viewport are measured in rows.
        TreeList.UpdateLayout();
        double first = sv.VerticalOffset;
        double visible = Math.Max(1, sv.ViewportHeight);
        if (index >= first && index < first + visible - 1) return;
        sv.ScrollToVerticalOffset(Math.Max(0, index - Math.Floor(visible / 3)));
    }

    // ================================================================ mouse and keyboard in the tree

    private void Chevron_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1 || _vm is null) return;
        if (sender is FrameworkElement { DataContext: TreeRow row } && row.IsExpandable)
        {
            _vm.SelectRow(row, scroll: false);
            _vm.ToggleRow(row);
            TreeList.Focus();
            e.Handled = true;
        }
    }

    private void TreeList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || _vm is null) return;
        if (e.OriginalSource is DependencyObject d && FindAncestor<ListBoxItem>(d) is { DataContext: TreeRow row })
        {
            _vm.ToggleRow(row);
            e.Handled = true;
        }
    }

    private void TreeList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm?.SelectedRow is not { } row) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Right:
                if (row.IsExpandable && !row.IsExpanded) _vm.ExpandRow(row);
                else if (row.IsExpanded) MoveSelection(+1);
                e.Handled = true;
                break;
            case Key.Left:
                if (row.IsExpanded) _vm.CollapseRow(row);
                else if (row.Parent is not null) _vm.SelectRow(row.Parent, scroll: true);
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Space:
                _vm.ToggleRow(row);
                e.Handled = true;
                break;
            case Key.C when ctrl && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                _vm.CopyValueCommand.Execute(null);
                e.Handled = true;
                break;
        }
        if (e.Handled) FocusSelectedContainer();
    }

    private void MoveSelection(int delta)
    {
        if (_vm?.SelectedRow is null) return;
        int i = _vm.Rows.IndexOf(_vm.SelectedRow) + delta;
        if (i >= 0 && i < _vm.Rows.Count) _vm.SelectRow(_vm.Rows[i], scroll: true);
    }

    private void FocusSelectedContainer()
    {
        // Keep keyboard focus on the selected row after a reset, so arrow keys continue from it.
        Dispatcher.BeginInvoke(() =>
        {
            if (TreeList.ItemContainerGenerator.ContainerFromItem(TreeList.SelectedItem) is ListBoxItem item) item.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ================================================================ search and query boxes

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _vm.PrevMatch();
            else _vm.NextMatch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _vm.SearchText = "";
            e.Handled = true;
        }
    }

    private void QueryBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        if (e.Key == Key.Enter)
        {
            _ = _vm.RunQueryAsync();
            e.Handled = true;
        }
    }

    /// <summary>Moves the little red triangle under the character where the query went wrong.</summary>
    public void PositionErrorPointer()
    {
        if (_vm is null || _vm.QueryErrorPosition < 0) return;
        QueryBox.UpdateLayout();
        int pos = Math.Min(_vm.QueryErrorPosition, QueryBox.Text.Length);
        var rect = QueryBox.GetRectFromCharacterIndex(pos);
        if (rect.IsEmpty) return;
        var origin = QueryBox.TranslatePoint(new Point(rect.Left, 0), ErrorPointerCanvas);
        Canvas.SetLeft(ErrorPointer, Math.Max(0, origin.X - 5));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        if (e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.L)
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_vm is null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files && File.Exists(files[0]))
            _ = _vm.OpenFileAsync(files[0]);
    }

    // ================================================================ helpers (also used by the frame recorder)

    /// <summary>The on-screen container for a tree row, if it is currently realized.</summary>
    internal ListBoxItem? ContainerFor(TreeRow row) =>
        TreeList.ItemContainerGenerator.ContainerFromItem(row) as ListBoxItem;

    internal ListBoxItem? ContainerFor(ResultItem item) =>
        ResultsList.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;

    internal static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            var found = FindDescendant<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    internal static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        for (var cur = d; cur is not null; cur = cur is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
            if (cur is T t) return t;
        return null;
    }
}
