using System.Text.Json;
using System.Text.Json.Serialization;

namespace EOS.Server.Ui.Components;

public sealed class GridOption
{
    public object? Value { get; init; }
    public string Label { get; init; } = "";
    public string? Tone { get; init; }
}

public sealed class GridColumn
{
    public required string Key { get; init; }
    public string? Title { get; init; }
    public string Type { get; init; } = "text";
    public int? Width { get; init; }
    public bool? Editable { get; init; }
    public string? Pin { get; init; }
    public bool? Hidden { get; init; }
    public string? Band { get; init; }
    public string? Hint { get; init; }
    public string? Href { get; init; }
    public string? Agg { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public int? Decimals { get; init; }
    public bool? Nullable { get; init; }
    public bool? Percent { get; init; }
    public string? Prefix { get; init; }
    public string? Suffix { get; init; }
    public bool? RowLabel { get; init; }
    public bool? TreeColumn { get; init; }
    public bool? Quick { get; init; }
    public bool? Groupable { get; init; }
    public List<GridOption>? Options { get; init; }
}

public sealed class GridRow
{
    public required object Id { get; init; }
    public object? Parent { get; init; }
    public Dictionary<string, object?> Cells { get; init; } = [];
    public Dictionary<string, string>? Tones { get; init; }
    public bool? Readonly { get; init; }
    public List<string>? Locked { get; init; }
}

public sealed class GridOptions
{
    public string? Density { get; init; }
    public bool? RowNumbers { get; init; }
    public bool? ReadOnly { get; init; }
    public bool? Tree { get; init; }
    public bool? Groupable { get; init; }
    public bool? QuickRating { get; init; }
    public string? EmptyText { get; init; }
    public string? Hint { get; init; }
}

public sealed class GridEdit
{
    public JsonElement RowId { get; set; }
    public string Key { get; set; } = "";
    public JsonElement Value { get; set; }
    public JsonElement Old { get; set; }

    public int RowInt => RowId.ValueKind == JsonValueKind.Number ? RowId.GetInt32() : int.Parse(RowId.GetString() ?? "0");
    public bool ValueIsNull => Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
    public int? ValueInt => ValueIsNull ? null : Value.ValueKind == JsonValueKind.Number ? Value.GetInt32() : int.Parse(Value.GetString() ?? "0");
    public string? ValueText => ValueIsNull ? null : Value.ValueKind == JsonValueKind.String ? Value.GetString() : Value.ToString();
    public int? OldInt => Old.ValueKind == JsonValueKind.Number ? Old.GetInt32() : null;
}

public sealed class GridReject
{
    public object? RowId { get; init; }
    public string Key { get; init; } = "";
    public string Message { get; init; } = "";
    public object? Value { get; init; }
}

public sealed class GridUpdate
{
    public object? RowId { get; init; }
    public string Key { get; init; } = "";
    public object? Value { get; init; }
}

public sealed class GridCommitResult
{
    public List<GridReject> Rejected { get; } = [];
    public List<GridUpdate> Updated { get; } = [];

    public void Reject(GridEdit edit, string message, object? value = null) =>
        Rejected.Add(new GridReject { RowId = edit.RowId.Clone(), Key = edit.Key, Message = message, Value = value });

    public void Update(object rowId, string key, object? value) => Updated.Add(new GridUpdate { RowId = rowId, Key = key, Value = value });
}

public static class GridJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
