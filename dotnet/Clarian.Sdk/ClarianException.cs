using System.Text.Json;

namespace Clarian;

/// <summary>
/// Thrown for any non-2xx HTTP response from the Clarian API.
/// Exception.Message truncates the raw body to 500 characters so logs stay safe.
/// Full body remains available via <see cref="Body"/>.
/// </summary>
public sealed class ClarianException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }
    public new string Message { get; }
    public IReadOnlyDictionary<string, object?> Meta { get; }
    public string Body { get; }

    public ClarianException(int statusCode, string? body)
        : base(BuildExceptionMessage(statusCode, body, out var code, out var message, out var meta, out var raw))
    {
        StatusCode = statusCode;
        Code = code;
        Message = message;
        Meta = meta;
        Body = raw;
    }

    private static string BuildExceptionMessage(
        int statusCode,
        string? body,
        out string code,
        out string message,
        out IReadOnlyDictionary<string, object?> meta,
        out string raw)
    {
        raw = body ?? "";
        code = "";
        var detail = "";
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (raw.Length > 0)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.NameEquals("error"))
                        {
                            code = prop.Value.ToString();
                            continue;
                        }
                        if (prop.NameEquals("detail"))
                        {
                            detail = prop.Value.ToString();
                            continue;
                        }
                        map[prop.Name] = JsonElementToObject(prop.Value);
                    }
                }
            }
            catch (JsonException)
            {
                // non-JSON body
            }
        }

        var truncated = raw.Length <= 500 ? raw : raw[..500];
        if (!string.IsNullOrEmpty(detail))
            message = detail;
        else if (!string.IsNullOrEmpty(code))
            message = code;
        else
            message = truncated;

        meta = map;

        if (!string.IsNullOrEmpty(code))
            return $"HTTP {statusCode}: {code}";
        if (!string.IsNullOrEmpty(truncated))
            return $"HTTP {statusCode}: {truncated}";
        return $"HTTP {statusCode}";
    }

    private static object? JsonElementToObject(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToList(),
            JsonValueKind.Object => el.EnumerateObject()
                .ToDictionary(p => p.Name, p => JsonElementToObject(p.Value), StringComparer.Ordinal),
            _ => el.ToString(),
        };
}
