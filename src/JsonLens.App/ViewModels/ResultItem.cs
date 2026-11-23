using System.Text.Json;
using JsonLens.Core;
using JsonLens.Core.Tree;

namespace JsonLens.App.ViewModels;

/// <summary>One row in the query results list.</summary>
public sealed class ResultItem
{
    public ResultItem(int number, JsonMatch match)
    {
        Number = number;
        Match = match;
    }

    public int Number { get; }
    public JsonMatch Match { get; }
    public string PathText => Match.PathText;

    public RowKind Kind => Match.Value.ValueKind switch
    {
        JsonValueKind.Object => RowKind.Object,
        JsonValueKind.Array => RowKind.Array,
        JsonValueKind.String => RowKind.String,
        JsonValueKind.Number => RowKind.Number,
        JsonValueKind.True or JsonValueKind.False => RowKind.Boolean,
        _ => RowKind.Null,
    };

    public string Preview => Describe(Match.Value);

    public static string Describe(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String: return TreeRow.Preview(value.GetString()!);
            case JsonValueKind.Number: return value.GetRawText();
            case JsonValueKind.True: return "true";
            case JsonValueKind.False: return "false";
            case JsonValueKind.Null: return "null";
            case JsonValueKind.Array:
                int n = value.GetArrayLength();
                return n == 1 ? "[…] 1 item" : $"[…] {n:N0} items";
            case JsonValueKind.Object:
                int k = 0;
                foreach (var _ in value.EnumerateObject()) k++;
                return k == 1 ? "{…} 1 key" : $"{{…}} {k:N0} keys";
            default: return "";
        }
    }
}
