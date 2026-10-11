using System.Text.Json;

namespace Clarian;

/// <summary>Every non-2xx reply. <see cref="Code"/> is the API's stable <c>error</c> string, e.g. <c>insufficient_balance</c>.</summary>
public sealed class ClarianException : Exception
{
    public int Status { get; }
    public string Code { get; }
    /// <summary>The reply's other fields (everything but <c>error</c> and <c>message</c>).</summary>
    public IReadOnlyDictionary<string, JsonElement> Meta { get; }

    public ClarianException(int status, JsonElement body) : base(Describe(body))
    {
        Status = status;
        Code = Str(body, "error") ?? "unknown_error";
        var meta = new Dictionary<string, JsonElement>();
        if (body.ValueKind == JsonValueKind.Object)
            foreach (var p in body.EnumerateObject())
                if (p.Name is not ("error" or "message")) meta[p.Name] = p.Value.Clone();
        Meta = meta;
    }

    private static string Describe(JsonElement body) => Str(body, "message") ?? Str(body, "error") ?? "unknown_error";

    private static string? Str(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
