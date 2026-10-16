using System.Text.Json;

namespace Clarian;

public sealed record ClarianOptions
{
    /// <summary><c>cl_live_sk_...</c> (live) or <c>cl_test_sk_...</c> (sandbox). The key picks the environment.</summary>
    public required string ApiKey { get; init; }
    public required string WorkspaceId { get; init; }
    /// <summary>API origin. Default <c>https://api.clarian.finance</c>.</summary>
    public string? BaseUrl { get; init; }
    /// <summary>Per-request timeout. Default 30 s.</summary>
    public TimeSpan? Timeout { get; init; }
    /// <summary>Act on one child workspace. Same as <see cref="ClarianClient.ForSubaccount"/>.</summary>
    public string? SubaccountId { get; init; }
    /// <summary>Retries for 429/502/503/504 and network errors on reads, deletes and idempotent creates. Default 2.</summary>
    public int MaxRetries { get; init; } = 2;
    /// <summary>Custom HttpClient (tests, proxies). Default a shared instance.</summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>Never prints the API key.</summary>
    public override string ToString() => $"ClarianOptions {{ WorkspaceId = {WorkspaceId}, SubaccountId = {SubaccountId}, BaseUrl = {BaseUrl}, ApiKey = **** }}";
}

public sealed class ClarianClient
{
    private const string DefaultBaseUrl = "https://api.clarian.finance";
    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly ClientConfig _config;
    private readonly ClarianOptions _options;

    public ClarianEnvironment Environment => _config.Environment;
    public Balances Balances { get; }
    public Wallets Wallets { get; }
    public Transactions Transactions { get; }
    public Pix Pix { get; }
    public Conversions Conversions { get; }
    public Trading Trading { get; }
    public Crypto Crypto { get; }
    public Customers Customers { get; }
    public Kyc Kyc { get; }
    public Whitelist Whitelist { get; }
    public BankAccounts BankAccounts { get; }
    public Webhooks Webhooks { get; }
    public PaymentLinks PaymentLinks { get; }
    public Checkout Checkout { get; }
    public SubAccounts Subaccounts { get; }
    public ApiKeys ApiKeys { get; }
    public Settings Settings { get; }
    public Notifications Notifications { get; }
    public Team Team { get; }
    public Workflows Workflows { get; }
    public Approvals Approvals { get; }
    public Treasury Treasury { get; }

    public ClarianClient(ClarianOptions options)
    {
        if (string.IsNullOrEmpty(options.ApiKey)) throw new ArgumentException("apiKey is required");
        if (string.IsNullOrEmpty(options.WorkspaceId)) throw new ArgumentException("workspaceId is required");
        // A blank id would drop the header and silently act on the parent workspace.
        if (options.SubaccountId is not null && string.IsNullOrWhiteSpace(options.SubaccountId))
            throw new ArgumentException("subaccountId must not be blank");
        _options = options;
        var c = _config = new ClientConfig(
            options.ApiKey, options.WorkspaceId, CheckBaseUrl(options.BaseUrl ?? DefaultBaseUrl),
            options.Timeout ?? TimeSpan.FromSeconds(30), Http.ResolveEnvironment(options.ApiKey),
            options.SubaccountId, options.HttpClient ?? SharedHttp, options.MaxRetries);
        Balances = new(c); Wallets = new(c); Transactions = new(c); Pix = new(c); Conversions = new(c);
        Trading = new(c); Crypto = new(c); Customers = new(c); Kyc = new(c); Whitelist = new(c); BankAccounts = new(c);
        Webhooks = new(c); PaymentLinks = new(c); Checkout = new(c); Subaccounts = new(c); ApiKeys = new(c);
        Settings = new(c); Notifications = new(c); Team = new(c); Workflows = new(c); Approvals = new(c); Treasury = new(c);
    }

    /// <summary>Checks the key and workspace.</summary>
    public Task<JsonElement> PingAsync(CancellationToken ct = default) => Http.RequestAsync(_config, HttpMethod.Get, "ping", ct: ct);

    /// <summary>A client that acts on one child workspace (sends <c>X-Subaccount-Id</c>).</summary>
    public ClarianClient ForSubaccount(string subaccountId) =>
        string.IsNullOrWhiteSpace(subaccountId)
            ? throw new ArgumentException("subaccountId is required", nameof(subaccountId))
            : new(_options with { SubaccountId = subaccountId });

    /// <summary>Never prints the API key.</summary>
    public override string ToString() => $"ClarianClient {{ Environment = {Environment}, {_options} }}";

    private static readonly HashSet<string> LocalHttpHosts = new(StringComparer.OrdinalIgnoreCase) { "localhost", "127.0.0.1", "host.docker.internal" };

    // Plain http would send the API key in clear text: only local origins may use it, for testing.
    private static string CheckBaseUrl(string baseUrl)
    {
        var ok = Uri.TryCreate(baseUrl, UriKind.Absolute, out var u)
            && (u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && LocalHttpHosts.Contains(u.Host)));
        if (!ok) throw new ArgumentException("baseUrl must be https (http only for localhost, 127.0.0.1, host.docker.internal)", nameof(baseUrl));
        return baseUrl.TrimEnd('/');
    }
}
