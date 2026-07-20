using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Clarian;

/// <summary>
/// Webhook signature helpers. Signature is hex(HMAC-SHA256(secret, timestamp + "." + body))
/// using the full whsec_… secret (prefix included).
/// </summary>
public static class ClarianWebhooks
{
    public const string HeaderSignature = "X-Clarian-Signature";
    public const string HeaderTimestamp = "X-Clarian-Timestamp";
    public const string HeaderEvent = "X-Clarian-Event";
    public const string HeaderDeliveryId = "X-Clarian-Delivery-Id";
    public const string HeaderIdempotencyKey = "X-Clarian-Idempotency-Key";
    public const string HeaderAttempt = "X-Clarian-Attempt";

    public const int DefaultToleranceSeconds = 300;

    public sealed record Headers(
        string Signature,
        string Timestamp,
        string Event,
        string DeliveryId,
        string IdempotencyKey,
        string Attempt);

    /// <summary>Sign timestamp + "." + payload with HMAC-SHA256 and return hex.</summary>
    public static string SignPayload(string secret, string timestamp, string payload)
    {
        secret ??= "";
        timestamp ??= "";
        payload ??= "";
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(timestamp + "." + payload);
        var hash = HMACSHA256.HashData(key, data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string SignPayload(string secret, string timestamp, byte[] payload)
    {
        var body = payload is null || payload.Length == 0
            ? ""
            : Encoding.UTF8.GetString(payload);
        return SignPayload(secret, timestamp, body);
    }

    /// <summary>
    /// Verify HMAC-SHA256 signature and timestamp freshness.
    /// Returns false on missing inputs, bad signature, or stale/future timestamp.
    /// </summary>
    public static bool VerifySignature(
        string payload,
        string timestamp,
        string signature,
        string secret,
        int toleranceSeconds = DefaultToleranceSeconds)
    {
        if (string.IsNullOrEmpty(secret)
            || string.IsNullOrEmpty(signature)
            || string.IsNullOrEmpty(timestamp))
        {
            return false;
        }

        if (!TryParseTimestampEpochSeconds(timestamp, out var eventEpoch))
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var age = Math.Abs(now - eventEpoch);
        if (age > toleranceSeconds)
            return false;

        var expected = SignPayload(secret, timestamp, payload ?? "");
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(signature);
        if (expectedBytes.Length != actualBytes.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public static bool VerifySignature(
        byte[] payload,
        string timestamp,
        string signature,
        string secret,
        int toleranceSeconds = DefaultToleranceSeconds)
    {
        var body = payload is null || payload.Length == 0
            ? ""
            : Encoding.UTF8.GetString(payload);
        return VerifySignature(body, timestamp, signature, secret, toleranceSeconds);
    }

    /// <summary>
    /// Read Clarian delivery headers (case-insensitive). Missing values become empty strings.
    /// </summary>
    public static Headers ExtractHeaders(IDictionary<string, string?> headers)
    {
        if (headers is null)
            return new Headers("", "", "", "", "", "");

        var lower = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in headers)
        {
            if (kv.Key is null)
                continue;
            lower[kv.Key] = kv.Value ?? "";
        }

        return new Headers(
            Get(lower, HeaderSignature),
            Get(lower, HeaderTimestamp),
            Get(lower, HeaderEvent),
            Get(lower, HeaderDeliveryId),
            Get(lower, HeaderIdempotencyKey),
            Get(lower, HeaderAttempt));
    }

    /// <summary>
    /// Read Clarian delivery headers from a multi-value header map (ASP.NET style).
    /// </summary>
    public static Headers ExtractHeaders(IDictionary<string, IEnumerable<string>?> headers)
    {
        if (headers is null)
            return new Headers("", "", "", "", "", "");

        var flat = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in headers)
        {
            if (kv.Key is null)
                continue;
            var first = kv.Value?.FirstOrDefault();
            flat[kv.Key] = first;
        }
        return ExtractHeaders(flat);
    }

    private static string Get(Dictionary<string, string> map, string name) =>
        map.TryGetValue(name, out var v) ? v : "";

    internal static bool TryParseTimestampEpochSeconds(string timestamp, out long epoch)
    {
        epoch = 0;
        if (DateTimeOffset.TryParse(
                timestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var dto))
        {
            epoch = dto.ToUnixTimeSeconds();
            return true;
        }

        if (double.TryParse(timestamp, NumberStyles.Float, CultureInfo.InvariantCulture, out var unix))
        {
            epoch = (long)unix;
            return true;
        }

        return false;
    }
}
