using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clarian;

/// <summary>Body of a delivery: <c>{id, type, created_at, data}</c>. Types: payment.completed, payment.failed, test.ping.</summary>
public sealed record WebhookEvent(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("data")] JsonElement Data);

public static class ClarianWebhooks
{
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromSeconds(300);

    /// <summary>Signs <c>{timestamp}.{body}</c> exactly as Clarian does (lowercase hex HMAC-SHA256). For tests of your own handler.</summary>
    public static string Sign(string body, string timestamp, string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();

    public static bool VerifySignature(string body, string signature, string timestamp, string secret, TimeSpan? tolerance = null)
    {
        // An unparseable timestamp fails closed.
        if (!DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var sentAt)) return false;
        if ((DateTimeOffset.UtcNow - sentAt).Duration() > (tolerance ?? DefaultTolerance)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Sign(body, timestamp, secret)), Encoding.UTF8.GetBytes(signature ?? ""));
    }

    /// <summary>
    /// Verifies a delivery and returns its parsed body. <paramref name="rawBody"/> must be the exact bytes received.
    /// Throws when the signature or timestamp is bad. Dedupe on <c>Id</c>: a delivery can arrive more than once.
    /// </summary>
    public static WebhookEvent ConstructEvent(string rawBody, string signature, string timestamp, string secret, TimeSpan? tolerance = null)
    {
        if (!VerifySignature(rawBody, signature, timestamp, secret, tolerance))
            throw new InvalidOperationException("Invalid webhook signature or timestamp outside tolerance");
        return JsonSerializer.Deserialize<WebhookEvent>(rawBody) ?? throw new InvalidOperationException("Empty webhook body");
    }

    /// <summary>Same, reading <c>x-clarian-signature</c> / <c>x-clarian-timestamp</c> from any header collection (case-insensitive).</summary>
    public static WebhookEvent ConstructEvent(string rawBody, IEnumerable<KeyValuePair<string, string>> headers, string secret, TimeSpan? tolerance = null)
    {
        string Get(string name) => headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value ?? "";
        return ConstructEvent(rawBody, Get("x-clarian-signature"), Get("x-clarian-timestamp"), secret, tolerance);
    }
}
