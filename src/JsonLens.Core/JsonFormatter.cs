using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace JsonLens.Core;

/// <summary>
/// Pretty-prints and minifies JSON. Numbers are written exactly as they appear in the source
/// (no rounding, no reformatting), and non-ASCII text stays readable instead of becoming \u escapes
/// (except characters outside the Basic Multilingual Plane, such as emoji, which System.Text.Json
/// always writes as \u surrogate pairs; the output is still valid JSON with the same content).
/// </summary>
public static class JsonFormatter
{
    private static JsonWriterOptions Options(bool pretty) => new()
    {
        Indented = pretty,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        SkipValidation = true,
    };

    public static string Pretty(JsonElement element) => Format(element, pretty: true);

    public static string Minify(JsonElement element) => Format(element, pretty: false);

    public static string Format(JsonElement element, bool pretty)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, Options(pretty)))
        {
            element.WriteTo(writer);
        }
        return NormalizeNewlines(Encoding.UTF8.GetString(buffer.WrittenSpan), pretty);
    }

    /// <summary>Pretty-prints or minifies JSON text.</summary>
    public static string Format(string json, bool pretty)
    {
        using var doc = JsonDocument.Parse(json, JsonDoc.ParseOptions);
        return Format(doc.RootElement, pretty);
    }

    /// <summary>
    /// Formats at most about <paramref name="maxChars"/> characters, for previews of huge nodes.
    /// Stops writing early instead of formatting the whole value and cutting it afterwards.
    /// </summary>
    public static string Format(JsonElement element, bool pretty, int maxChars, out bool truncated)
    {
        var buffer = new LimitedBufferWriter(maxChars);
        truncated = false;
        var writer = new Utf8JsonWriter(buffer, Options(pretty));
        try
        {
            element.WriteTo(writer);
            writer.Dispose(); // flushes
        }
        catch (LimitReachedException)
        {
            // The writer's state is unusable now; it owns nothing that needs disposing when writing to a buffer.
            truncated = true;
        }

        var span = buffer.WrittenSpan;
        if (span.Length > maxChars)
        {
            span = span[..maxChars];
            truncated = true;
        }
        // Don't end in the middle of a UTF-8 sequence.
        int end = span.Length;
        if (truncated)
        {
            while (end > 0 && (span[end - 1] & 0xC0) == 0x80) end--;
            if (end > 0 && span[end - 1] >= 0xC0) end--;
        }
        return NormalizeNewlines(Encoding.UTF8.GetString(span[..end]), pretty);
    }

    /// <summary>Writes the whole element to a stream (used by Save As).</summary>
    public static void WriteTo(JsonElement element, Stream stream, bool pretty)
    {
        using var writer = new Utf8JsonWriter(stream, Options(pretty));
        element.WriteTo(writer);
    }

    private static string NormalizeNewlines(string text, bool pretty)
    {
        if (!pretty) return text;
        // Utf8JsonWriter uses Environment.NewLine; make output identical on every platform.
        return text.Contains('\r') ? text.Replace("\r\n", "\n") : text;
    }

    private sealed class LimitReachedException : Exception { }

    /// <summary>An IBufferWriter that refuses more space once more than the limit has been committed.</summary>
    private sealed class LimitedBufferWriter : IBufferWriter<byte>
    {
        private readonly int _limit;
        private readonly ArrayBufferWriter<byte> _inner = new();

        public LimitedBufferWriter(int limit) => _limit = limit;

        public ReadOnlySpan<byte> WrittenSpan => _inner.WrittenSpan;

        public void Advance(int count) => _inner.Advance(count);

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (_inner.WrittenCount > _limit) throw new LimitReachedException();
            return _inner.GetMemory(sizeHint);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            if (_inner.WrittenCount > _limit) throw new LimitReachedException();
            return _inner.GetSpan(sizeHint);
        }
    }
}
