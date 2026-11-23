using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using JsonLens.App.Services;
using JsonLens.Core;
using JsonLens.Core.JsonPath;
using JsonLens.Core.Tree;

namespace JsonLens.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>The details pane shows at most this much text; Copy value always copies everything.</summary>
    public const int DetailLimit = 200_000;
    public const int MaxQueryResults = 100_000;

    private readonly IAppServices _services;
    private JsonDoc? _doc;
    private FlatTree? _tree;

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _queryCts;
    private readonly DispatcherTimer? _searchDebounce;
    private readonly DispatcherTimer? _toastTimer;
    private readonly Stopwatch _toastClock = new();
    private double _toastSecondsLeft;

    private SearchResult _searchResult = SearchResult.Empty;
    private int _searchIndex = -1;

    /// <param name="synchronous">
    /// Run search and queries on the calling thread and drive animation only through <see cref="Tick"/>.
    /// Used by --check and --frames so every step is finished before the next one.
    /// </param>
    public MainViewModel(IAppServices services, bool synchronous = false)
    {
        _services = services;
        Synchronous = synchronous;

        if (!synchronous)
        {
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _searchDebounce.Tick += (_, _) =>
            {
                _searchDebounce.Stop();
                _ = RunSearchAsync();
            };
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _toastTimer.Tick += (_, _) =>
            {
                double dt = _toastClock.Elapsed.TotalSeconds;
                _toastClock.Restart();
                Tick(dt);
                if (ToastOpacity <= 0) _toastTimer.Stop();
            };
        }

        OpenCommand = new RelayCommand(Open, () => !IsLoading);
        OpenDemoCommand = new RelayCommand(() => _ = OpenDemoAsync(), () => !IsLoading);
        SaveAsCommand = new RelayCommand(SaveAs, () => HasDocument);
        ExpandAllCommand = new RelayCommand(ExpandAll, () => HasDocument);
        CollapseAllCommand = new RelayCommand(CollapseAll, () => HasDocument);
        CopyPathCommand = new RelayCommand(CopyPath, () => SelectedRow is not null);
        CopyPointerCommand = new RelayCommand(CopyPointer, () => SelectedRow is not null);
        CopyValueCommand = new RelayCommand(CopyValue, () => SelectedRow is not null);
        NextMatchCommand = new RelayCommand(NextMatch, () => _searchResult.Matches.Count > 0);
        PrevMatchCommand = new RelayCommand(PrevMatch, () => _searchResult.Matches.Count > 0);
        RunQueryCommand = new RelayCommand(() => _ = RunQueryAsync(), () => HasDocument);
        AboutCommand = new RelayCommand(_services.ShowAbout);
        ToggleRowCommand = new RelayCommand(p => { if (p is TreeRow r) ToggleRow(r); });
    }

    public bool Synchronous { get; }

    // ================================================================ commands

    public ICommand OpenCommand { get; }
    public ICommand OpenDemoCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand ExpandAllCommand { get; }
    public ICommand CollapseAllCommand { get; }
    public ICommand CopyPathCommand { get; }
    public ICommand CopyPointerCommand { get; }
    public ICommand CopyValueCommand { get; }
    public ICommand NextMatchCommand { get; }
    public ICommand PrevMatchCommand { get; }
    public ICommand RunQueryCommand { get; }
    public ICommand AboutCommand { get; }
    public ICommand ToggleRowCommand { get; }

    /// <summary>The view should select this row and scroll it into view.</summary>
    public event Action<TreeRow>? ScrollRequested;

    // ================================================================ document

    public RowList Rows { get; } = new();
    public FlatTree? Tree => _tree;
    public JsonDoc? Document => _doc;

    private bool _hasDocument;
    public bool HasDocument { get => _hasDocument; private set => Set(ref _hasDocument, value); }

    private string _fileName = "";
    public string FileName
    {
        get => _fileName;
        private set
        {
            if (Set(ref _fileName, value)) OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public string WindowTitle => string.IsNullOrEmpty(FileName) ? "JSON Lens" : $"{FileName} — JSON Lens";

    private string _docInfo = "";
    public string DocInfo { get => _docInfo; private set => Set(ref _docInfo, value); }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

    private string _loadingText = "";
    public string LoadingText { get => _loadingText; private set => Set(ref _loadingText, value); }

    private string? _loadError;
    public string? LoadError { get => _loadError; private set => Set(ref _loadError, value); }

    private void Open()
    {
        var path = _services.PickOpenFile();
        if (path is not null) _ = OpenFileAsync(path);
    }

    /// <summary>Opens a file; in synchronous mode it is finished when this returns.</summary>
    public async Task OpenFileAsync(string path)
    {
        if (IsLoading) return;
        string name = Path.GetFileName(path);
        IsLoading = true;
        LoadingText = $"Opening {name}…";
        try
        {
            var doc = Synchronous ? JsonDoc.Load(path) : await Task.Run(() => JsonDoc.Load(path));
            SetDocument(doc, name);
        }
        catch (Exception ex) when (ex is JsonLoadException or IOException or UnauthorizedAccessException)
        {
            var message = ex is JsonLoadException ? $"{name}: {ex.Message}" : $"Couldn't open {name}: {ex.Message}";
            if (HasDocument) ShowToast(message, 5);
            else LoadError = message;
        }
        finally
        {
            IsLoading = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Opens the generated large demo file, creating it first if it doesn't exist.</summary>
    public async Task OpenDemoAsync()
    {
        var path = AppPaths.DefaultDemoFile;
        if (!File.Exists(path))
        {
            IsLoading = true;
            LoadingText = "Generating the demo file (about 37 MB, once)…";
            try
            {
                if (Synchronous) DemoData.EnsureFile(path);
                else await Task.Run(() => DemoData.EnsureFile(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LoadError = $"Couldn't create the demo file: {ex.Message}";
                return;
            }
            finally
            {
                IsLoading = false;
            }
        }
        await OpenFileAsync(path);
    }

    /// <summary>Loads JSON text directly (used by --check with the built-in sample).</summary>
    public void LoadText(string json, string name) => SetDocument(JsonDoc.Parse(json), name);

    private void SetDocument(JsonDoc doc, string name)
    {
        _searchCts?.Cancel();
        _queryCts?.Cancel();
        var old = _doc;

        _doc = doc;
        if (_tree is not null) _tree.RowsChanged -= OnRowsChanged;
        _tree = new FlatTree(doc.Root);
        _tree.RowsChanged += OnRowsChanged;
        Rows.SetSource(_tree.Rows);

        FileName = name;
        DocInfo = $"{AppPaths.FormatBytes(doc.ByteSize)}  ·  {doc.NodeCount:N0} nodes  ·  parsed in {doc.ParseTime.TotalMilliseconds:N0} ms";
        LoadError = null;
        HasDocument = true;

        _searchResult = SearchResult.Empty;
        _searchIndex = -1;
        UpdateSearchStatus();
        QueryResults = Array.Empty<ResultItem>();
        QueryStatus = "";
        QueryError = null;

        SelectRow(_tree.Root, scroll: true);
        old?.Dispose();

        if (!string.IsNullOrEmpty(SearchText)) OnSearchInputChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnRowsChanged(object? sender, EventArgs e) => Rows.Refresh();

    // ================================================================ tree

    public void ToggleRow(TreeRow row)
    {
        if (_tree is null || !row.IsExpandable) return;
        _tree.Toggle(row);
    }

    public void ExpandRow(TreeRow row) => _tree?.Expand(row);
    public void CollapseRow(TreeRow row) => _tree?.Collapse(row);

    private void ExpandAll()
    {
        if (_tree is null) return;
        if (!_tree.ExpandAll()) ShowToast("Expanded the first 200,000 rows (the file is too big to expand fully)", 3.5);
        if (SelectedRow is not null) ScrollRequested?.Invoke(SelectedRow);
    }

    private void CollapseAll()
    {
        if (_tree is null) return;
        _tree.CollapseAll();
        SelectRow(_tree.Root, scroll: true);
    }

    /// <summary>Expands down to the node and selects it.</summary>
    public TreeRow? RevealPath(IReadOnlyList<PathSegment> path)
    {
        var row = _tree?.Reveal(path);
        if (row is not null) SelectRow(row, scroll: true);
        return row;
    }

    public void SelectRow(TreeRow? row, bool scroll)
    {
        SelectedRow = row;
        if (row is not null && scroll) ScrollRequested?.Invoke(row);
    }

    // ================================================================ selection and details

    private TreeRow? _selectedRow;
    public TreeRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (Set(ref _selectedRow, value)) UpdateDetails();
        }
    }

    private string _selectedPath = "";
    public string SelectedPath { get => _selectedPath; private set => Set(ref _selectedPath, value); }

    private string _selectedPointer = "";
    public string SelectedPointer { get => _selectedPointer; private set => Set(ref _selectedPointer, value); }

    private string _selectedInfo = "";
    public string SelectedInfo { get => _selectedInfo; private set => Set(ref _selectedInfo, value); }

    private string _detailText = "";
    public string DetailText { get => _detailText; private set => Set(ref _detailText, value); }

    private bool _detailTruncated;
    public bool DetailTruncated { get => _detailTruncated; private set => Set(ref _detailTruncated, value); }

    private bool _isMinified;
    public bool IsMinified
    {
        get => _isMinified;
        set
        {
            if (Set(ref _isMinified, value)) UpdateDetails();
        }
    }

    private void UpdateDetails()
    {
        var row = SelectedRow?.ValueRow;
        if (row is null)
        {
            SelectedPath = SelectedPointer = SelectedInfo = DetailText = "";
            DetailTruncated = false;
            return;
        }

        var path = row.Path;
        SelectedPath = PathFormat.ToJsonPath(path);
        SelectedPointer = PathFormat.ToJsonPointer(path);
        SelectedInfo = row.Kind switch
        {
            RowKind.Object => $"object  ·  {row.Summary}",
            RowKind.Array => $"array  ·  {row.Summary}",
            RowKind.String => $"string  ·  {row.Element.GetString()!.Length:N0} chars",
            RowKind.Number => "number",
            RowKind.Boolean => "boolean",
            _ => "null",
        };
        DetailText = JsonFormatter.Format(row.Element, pretty: !IsMinified, DetailLimit, out bool truncated);
        DetailTruncated = truncated;
    }

    // ================================================================ copy and save

    private void CopyPath() => CopyText(SelectedPath, $"Copied path   {SelectedPath}");

    private void CopyPointer() => CopyText(SelectedPointer, $"Copied JSON Pointer   {(SelectedPointer.Length == 0 ? "(root)" : SelectedPointer)}");

    private void CopyValue()
    {
        var row = SelectedRow?.ValueRow;
        if (row is null) return;
        // Strings are copied as their text; everything else as JSON in the current format.
        string text = row.Kind == RowKind.String
            ? row.Element.GetString()!
            : JsonFormatter.Format(row.Element, pretty: !IsMinified);
        CopyText(text, $"Copied value ({AppPaths.FormatBytes(System.Text.Encoding.UTF8.GetByteCount(text))}, {(row.Kind == RowKind.String ? "text" : IsMinified ? "minified" : "pretty")})");
    }

    private void CopyText(string text, string toast)
    {
        if (SelectedRow is null) return;
        if (_services.TrySetClipboard(text)) ShowToast(toast);
        else ShowToast("The clipboard is busy; try again", 3);
    }

    private void SaveAs()
    {
        if (_doc is null) return;
        var baseName = Path.GetFileNameWithoutExtension(FileName);
        var path = _services.PickSaveFile(baseName + (IsMinified ? ".min.json" : ".pretty.json"));
        if (path is null) return;
        try
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                JsonFormatter.WriteTo(_doc.Root, fs, pretty: !IsMinified);
            }
            ShowToast($"Saved {(IsMinified ? "minified" : "pretty")} JSON to {Path.GetFileName(path)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast($"Couldn't save: {ex.Message}", 5);
        }
    }

    // ================================================================ search

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? "")) OnSearchInputChanged();
        }
    }

    private bool _matchCase;
    public bool MatchCase
    {
        get => _matchCase;
        set
        {
            if (Set(ref _matchCase, value)) OnSearchInputChanged();
        }
    }

    private string _searchStatus = "";
    public string SearchStatus { get => _searchStatus; private set => Set(ref _searchStatus, value); }

    private bool _searchFoundNothing;
    public bool SearchFoundNothing { get => _searchFoundNothing; private set => Set(ref _searchFoundNothing, value); }

    public SearchResult SearchResult => _searchResult;
    public int SearchIndex => _searchIndex;

    private void OnSearchInputChanged()
    {
        _searchCts?.Cancel();
        if (Synchronous)
        {
            ApplySearch(_doc is null || SearchText.Length == 0
                ? SearchResult.Empty
                : JsonSearcher.Search(_doc.Root, SearchText, new SearchOptions { MatchCase = MatchCase }));
            return;
        }
        _searchDebounce!.Stop();
        if (SearchText.Length == 0)
        {
            ApplySearch(SearchResult.Empty);
            return;
        }
        _searchDebounce.Start();
    }

    private async Task RunSearchAsync()
    {
        if (_doc is null) return;
        var term = SearchText;
        if (term.Length == 0)
        {
            ApplySearch(SearchResult.Empty);
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var root = _doc.Root;
        var options = new SearchOptions { MatchCase = MatchCase };
        SearchStatus = "Searching…";
        try
        {
            var result = await Task.Run(() => JsonSearcher.Search(root, term, options, cts.Token));
            if (!cts.IsCancellationRequested) ApplySearch(result);
        }
        catch (Exception) when (cts.IsCancellationRequested)
        {
            // Superseded by newer input or a new document.
        }
    }

    private void ApplySearch(SearchResult result)
    {
        _searchResult = result;
        _searchIndex = -1;
        if (result.Matches.Count > 0) GoToMatch(0);
        else UpdateSearchStatus();
        CommandManager.InvalidateRequerySuggested();
    }

    public void NextMatch()
    {
        int n = _searchResult.Matches.Count;
        if (n > 0) GoToMatch((_searchIndex + 1) % n);
    }

    public void PrevMatch()
    {
        int n = _searchResult.Matches.Count;
        if (n > 0) GoToMatch((_searchIndex - 1 + n) % n);
    }

    private void GoToMatch(int index)
    {
        _searchIndex = index;
        RevealPath(_searchResult.Matches[index].Path);
        UpdateSearchStatus();
    }

    private void UpdateSearchStatus()
    {
        int n = _searchResult.Matches.Count;
        SearchFoundNothing = SearchText.Length > 0 && n == 0 && HasDocument;
        if (SearchText.Length == 0 || !HasDocument) SearchStatus = "";
        else if (n == 0) SearchStatus = "No results";
        else SearchStatus = $"{_searchIndex + 1:N0} of {n:N0}{(_searchResult.Truncated ? "+" : "")}";
    }

    // ================================================================ JSONPath query

    private string _queryText = "";
    public string QueryText
    {
        get => _queryText;
        set
        {
            if (Set(ref _queryText, value ?? "") && QueryError is not null) QueryError = null;
        }
    }

    private string? _queryError;
    public string? QueryError
    {
        get => _queryError;
        private set
        {
            if (Set(ref _queryError, value))
            {
                if (value is null) QueryErrorPosition = -1;
                OnPropertyChanged(nameof(HasQueryError));
            }
        }
    }

    public bool HasQueryError => QueryError is not null;

    private int _queryErrorPosition = -1;
    /// <summary>0-based character position of the query error, or -1.</summary>
    public int QueryErrorPosition { get => _queryErrorPosition; private set => Set(ref _queryErrorPosition, value); }

    private string _queryStatus = "";
    public string QueryStatus { get => _queryStatus; private set => Set(ref _queryStatus, value); }

    private IReadOnlyList<ResultItem> _queryResults = Array.Empty<ResultItem>();
    public IReadOnlyList<ResultItem> QueryResults
    {
        get => _queryResults;
        private set
        {
            if (Set(ref _queryResults, value)) OnPropertyChanged(nameof(HasQueryResults));
        }
    }

    public bool HasQueryResults => _queryResults.Count > 0;

    private ResultItem? _selectedResult;
    public ResultItem? SelectedResult
    {
        get => _selectedResult;
        set
        {
            if (Set(ref _selectedResult, value) && value is not null) RevealPath(value.Match.Path);
        }
    }

    public async Task RunQueryAsync()
    {
        if (_doc is null) return;
        _queryCts?.Cancel();
        var text = QueryText.Trim();
        if (text.Length == 0)
        {
            QueryResults = Array.Empty<ResultItem>();
            QueryStatus = "";
            QueryError = null;
            return;
        }

        if (!JsonPathQuery.TryParse(text, out var query, out var error))
        {
            // Point at the character in the text box (the text was trimmed at the start).
            int lead = QueryText.Length - QueryText.TrimStart().Length;
            QueryError = error!.Message;
            QueryErrorPosition = error.Position + lead;
            QueryStatus = "";
            return;
        }

        QueryError = null;
        var cts = new CancellationTokenSource();
        _queryCts = cts;
        var root = _doc.Root;
        QueryStatus = "Running…";
        var sw = Stopwatch.StartNew();
        try
        {
            var result = Synchronous
                ? query!.Evaluate(root, MaxQueryResults)
                : await Task.Run(() => query!.Evaluate(root, MaxQueryResults, cts.Token));
            if (cts.IsCancellationRequested) return;
            sw.Stop();

            var items = new ResultItem[result.Matches.Count];
            for (int i = 0; i < items.Length; i++) items[i] = new ResultItem(i + 1, result.Matches[i]);
            _selectedResult = null;
            OnPropertyChanged(nameof(SelectedResult));
            QueryResults = items;

            string count = items.Length == 1 ? "1 result" : $"{items.Length:N0} results";
            QueryStatus = result.Truncated
                ? $"First {items.Length:N0} results  ·  {sw.ElapsedMilliseconds:N0} ms"
                : $"{count}  ·  {sw.ElapsedMilliseconds:N0} ms";
        }
        catch (Exception) when (cts.IsCancellationRequested)
        {
        }
    }

    // ================================================================ toast

    private string _toastText = "";
    public string ToastText { get => _toastText; private set => Set(ref _toastText, value); }

    private double _toastOpacity;
    public double ToastOpacity { get => _toastOpacity; private set => Set(ref _toastOpacity, value); }

    public void ShowToast(string text, double seconds = 2.2)
    {
        ToastText = text;
        ToastOpacity = 1;
        _toastSecondsLeft = seconds;
        if (_toastTimer is not null)
        {
            _toastClock.Restart();
            _toastTimer.Start();
        }
    }

    /// <summary>Advances time-based UI (the toast fade). Called by a timer, or once per frame by --frames.</summary>
    public void Tick(double seconds)
    {
        if (ToastOpacity <= 0) return;
        _toastSecondsLeft -= seconds;
        if (_toastSecondsLeft <= 0) ToastOpacity = Math.Max(0, ToastOpacity - seconds / 0.35);
    }

    public void Dispose()
    {
        _searchCts?.Cancel();
        _queryCts?.Cancel();
        _searchDebounce?.Stop();
        _toastTimer?.Stop();
        _doc?.Dispose();
    }
}
