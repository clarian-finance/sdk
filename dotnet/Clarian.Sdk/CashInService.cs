namespace Clarian;

/// <summary>PIX cash-in: generate and fetch dynamic charges.</summary>
public sealed class CashInService
{
    private readonly ClarianClient _client;

    internal CashInService(ClarianClient client) => _client = client;

    /// <summary>
    /// POST /cash-in/pix. idempotencyKey is required by the API —
    /// retries with the same key return the original order.
    /// </summary>
    public Task<CashInOrderResponse> CreateAsync(
        CashInRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        _client.RequestAsync<CashInOrderResponse>(
            HttpMethod.Post,
            "/cash-in/pix",
            request,
            idempotencyKey,
            cancellationToken);

    /// <summary>GET /cash-in/{id}.</summary>
    public Task<CashInOrderResponse> RetrieveAsync(
        string orderId,
        CancellationToken cancellationToken = default) =>
        _client.RequestAsync<CashInOrderResponse>(
            HttpMethod.Get,
            "/cash-in/" + ClarianClient.PathEscape(orderId),
            null,
            null,
            cancellationToken);
}
