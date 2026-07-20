using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clarian;

/// <summary>
/// Clarian Finance API client.
/// Environment is inferred from the API key prefix:
/// cl_test_sk_ → sandbox (BaseUrlTest), otherwise live (BaseUrlLive).
/// </summary>
public sealed class ClarianClient : IDisposable
{
    public const string BaseUrlLive = "https://api.clarian.finance/functions/v1/api-gateway/live";
    public const string BaseUrlTest = "https://api.clarian.finance/functions/v1/api-gateway/test";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
    };

    private readonly HttpClient _http;
    private readonly bool _disposeHttp;
    private readonly string _apiKey;
    private readonly string _workspaceId;
    private readonly string _baseUrl;

    public string ApiKey => _apiKey;
    public string WorkspaceId => _workspaceId;
    public string BaseUrl => _baseUrl;

    public CashInService CashIn { get; }
    public CashOutService CashOut { get; }
    public BalancesService Balances { get; }
    public TransactionsService Transactions { get; }
    public WalletsService Wallets { get; }
    public WebhooksService Webhooks { get; }
    public SandboxService Sandbox { get; }

    public ClarianClient(
        string apiKey,
        string workspaceId,
        string? baseUrl = null,
        TimeSpan? timeout = null,
        HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new ArgumentException("apiKey is required", nameof(apiKey));
        if (string.IsNullOrEmpty(workspaceId))
            throw new ArgumentException("workspaceId is required", nameof(workspaceId));

        _apiKey = apiKey;
        _workspaceId = workspaceId;

        var inferred = apiKey.StartsWith("cl_test_sk_", StringComparison.Ordinal)
            ? BaseUrlTest
            : BaseUrlLive;
        var baseResolved = (baseUrl ?? inferred).TrimEnd('/');
        _baseUrl = baseResolved;

        if (handler is not null)
        {
            _http = new HttpClient(handler, disposeHandler: false)
            {
                Timeout = timeout ?? DefaultTimeout,
            };
            _disposeHttp = true;
        }
        else
        {
            _http = new HttpClient
            {
                Timeout = timeout ?? DefaultTimeout,
            };
            _disposeHttp = true;
        }

        CashIn = new CashInService(this);
        CashOut = new CashOutService(this);
        Balances = new BalancesService(this);
        Transactions = new TransactionsService(this);
        Wallets = new WalletsService(this);
        Webhooks = new WebhooksService(this);
        Sandbox = new SandboxService(this);
    }

    /// <summary>GET /ping: credential and workspace probe.</summary>
    public Task<PingResponse> PingAsync(CancellationToken cancellationToken = default) =>
        RequestAsync<PingResponse>(HttpMethod.Get, "/ping", null, null, cancellationToken);

    internal async Task<T> RequestAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var url = _baseUrl + path;
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        req.Headers.TryAddWithoutValidation("X-Workspace-Id", _workspaceId);

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, body.GetType(), JsonOptions);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (!string.IsNullOrEmpty(idempotencyKey))
            req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        using var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var raw = res.Content is null
            ? ""
            : await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var status = (int)res.StatusCode;
        if (status < 200 || status >= 300)
            throw new ClarianException(status, raw);

        if (string.IsNullOrEmpty(raw) || typeof(T) == typeof(object))
            return default!;

        var parsed = JsonSerializer.Deserialize<T>(raw, JsonOptions);
        if (parsed is null)
            throw new ClarianException(status, raw);
        return parsed;
    }

    internal async Task RequestAsync(
        HttpMethod method,
        string path,
        object? body,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var url = _baseUrl + path;
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        req.Headers.TryAddWithoutValidation("X-Workspace-Id", _workspaceId);

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, body.GetType(), JsonOptions);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (!string.IsNullOrEmpty(idempotencyKey))
            req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        using var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var raw = res.Content is null
            ? ""
            : await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var status = (int)res.StatusCode;
        if (status < 200 || status >= 300)
            throw new ClarianException(status, raw);
    }

    internal static string PathEscape(string segment)
    {
        if (string.IsNullOrEmpty(segment))
            return "";
        return Uri.EscapeDataString(segment);
    }

    public void Dispose()
    {
        if (_disposeHttp)
            _http.Dispose();
    }
}
