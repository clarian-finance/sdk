using System.Net;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clarian;

public enum ClarianEnvironment { Live, Sandbox }

internal sealed record ClientConfig(
    string ApiKey, string WorkspaceId, string BaseUrl, TimeSpan Timeout,
    ClarianEnvironment Environment, string? SubaccountId, HttpClient Http, int MaxRetries);

internal static class Http
{
    private static readonly HashSet<int> RetryStatus = new() { 429, 502, 503, 504 };

    internal static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new BigIntegerConverter(), new NoFloat<double>(), new NoFloat<float>(), new NoFloat<decimal>() },
    };

    internal static ClarianEnvironment ResolveEnvironment(string apiKey) =>
        apiKey.StartsWith("cl_live_sk_", StringComparison.Ordinal) ? ClarianEnvironment.Live
        : apiKey.StartsWith("cl_test_sk_", StringComparison.Ordinal) ? ClarianEnvironment.Sandbox
        : throw new ArgumentException("Invalid API key format: must start with cl_live_sk_ or cl_test_sk_", nameof(apiKey));

    // Dots are encoded so "." and ".." never become path dot-segments.
    internal static string Seg(string id) =>
        string.IsNullOrEmpty(id) ? throw new ArgumentException("id is required", nameof(id)) : Uri.EscapeDataString(id).Replace(".", "%2E");

    /// <summary>Money-moving creates: an empty key throws before any network call.</summary>
    internal static string RequireKey(string idempotencyKey) =>
        string.IsNullOrEmpty(idempotencyKey)
            ? throw new ArgumentException("idempotencyKey is required: reuse the same key to retry safely", nameof(idempotencyKey))
            : idempotencyKey;

    internal static (string, object?)[] Q(params (string, object?)[] pairs) => pairs;

    private static string QueryString((string, object?)[]? query)
    {
        var parts = (query ?? Array.Empty<(string, object?)>())
            .Where(p => p.Item2 is string or int or long)
            .Select(p => $"{Uri.EscapeDataString(p.Item1)}={Uri.EscapeDataString(Convert.ToString(p.Item2, System.Globalization.CultureInfo.InvariantCulture)!)}")
            .ToList();
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }

    /// <summary>Calls <c>/v1/{live|test}/&lt;path&gt;</c> and returns the <c>data</c> member of the reply.</summary>
    internal static async Task<JsonElement> RequestAsync(
        ClientConfig c, HttpMethod method, string path,
        (string, object?)[]? query = null, object? body = null,
        string? idempotencyKey = null, string? otpCode = null, CancellationToken ct = default)
    {
        var env = c.Environment == ClarianEnvironment.Live ? "live" : "test";
        var url = $"{c.BaseUrl}/v1/{env}/{path}{QueryString(query)}";
        var json = body is null ? null : JsonSerializer.Serialize(body, body.GetType(), Json);
        // Only a request that cannot double-apply is retried: reads, deletes, or a create with an idempotency key.
        var retryable = method == HttpMethod.Get || method == HttpMethod.Delete || !string.IsNullOrEmpty(idempotencyKey);
        var retries = retryable ? c.MaxRetries : 0;

        for (var attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(method, url);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {c.ApiKey}");
            req.Headers.TryAddWithoutValidation("X-Workspace-Id", c.WorkspaceId);
            if (!string.IsNullOrEmpty(c.SubaccountId)) req.Headers.TryAddWithoutValidation("X-Subaccount-Id", c.SubaccountId);
            if (!string.IsNullOrEmpty(idempotencyKey)) req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            if (!string.IsNullOrEmpty(otpCode)) req.Headers.TryAddWithoutValidation("X-OTP-Code", otpCode);
            if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(c.Timeout);
            HttpResponseMessage? resp = null;
            Exception? error = null;
            try { resp = await c.Http.SendAsync(req, cts.Token).ConfigureAwait(false); }
            catch (HttpRequestException e) { error = e; }
            catch (OperationCanceledException e) when (!ct.IsCancellationRequested) { error = e; } // timeout

            var again = resp is null || RetryStatus.Contains((int)resp.StatusCode);
            if (!again || attempt >= retries)
            {
                if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
                using (resp) return await FinishAsync(resp!, ct).ConfigureAwait(false);
            }
            var delay = Backoff(attempt, resp?.Headers.RetryAfter?.Delta);
            resp?.Dispose();
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    private static TimeSpan Backoff(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } ra && ra >= TimeSpan.Zero) return ra > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : ra;
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * Math.Min(8000, 500 * Math.Pow(2, attempt)));
    }

    private static async Task<JsonElement> FinishAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(text == "" ? "{}" : text);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            var fallback = new { error = "invalid_json_response", message = text.Length > 500 ? text[..500] : text };
            root = JsonSerializer.SerializeToElement(fallback);
        }
        if (!resp.IsSuccessStatusCode) throw new ClarianException((int)resp.StatusCode, root);
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) ? data : root;
    }

    /// <summary>BigInteger amounts go on the wire as strings.</summary>
    private sealed class BigIntegerConverter : JsonConverter<BigInteger>
    {
        public override BigInteger Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => BigInteger.Parse(r.GetString()!);
        public override void Write(Utf8JsonWriter w, BigInteger v, JsonSerializerOptions o) => w.WriteStringValue(v.ToString());
    }

    /// <summary>Amounts are integer minor units: a float in a request body is a bug, refuse it.</summary>
    private sealed class NoFloat<T> : JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter w, T v, JsonSerializerOptions o) =>
            throw new ArgumentException($"{typeof(T).Name} is not allowed in a request body: amounts are integer minor units (string, int, long or BigInteger)");
    }
}
