namespace Clarian;

/// <summary>PIX cash-out: DICT lookup, create payout, fetch order.</summary>
public sealed class CashOutService
{
    private readonly ClarianClient _client;

    internal CashOutService(ClarianClient client) => _client = client;

    /// <summary>
    /// POST /pix/payouts/dict: preview a PIX key owner before paying out.
    /// keyType optional: CPF | CNPJ | EMAIL | PHONE | EVP
    /// </summary>
    public Task<DictCheckResponse> DictCheckAsync(
        string pixKey,
        string? keyType = null,
        CancellationToken cancellationToken = default)
    {
        var body = new DictCheckRequest
        {
            PixKey = pixKey,
            KeyType = string.IsNullOrEmpty(keyType) ? null : keyType,
        };
        return _client.RequestAsync<DictCheckResponse>(
            HttpMethod.Post,
            "/pix/payouts/dict",
            body,
            null,
            cancellationToken);
    }

    /// <summary>
    /// POST /cash-out/pix. idempotencyKey is required —
    /// retries never double-send.
    /// </summary>
    public Task<CashOutOrderResponse> CreateAsync(
        CashOutRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        _client.RequestAsync<CashOutOrderResponse>(
            HttpMethod.Post,
            "/cash-out/pix",
            request,
            idempotencyKey,
            cancellationToken);

    /// <summary>GET /cash-out/{id}.</summary>
    public Task<CashOutOrderResponse> RetrieveAsync(
        string orderId,
        CancellationToken cancellationToken = default) =>
        _client.RequestAsync<CashOutOrderResponse>(
            HttpMethod.Get,
            "/cash-out/" + ClarianClient.PathEscape(orderId),
            null,
            null,
            cancellationToken);
}
