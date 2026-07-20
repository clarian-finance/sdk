using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clarian;

public sealed class Payer
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("document_number")]
    public string DocumentNumber { get; set; } = "";
}

public sealed class CashInRequest
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("payer")]
    public Payer Payer { get; set; } = new();

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("external_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExternalId { get; set; }
}

public sealed class PixDetails
{
    [JsonPropertyName("qr_code")]
    public string? QrCode { get; set; }

    [JsonPropertyName("copy_paste")]
    public string? CopyPaste { get; set; }

    [JsonPropertyName("end_to_end_id")]
    public string? EndToEndId { get; set; }
}

public sealed class CashInOrder
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "";

    [JsonPropertyName("pix")]
    public PixDetails Pix { get; set; } = new();

    [JsonPropertyName("fee")]
    public decimal? Fee { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("expires_at")]
    public string? ExpiresAt { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public sealed class CashInOrderResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    [JsonPropertyName("order")]
    public CashInOrder Order { get; set; } = new();
}

public sealed class CashOutRequest
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("pix_key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PixKey { get; set; }

    [JsonPropertyName("pix_key_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PixKeyType { get; set; }

    [JsonPropertyName("external_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExternalId { get; set; }
}

public sealed class CashOutOrder
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("pix_amount")]
    public decimal? PixAmount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "";

    [JsonPropertyName("fee")]
    public decimal? Fee { get; set; }

    [JsonPropertyName("fee_bearer")]
    public string? FeeBearer { get; set; }

    [JsonPropertyName("end_to_end_id")]
    public string? EndToEndId { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public sealed class CashOutOrderResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    [JsonPropertyName("order")]
    public CashOutOrder Order { get; set; } = new();
}

public sealed class DictCheckRequest
{
    [JsonPropertyName("pix_key")]
    public string PixKey { get; set; } = "";

    [JsonPropertyName("key_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KeyType { get; set; }
}

public sealed class DictCheckResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("dict")]
    public JsonElement Dict { get; set; }
}

public sealed class Balance
{
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "";

    [JsonPropertyName("available")]
    public decimal Available { get; set; }

    [JsonPropertyName("pending")]
    public decimal Pending { get; set; }

    [JsonPropertyName("locked")]
    public decimal Locked { get; set; }
}

public sealed class BalancesResponse
{
    [JsonPropertyName("balances")]
    public List<Balance> Balances { get; set; } = new();
}

public sealed class Transaction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("fee")]
    public decimal Fee { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("external_id")]
    public string? ExternalId { get; set; }

    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }
}

public sealed class ListTransactionsParams
{
    public string? Type { get; set; }
    public string? Status { get; set; }
    public int? Limit { get; set; }
}

public sealed class TransactionsListResponse
{
    [JsonPropertyName("transactions")]
    public List<Transaction> Transactions { get; set; } = new();
}

public sealed class TransactionResponse
{
    [JsonPropertyName("transaction")]
    public Transaction Transaction { get; set; } = new();
}

public sealed class Wallet
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("network")]
    public string Network { get; set; } = "";

    [JsonPropertyName("address")]
    public string Address { get; set; } = "";

    [JsonPropertyName("is_auto_provisioned")]
    public bool IsAutoProvisioned { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public sealed class WalletsListResponse
{
    [JsonPropertyName("wallets")]
    public List<Wallet> Wallets { get; set; } = new();
}

public sealed class OnChainBalance
{
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "";

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = "";
}

public sealed class WalletBalanceResponse
{
    [JsonPropertyName("wallet_id")]
    public string WalletId { get; set; } = "";

    [JsonPropertyName("network")]
    public string Network { get; set; } = "";

    [JsonPropertyName("address")]
    public string Address { get; set; } = "";

    [JsonPropertyName("balances")]
    public List<OnChainBalance> Balances { get; set; } = new();
}

public sealed class PingResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    [JsonPropertyName("master_account_id")]
    public string? MasterAccountId { get; set; }
}

public sealed class WebhookInput
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("events")]
    public List<string> Events { get; set; } = new();

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("is_active")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsActive { get; set; }
}

public class Webhook
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("events")]
    public List<string> Events { get; set; } = new();

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }
}

public sealed class WebhookWithSecret : Webhook
{
    [JsonPropertyName("secret")]
    public string Secret { get; set; } = "";
}

public sealed class WebhooksListResponse
{
    [JsonPropertyName("subscriptions")]
    public List<Webhook> Subscriptions { get; set; } = new();
}

public sealed class WebhookSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public Webhook Subscription { get; set; } = new();

    [JsonPropertyName("secret")]
    public string? Secret { get; set; }
}

public sealed class SandboxWebhookEventResult
{
    public bool Ok { get; set; }
    public string? Environment { get; set; }
    public string? EventType { get; set; }
    public int Enqueued { get; set; }
    public string DeliveryId { get; set; } = "";
}

internal sealed class SandboxStatusBody
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}

internal sealed class SandboxEventTypeBody
{
    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = "";
}

internal sealed class SandboxWebhookEventApiResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    [JsonPropertyName("event_type")]
    public string? EventType { get; set; }

    [JsonPropertyName("enqueued")]
    public SandboxEnqueuedInfo? Enqueued { get; set; }
}

internal sealed class SandboxEnqueuedInfo
{
    [JsonPropertyName("enqueued")]
    public int Enqueued { get; set; }

    [JsonPropertyName("delivery_id")]
    public string? DeliveryId { get; set; }
}

internal sealed class SandboxResendResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("delivery_id")]
    public string DeliveryId { get; set; } = "";
}
