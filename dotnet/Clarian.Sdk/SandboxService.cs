namespace Clarian;

/// <summary>
/// Sandbox-only test helpers. Refuse non-cl_test_sk_ keys before any HTTP call.
/// Outside the test environment the gateway returns 404 {error:"sandbox_only"}.
/// </summary>
public sealed class SandboxService
{
    /// <summary>Magic PIX key: sandbox payout fails and refunds.</summary>
    public const string SandboxFailPixKey = "fail@sandbox.clarian";

    /// <summary>Magic PIX key: sandbox payout stays pending until simulated.</summary>
    public const string SandboxPendingPixKey = "pending@sandbox.clarian";

    public const string EventPixPayinCreated = "pix_payin.created";
    public const string EventPixPayinCompleted = "pix_payin.completed";
    public const string EventPixPayinExpired = "pix_payin.expired";
    public const string EventPixPayoutCreated = "pix_payout.created";
    public const string EventPixPayoutCompleted = "pix_payout.completed";
    public const string EventPixPayoutFailed = "pix_payout.failed";
    public const string EventCheckoutPaid = "checkout.paid";

    private static readonly HashSet<string> CashInStatuses =
        new(StringComparer.Ordinal) { "completed", "expired", "failed" };

    private static readonly HashSet<string> CashOutStatuses =
        new(StringComparer.Ordinal) { "completed", "failed" };

    private readonly ClarianClient _client;

    internal SandboxService(ClarianClient client) => _client = client;

    private void RequireTestKey()
    {
        if (!_client.ApiKey.StartsWith("cl_test_sk_", StringComparison.Ordinal))
            throw new InvalidOperationException("sandbox helpers require a cl_test_sk_ key");
    }

    /// <summary>
    /// POST /pix/payins/{id}/simulate. Empty/null status defaults to completed.
    /// Allowed: completed | expired | failed.
    /// </summary>
    public Task<CashInOrderResponse> SimulateCashInAsync(
        string orderId,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        RequireTestKey();
        var resolved = string.IsNullOrEmpty(status) ? "completed" : status;
        if (!CashInStatuses.Contains(resolved))
            throw new ArgumentException($"invalid cash-in simulate status \"{resolved}\"", nameof(status));

        var body = new SandboxStatusBody { Status = resolved };
        return _client.RequestAsync<CashInOrderResponse>(
            HttpMethod.Post,
            "/pix/payins/" + ClarianClient.PathEscape(orderId) + "/simulate",
            body,
            null,
            cancellationToken);
    }

    /// <summary>
    /// POST /pix/payouts/{id}/simulate. Empty/null status defaults to completed.
    /// Allowed: completed | failed.
    /// </summary>
    public Task<CashOutOrderResponse> SimulateCashOutAsync(
        string orderId,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        RequireTestKey();
        var resolved = string.IsNullOrEmpty(status) ? "completed" : status;
        if (!CashOutStatuses.Contains(resolved))
            throw new ArgumentException($"invalid cash-out simulate status \"{resolved}\"", nameof(status));

        var body = new SandboxStatusBody { Status = resolved };
        return _client.RequestAsync<CashOutOrderResponse>(
            HttpMethod.Post,
            "/pix/payouts/" + ClarianClient.PathEscape(orderId) + "/simulate",
            body,
            null,
            cancellationToken);
    }

    /// <summary>POST /webhooks/{id}/test: enqueue a sample delivery.</summary>
    public async Task<SandboxWebhookEventResult> SendWebhookEventAsync(
        string subscriptionId,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        RequireTestKey();
        var body = new SandboxEventTypeBody { EventType = eventType };
        var res = await _client.RequestAsync<SandboxWebhookEventApiResponse>(
            HttpMethod.Post,
            "/webhooks/" + ClarianClient.PathEscape(subscriptionId) + "/test",
            body,
            null,
            cancellationToken).ConfigureAwait(false);

        return new SandboxWebhookEventResult
        {
            Ok = res.Ok,
            Environment = res.Environment,
            EventType = res.EventType,
            Enqueued = res.Enqueued?.Enqueued ?? 0,
            DeliveryId = res.Enqueued?.DeliveryId ?? "",
        };
    }

    /// <summary>POST /webhooks/deliveries/{id}/resend; returns the new delivery id.</summary>
    public async Task<string> ResendWebhookDeliveryAsync(
        string deliveryId,
        CancellationToken cancellationToken = default)
    {
        RequireTestKey();
        var res = await _client.RequestAsync<SandboxResendResponse>(
            HttpMethod.Post,
            "/webhooks/deliveries/" + ClarianClient.PathEscape(deliveryId) + "/resend",
            null,
            null,
            cancellationToken).ConfigureAwait(false);
        return res.DeliveryId ?? "";
    }
}
