<?php

declare(strict_types=1);

namespace Clarian;

/** Webhook subscription CRUD. */
final class WebhooksService
{
    public function __construct(private readonly Client $client)
    {
    }

    /**
     * GET /webhooks.
     *
     * @return list<array<string, mixed>>
     */
    public function list(): array
    {
        $res = $this->client->request('GET', '/webhooks');
        $subs = $res['subscriptions'] ?? null;

        return is_array($subs) ? array_values($subs) : [];
    }

    /**
     * POST /webhooks: secret is returned once; store it.
     *
     * @param array<string, mixed> $params
     * @return array<string, mixed> Subscription fields plus secret when present.
     */
    public function create(array $params): array
    {
        $res = $this->client->request('POST', '/webhooks', $params);
        $sub = $res['subscription'] ?? null;
        $out = is_array($sub) ? $sub : [];
        if (array_key_exists('secret', $res)) {
            $out['secret'] = $res['secret'];
        }

        return $out;
    }

    /**
     * PATCH /webhooks/{id}.
     *
     * @param array<string, mixed> $params
     * @return array<string, mixed>
     */
    public function update(string $webhookId, array $params): array
    {
        $res = $this->client->request(
            'PATCH',
            '/webhooks/' . Client::pathEscape($webhookId),
            $params,
        );
        $sub = $res['subscription'] ?? null;

        return is_array($sub) ? $sub : $res;
    }

    /**
     * DELETE /webhooks/{id}.
     */
    public function delete(string $webhookId): void
    {
        $this->client->request(
            'DELETE',
            '/webhooks/' . Client::pathEscape($webhookId),
        );
    }
}
