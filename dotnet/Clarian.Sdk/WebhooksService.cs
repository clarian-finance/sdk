namespace Clarian;

/// <summary>Webhook subscription CRUD.</summary>
public sealed class WebhooksService
{
    private readonly ClarianClient _client;

    internal WebhooksService(ClarianClient client) => _client = client;

    /// <summary>GET /webhooks.</summary>
    public async Task<IReadOnlyList<Webhook>> ListAsync(CancellationToken cancellationToken = default)
    {
        var res = await _client.RequestAsync<WebhooksListResponse>(
            HttpMethod.Get,
            "/webhooks",
            null,
            null,
            cancellationToken).ConfigureAwait(false);
        return res.Subscriptions ?? new List<Webhook>();
    }

    /// <summary>
    /// POST /webhooks. Secret is a sibling of subscription in the 201 body and is
    /// shown only once — store it.
    /// </summary>
    public async Task<WebhookWithSecret> CreateAsync(
        WebhookInput input,
        CancellationToken cancellationToken = default)
    {
        var res = await _client.RequestAsync<WebhookSubscriptionResponse>(
            HttpMethod.Post,
            "/webhooks",
            input,
            null,
            cancellationToken).ConfigureAwait(false);

        var sub = res.Subscription ?? new Webhook();
        return new WebhookWithSecret
        {
            Id = sub.Id,
            Url = sub.Url,
            Events = sub.Events,
            IsActive = sub.IsActive,
            Description = sub.Description,
            Environment = sub.Environment,
            CreatedAt = sub.CreatedAt,
            UpdatedAt = sub.UpdatedAt,
            Secret = res.Secret ?? "",
        };
    }

    /// <summary>PATCH /webhooks/{id}.</summary>
    public async Task<Webhook> UpdateAsync(
        string webhookId,
        WebhookInput input,
        CancellationToken cancellationToken = default)
    {
        var res = await _client.RequestAsync<WebhookSubscriptionResponse>(
            HttpMethod.Patch,
            "/webhooks/" + ClarianClient.PathEscape(webhookId),
            input,
            null,
            cancellationToken).ConfigureAwait(false);
        return res.Subscription;
    }

    /// <summary>DELETE /webhooks/{id}.</summary>
    public Task DeleteAsync(string webhookId, CancellationToken cancellationToken = default) =>
        _client.RequestAsync(
            HttpMethod.Delete,
            "/webhooks/" + ClarianClient.PathEscape(webhookId),
            null,
            null,
            cancellationToken);
}
