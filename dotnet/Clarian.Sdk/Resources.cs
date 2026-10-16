// One class per API area, in the order of the public reference.
// Bodies are any JSON-serializable object (anonymous objects, dictionaries, your own records) with
// snake_case keys as in openapi.json. Replies are the unwrapped `data` as a JsonElement:
// call .Deserialize<T>() for your own types.
using System.Text.Json;
using static Clarian.Http;

namespace Clarian;

public abstract class Resource
{
    private protected readonly ClientConfig C;
    private protected Resource(ClientConfig c) => C = c;
    private protected static readonly object Completed = new { status = "completed" };

    private protected Task<JsonElement> Get(string path, (string, object?)[]? query = null, CancellationToken ct = default) =>
        RequestAsync(C, HttpMethod.Get, path, query, ct: ct);
    private protected Task<JsonElement> Send(HttpMethod m, string path, object? body = null, string? idempotencyKey = null, CancellationToken ct = default) =>
        RequestAsync(C, m, path, body: body, idempotencyKey: idempotencyKey, ct: ct);
}

public sealed class Balances : Resource
{
    internal Balances(ClientConfig c) : base(c) { }
    public Task<JsonElement> ListAsync(CancellationToken ct = default) => Get("balances", ct: ct);
}

public sealed class Wallets : Resource
{
    internal Wallets(ClientConfig c) : base(c) { }
    public Task<JsonElement> ListAsync(CancellationToken ct = default) => Get("wallets", ct: ct);
    public Task<JsonElement> CreateAsync(object? body = null, CancellationToken ct = default) => Send(HttpMethod.Post, "wallets", body ?? new { }, ct: ct);
    public Task<JsonElement> RetrieveAsync(string id, CancellationToken ct = default) => Get($"wallets/{Seg(id)}", ct: ct);
    public Task<JsonElement> BalanceAsync(string id, CancellationToken ct = default) => Get($"wallets/{Seg(id)}/balance", ct: ct);
    public Task<JsonElement> TransactionsAsync(string id, int? limit = null, CancellationToken ct = default) =>
        Get($"wallets/{Seg(id)}/transactions", Q(("limit", limit)), ct);
}

public sealed class Transactions : Resource
{
    internal Transactions(ClientConfig c) : base(c) { }
    /// <param name="before">Id of the last row of the previous page (pages newest first).</param>
    public Task<JsonElement> ListAsync(int? limit = null, string? before = null, CancellationToken ct = default) =>
        Get("transactions", Q(("limit", limit), ("before", before)), ct);
    public Task<JsonElement> RetrieveAsync(string id, CancellationToken ct = default) => Get($"transactions/{Seg(id)}", ct: ct);
    public Task<JsonElement> StatementAsync(int? limit = null, string? before = null, string? from = null, string? to = null,
        string? currency = null, string? kind = null, CancellationToken ct = default) =>
        Get("statement", Q(("limit", limit), ("before", before), ("from", from), ("to", to), ("currency", currency), ("kind", kind)), ct);
}

/// <summary>PIX cash-in: a charge the payer pays with a QR code / copia-e-cola.</summary>
public sealed class PixPayins : Resource
{
    internal PixPayins(ClientConfig c) : base(c) { }
    public Task<JsonElement> CreateAsync(object body, string idempotencyKey, CancellationToken ct = default) =>
        Send(HttpMethod.Post, "pix/payins", body, RequireKey(idempotencyKey), ct);
    public Task<JsonElement> RetrieveAsync(string id, CancellationToken ct = default) => Get($"pix/payins/{Seg(id)}", ct: ct);
    /// <summary>Sandbox only: settle (default) or fail a pending charge.</summary>
    public Task<JsonElement> SimulateAsync(string id, object? body = null, CancellationToken ct = default) =>
        Send(HttpMethod.Post, $"pix/payins/{Seg(id)}/simulate", body ?? Completed, ct: ct);
}

/// <summary>PIX cash-out. Over the API the workspace's registered settlement key receives the money (403 <c>settlement_key_required</c> until one is set).</summary>
public sealed class PixPayouts : Resource
{
    internal PixPayouts(ClientConfig c) : base(c) { }
    public Task<JsonElement> CreateAsync(object body, string idempotencyKey, CancellationToken ct = default) =>
        Send(HttpMethod.Post, "pix/payouts", body, RequireKey(idempotencyKey), ct);
    public Task<JsonElement> RetrieveAsync(string id, CancellationToken ct = default) => Get($"pix/payouts/{Seg(id)}", ct: ct);
    /// <summary>Fee and total debit before sending.</summary>
    public Task<JsonElement> FeeAsync(object body, CancellationToken ct = default) => Send(HttpMethod.Post, "pix/payouts/fee", body, ct: ct);
    /// <summary>Resolve a PIX key to its holder (masked) before paying.</summary>
    public Task<JsonElement> LookupAsync(object body, CancellationToken ct = default) => Send(HttpMethod.Post, "pix/payouts/dict", body, ct: ct);
    /// <summary>Sandbox only.</summary>
    public Task<JsonElement> SimulateAsync(string id, object? body = null, CancellationToken ct = default) =>
        Send(HttpMethod.Post, $"pix/payouts/{Seg(id)}/simulate", body ?? Completed, ct: ct);
    public Task<JsonElement> ListAccountsAsync(CancellationToken ct = default) => Get("pix/payout-accounts", ct: ct);
}

public sealed class Pix
{
    public PixPayins Payins { get; }
    public PixPayouts Payouts { get; }
    internal Pix(ClientConfig c) { Payins = new PixPayins(c); Payouts = new PixPayouts(c); }
}

/// <summary>Quote then convert. Direction is always base_currency to quote_currency.</summary>
public sealed class Conversions : Resource
{
    internal Conversions(ClientConfig c) : base(c) { }
    public Task<JsonElement> QuoteAsync(object body, CancellationToken ct = default) => Send(HttpMethod.Post, "rfq/quote", body, ct: ct);
    /// <summary>
    /// A quote converts once: a second execute is 409 <c>quote_not_open</c>, an old one 410 <c>quote_expired</c>.
    /// Never retried automatically; after a lost reply, read the payment with <c>Transactions.ListAsync()</c>.
    /// </summary>
    public Task<JsonElement> ExecuteAsync(string quoteId, CancellationToken ct = default) =>
        Send(HttpMethod.Post, "rfq/execute", new { quote_id = quoteId }, ct: ct);
    public Task<JsonElement> PricesAsync(IEnumerable<string>? symbols = null, CancellationToken ct = default) =>
        Get("prices", Q(("symbols", symbols is null ? null : string.Join(",", symbols))), ct);
}

public sealed class Trading : Resource
{
    internal Trading(ClientConfig c) : base(c) { }
    public Task<JsonElement> SummaryAsync(CancellationToken ct = default) => Get("trading/summary", ct: ct);
    /// <summary>BRL available to trading. Body: <c>new { amount_minor = "100000" }</c>.</summary>
    public Task<JsonElement> DepositAsync(object body, string idempotencyKey, CancellationToken ct = default) =>
        Send(HttpMethod.Post, "trading/deposits", body, RequireKey(idempotencyKey), ct);
    /// <summary>BRL trading to available.</summary>
    public Task<JsonElement> WithdrawAsync(object body, string idempotencyKey, CancellationToken ct = default) =>
        Send(HttpMethod.Post, "trading/withdrawals", body, RequireKey(idempotencyKey), ct);
    /// <summary>USDT/USDC trading to your own wallet on <c>network</c>. Body: <c>new { amount_minor, currency, network }</c>.</summary>
    public Task<JsonElement> CryptoWithdrawAsync(object body, string idempotencyKey, CancellationToken ct = default) =>
        Send(HttpMethod.Post, "trading/crypto-withdrawals", body, RequireKey(idempotencyKey), ct);
}

public sealed class Crypto : Resource
{
    internal Crypto(ClientConfig c) : base(c) { }
    public Task<JsonElement> SendAsync(object body, string idempotencyKey, CancellationToken ct = default) =>
        Send(HttpMethod.Post, "crypto/send", body, RequireKey(idempotencyKey), ct);
    /// <summary>Sandbox only.</summary>
    public Task<JsonElement> SimulateAsync(string id, object? body = null, CancellationToken ct = default) =>
        Send(HttpMethod.Post, $"crypto/send/{Seg(id)}/simulate", body ?? Completed, ct: ct);
    public Task<JsonElement> NetworksAsync(CancellationToken ct = default) => Get("crypto/networks", ct: ct);
}

public sealed class Customers : Resource
{
    internal Customers(ClientConfig c) : base(c) { }
    public Task<JsonElement> ListAsync(CancellationToken ct = default) => Get("customers", ct: ct);
    public Task<JsonElement> CreateAsync(object body, CancellationToken ct = default) => Send(HttpMethod.Post, "customers", body, ct: ct);
    public Task<JsonElement> RetrieveAsync(string id, CancellationToken ct = default) => Get($"customers/{Seg(id)}", ct: ct);
    public Task<JsonElement> UpdateAsync(string id, object body, CancellationToken ct = default) => Send(HttpMethod.Patch, $"customers/{Seg(id)}", body, ct: ct);
}

public sealed class Kyc : Resource
{
    internal Kyc(ClientConfig c) : base(c) { }
    public Task<JsonElement> ListAsync(CancellationToken ct = default) => Get("kyc", ct: ct);
    public Task<JsonElement> CreateAsync(object body, CancellationToken ct = default) => Send(HttpMethod.Post, "kyc", body, ct: ct);
    public Task<JsonElement> RetrieveAsync(string id, CancellationToken ct = default) => Get($"kyc/{Seg(id)}", ct: ct);
    public Task<JsonElement> ListRfiAsync(string id, CancellationToken ct = default) => Get($"kyc/{Seg(id)}/rfi", ct: ct);
    public Task<JsonElement> AnswerRfiAsync(string id, object body, CancellationToken ct = default) => Send(HttpMethod.Post, $"kyc/{Seg(id)}/rfi", body, ct: ct);
}

public sealed class Whitelist : Resource
{
    internal Whitelist(ClientConfig c) : base(c) { }
    public Task<JsonElement> ListAsync(CancellationToken ct = default) => Get("whitelist", ct: ct);
    public Task<JsonElement> SettingsAsync(CancellationToken ct = default) => Get("whitelist/settings", ct: ct);
}

public sealed class BankAccounts : Resource
{
    internal BankAccounts(ClientConfig c) : base(c) { }
    public Task<JsonElement> ListAsync(CancellationToken ct = default) => Get("bank-accounts", ct: ct);
}

public sealed class Webhooks : Resource
{
    internal Webhooks(ClientConfig c) : base(c) { }
    public Task<JsonElement> ListAsync(CancellationToken ct = default) => Get("webhooks", ct: ct);
    /// <summary>The signing <c>secret</c> is in this reply only. Store it.</summary>
    public Task<JsonElement> CreateAsync(object body, CancellationToken ct = default) => Send(HttpMethod.Post, "webhooks", body, ct: ct);
    public Task<JsonElement> RetrieveAsync(string id, CancellationToken ct = default) => Get($"webhooks/{Seg(id)}", ct: ct);
    public Task<JsonElement> UpdateAsync(string id, object body, CancellationToken ct = default) => Send(HttpMethod.Patch, $"webhooks/{Seg(id)}", body, ct: ct);
    public Task<JsonElement> DeleteAsync(string id, CancellationToken ct = default) => Send(HttpMethod.Delete, $"webhooks/{Seg(id)}", ct: ct);
    /// <summary>Sandbox key only: queues a signed <c>test.ping</c> to the endpoint.</summary>
    public Task<JsonElement> TestAsync(string id, CancellationToken ct = default) => Send(HttpMethod.Post, $"webhooks/{Seg(id)}/test", ct: ct);
    public Task<JsonElement> DeliveriesAsync(CancellationToken ct = default) => Get("webhooks/deliveries", ct: ct);
    /// <summary>Sandbox key only.</summary>
    public Task<JsonElement> ResendAsync(string deliveryId, CancellationToken ct = default) =>
        Send(HttpMethod.Post, $"webhooks/deliveries/{Seg(deliveryId)}/resend", ct: ct);
}

public sealed class PaymentLinks : Resource
{
    internal PaymentLinks(ClientConfig c) : base(c) { }
    public Task<JsonElement> ListAsync(CancellationToken ct = default) => Get("payment-links", ct: ct);
    public Task<JsonElement> CreateAsync(object body, CancellationToken ct = default) => Send(HttpMethod.Post, "payment-links", body, ct: ct);
    public Task<JsonElement> RetrieveAsync(string id, CancellationToken ct = default) => Get($"payment-links/{Seg(id)}", ct: ct);
    public Task<JsonElement> UpdateAsync(string id, object body, CancellationToken ct = default) => Send(HttpMethod.Patch, $"payment-links/{Seg(id)}", body, ct: ct);
    public Task<JsonElement> DeleteAsync(string id, CancellationToken ct = default) => Send(HttpMethod.Delete, $"payment-links/{Seg(id)}", ct: ct);
    /// <summary>Paginated <c>{items, next_cursor}</c>: pass <c>next_cursor</c> back as <paramref name="cursor"/>.</summary>
    public Task<JsonElement> PaymentsAsync(int? limit = null, string? cursor = null, string? from = null, string? to = null,
        string? linkId = null, string? status = null, string? payer = null, CancellationToken ct = default) =>
        Get("payment-links/payments", Q(("limit", limit), ("cursor", cursor), ("from", from), ("to", to), ("link_id", linkId), ("status", status), ("payer", payer)), ct);
    public Task<JsonElement> StatsAsync(string? from = null, string? to = null, string? linkId = null, string? status = null,
        string? payer = null, CancellationToken ct = default) =>
        Get("payment-links/stats", Q(("from", from), ("to", to), ("link_id", linkId), ("status", status), ("payer", payer)), ct);
    public Task<JsonElement> SplitsAsync(string id, CancellationToken ct = default) => Get($"payment-links/{Seg(id)}/splits", ct: ct);
    public Task<JsonElement> ReplaceSplitsAsync(string id, object body, CancellationToken ct = default) =>
        Send(HttpMethod.Put, $"payment-links/{Seg(id)}/splits", body, ct: ct);
    public Task<JsonElement> ChargeAsync(string id, object body, string idempotencyKey, CancellationToken ct = default) =>
        Send(HttpMethod.Post, $"payment-links/{Seg(id)}/charges", body, RequireKey(idempotencyKey), ct);
}

/// <summary>The public checkout page of a payment link.</summary>
public sealed class Checkout : Resource
{
    internal Checkout(ClientConfig c) : base(c) { }
    public Task<JsonElement> RetrieveAsync(string slug, CancellationToken ct = default) => Get($"checkout/{Seg(slug)}", ct: ct);
    public Task<JsonElement> PayAsync(string slug, object body, string idempotencyKey, CancellationToken ct = default) =>
        Send(HttpMethod.Post, $"checkout/{Seg(slug)}/pay", body, RequireKey(idempotencyKey), ct);
}

public sealed class SubAccounts : Resource
{
    internal SubAccounts(ClientConfig c) : base(c) { }
    public Task<JsonElement> ListAsync(CancellationToken ct = default) => Get("subaccounts", ct: ct);
    public Task<JsonElement> CreateAsync(object body, CancellationToken ct = default) => Send(HttpMethod.Post, "subaccounts", body, ct: ct);
    public Task<JsonElement> RetrieveAsync(string id, CancellationToken ct = default) => Get($"subaccounts/{Seg(id)}", ct: ct);
    public Task<JsonElement> BalancesAsync(string id, CancellationToken ct = default) => Get($"subaccounts/{Seg(id)}/balances", ct: ct);
    public Task<JsonElement> TransactionsAsync(string id, int? limit = null, CancellationToken ct = default) =>
        Get($"subaccounts/{Seg(id)}/transactions", Q(("limit", limit)), ct);
}

public sealed class ApiKeys : Resource
{
    internal ApiKeys(ClientConfig c) : base(c) { }
    public Task<JsonElement> ListAsync(CancellationToken ct = default) => Get("api-keys", ct: ct);
    public Task<JsonElement> AuditAsync(int? limit = null, CancellationToken ct = default) => Get("api-keys/audit", Q(("limit", limit)), ct);
}

public sealed class Settings : Resource
{
    internal Settings(ClientConfig c) : base(c) { }
    public Task<JsonElement> RetrieveAsync(CancellationToken ct = default) => Get("settings", ct: ct);
}

public sealed class Notifications : Resource
{
    internal Notifications(ClientConfig c) : base(c) { }
    public Task<JsonElement> ListAsync(CancellationToken ct = default) => Get("notifications", ct: ct);
}

public sealed class Team : Resource
{
    internal Team(ClientConfig c) : base(c) { }
    public Task<JsonElement> ListAsync(CancellationToken ct = default) => Get("team", ct: ct);
}

public sealed class Workflows : Resource
{
    internal Workflows(ClientConfig c) : base(c) { }
    public Task<JsonElement> ListAsync(CancellationToken ct = default) => Get("workflows", ct: ct);
    public Task<JsonElement> CreateAsync(object body, CancellationToken ct = default) => Send(HttpMethod.Post, "workflows", body, ct: ct);
    public Task<JsonElement> RunsAsync(CancellationToken ct = default) => Get("workflows/runs", ct: ct);
}

public sealed class Approvals : Resource
{
    internal Approvals(ClientConfig c) : base(c) { }
    public Task<JsonElement> ListAsync(CancellationToken ct = default) => Get("approvals", ct: ct);
    public Task<JsonElement> PolicyAsync(CancellationToken ct = default) => Get("approval-policy", ct: ct);
}

public sealed class Treasury : Resource
{
    internal Treasury(ClientConfig c) : base(c) { }
    public Task<JsonElement> ListAsync(CancellationToken ct = default) => Get("treasury", ct: ct);
    public Task<JsonElement> CreateAsync(object body, CancellationToken ct = default) => Send(HttpMethod.Post, "treasury", body, ct: ct);
}
