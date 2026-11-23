using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace JsonLens.Core;

/// <summary>Thrown when a file is not valid JSON. Carries a 1-based line and column when known.</summary>
public sealed class JsonLoadException : Exception
{
    public JsonLoadException(string message, long? line, long? column, Exception? inner = null)
        : base(message, inner)
    {
        Line = line;
        Column = column;
    }

    public long? Line { get; }
    public long? Column { get; }
}

/// <summary>Load progress: what is happening and how far along it is (0 to 1).</summary>
public readonly record struct LoadProgress(string Phase, double Fraction);

/// <summary>
/// A parsed, read-only JSON document. Wraps <see cref="JsonDocument"/>, which keeps the UTF-8 bytes
/// and a compact index rather than an object per node, so large files stay cheap.
/// </summary>
public sealed class JsonDoc : IDisposable
{
    public static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 1024,
    };

    private readonly JsonDocument _document;

    private JsonDoc(JsonDocument document, long byteSize, TimeSpan parseTime, string? filePath, CancellationToken ct)
    {
        _document = document;
        ByteSize = byteSize;
        ParseTime = parseTime;
        FilePath = filePath;
        var sw = Stopwatch.StartNew();
        NodeCount = ParallelWalk.CountNodes(document.RootElement, ct);
        CountTime = sw.Elapsed;
    }

    public JsonElement Root => _document.RootElement;
    public long ByteSize { get; }
    public string? FilePath { get; }

    /// <summary>Time spent reading the file (zero when parsing text or bytes directly).</summary>
    public TimeSpan ReadTime { get; private set; }
    public TimeSpan ParseTime { get; }
    public TimeSpan CountTime { get; }

    /// <summary>Read + parse + count: how long opening took.</summary>
    public TimeSpan LoadTime => ReadTime + ParseTime + CountTime;

    /// <summary>Total number of values in the document, including the root and every container.</summary>
    public long NodeCount { get; }

    public static JsonDoc Parse(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return Parse(bytes, null);
    }

    /// <summary>Time per load phase, in order, for diagnostics (--bench).</summary>
    public IReadOnlyList<(string Name, TimeSpan Time)> Phases =>
        new[] { ("read file", ReadTime), ("parse (JsonDocument)", ParseTime), ("count nodes", CountTime) };

    public static JsonDoc Load(string path) => Load(path, null);

    /// <summary>
    /// Reads and parses a file, reporting progress: "Reading" with the fraction of bytes read, then
    /// "Parsing" (a single step: System.Text.Json can't report progress inside a parse), then
    /// "Counting". Cancellation is checked between chunks and phases.
    /// </summary>
    public static JsonDoc Load(string path, IProgress<LoadProgress>? progress, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var bytes = ReadFile(path, progress, ct);
        var readTime = sw.Elapsed;
        ct.ThrowIfCancellationRequested();
        var doc = Parse(bytes, path, progress, ct);
        doc.ReadTime = readTime;
        return doc;
    }

    private const int ReadChunk = 8 * 1024 * 1024;

    private static byte[] ReadFile(string path, IProgress<LoadProgress>? progress, CancellationToken ct)
    {
        // FileShare.ReadWrite | Delete: never stop an editor from saving the file while we read it.
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1, FileOptions.SequentialScan);
        long length = fs.Length;
        if (length > Array.MaxLength)
            throw new JsonLoadException($"The file is {length / 1073741824.0:0.0} GB; JSON Lens can open files up to 2 GB.", null, null);

        // Uninitialized: every byte is overwritten by the read, so skip zeroing hundreds of MB.
        var bytes = GC.AllocateUninitializedArray<byte>((int)length);
        int read = 0;
        progress?.Report(new LoadProgress("Reading", 0));
        while (read < bytes.Length)
        {
            ct.ThrowIfCancellationRequested();
            int n = fs.Read(bytes, read, Math.Min(ReadChunk, bytes.Length - read));
            if (n == 0) break; // the file got shorter while we were reading it
            read += n;
            progress?.Report(new LoadProgress("Reading", (double)read / bytes.Length));
        }
        if (read < bytes.Length) Array.Resize(ref bytes, read);
        return bytes;
    }

    /// <summary>Parses raw file bytes. Handles UTF-8 (with or without BOM) and UTF-16 with a BOM.</summary>
    public static JsonDoc Parse(byte[] bytes, string? filePath) => Parse(bytes, filePath, null, default);

    private static JsonDoc Parse(byte[] bytes, string? filePath, IProgress<LoadProgress>? progress, CancellationToken ct)
    {
        ReadOnlyMemory<byte> utf8 = bytes;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            utf8 = utf8[3..];
        }
        else if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
        {
            var text = (bytes[0] == 0xFF ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(bytes, 2, bytes.Length - 2);
            utf8 = Encoding.UTF8.GetBytes(text);
        }

        var sw = Stopwatch.StartNew();
        JsonDocument document;
        try
        {
            progress?.Report(new LoadProgress("Parsing", 0));
            // Parse(ReadOnlyMemory) uses the buffer directly instead of copying it.
            document = JsonDocument.Parse(utf8, ParseOptions);
            sw.Stop();
        }
        catch (JsonException ex)
        {
            long? line = ex.LineNumber + 1;
            long? col = ex.BytePositionInLine + 1;
            var reason = ex.Message;
            int cut = reason.IndexOf(" LineNumber:", StringComparison.Ordinal);
            if (cut > 0) reason = reason[..cut].TrimEnd();
            throw new JsonLoadException($"Invalid JSON at line {line}, column {col}: {reason}", line, col, ex);
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new LoadProgress("Counting", 0));
            var doc = new JsonDoc(document, bytes.LongLength, sw.Elapsed, filePath, ct);
            progress?.Report(new LoadProgress("Counting", 1));
            return doc;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    public static long CountNodes(JsonElement element)
    {
        long count = 1;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject()) count += CountNodes(p.Value);
                break;
            case JsonValueKind.Array:
                foreach (var e in element.EnumerateArray()) count += CountNodes(e);
                break;
        }
        return count;
    }

    /// <summary>
    /// Follows a path from the root. Returns false if any step doesn't exist. A member segment with a
    /// <see cref="PathSegment.MemberIndex"/> selects that exact member (and its name must match);
    /// without one, a repeated name resolves to the last member with that name.
    /// </summary>
    public bool TryResolve(IReadOnlyList<PathSegment> path, out JsonElement element) =>
        TryResolve(Root, path, out element);

    public static bool TryResolve(JsonElement root, IReadOnlyList<PathSegment> path, out JsonElement element)
    {
        element = root;
        foreach (var seg in path)
        {
            if (seg.IsIndex)
            {
                if (element.ValueKind != JsonValueKind.Array || seg.Index >= element.GetArrayLength()) return false;
                element = element[seg.Index];
            }
            else if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            else if (seg.MemberIndex >= 0)
            {
                if (!TryGetMemberAt(element, seg.MemberIndex, seg.Name!, out element)) return false;
            }
            else
            {
                // TryGetProperty matches the last definition of a repeated name.
                if (!element.TryGetProperty(seg.Name!, out var child)) return false;
                element = child;
            }
        }
        return true;
    }

    private static bool TryGetMemberAt(JsonElement obj, int memberIndex, string name, out JsonElement value)
    {
        int i = 0;
        foreach (var prop in obj.EnumerateObject())
        {
            if (i++ == memberIndex)
            {
                value = prop.Value;
                return prop.NameEquals(name);
            }
        }
        value = default;
        return false;
    }

    public void Dispose() => _document.Dispose();
}
