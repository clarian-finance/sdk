<?php

declare(strict_types=1);

namespace Clarian;

/** PIX cash-in (pay-in) orders. */
final class CashIn
{
    public function __construct(private readonly Client $client)
    {
    }

    /**
     * POST /cash-in/pix: generate a dynamic PIX charge.
     * Idempotency-Key is required by the API.
     *
     * @param array<string, mixed> $params
     * @return array<string, mixed>
     */
    public function create(array $params, string $idempotencyKey): array
    {
        return $this->client->request('POST', '/cash-in/pix', $params, $idempotencyKey);
    }

    /**
     * GET /cash-in/{id}.
     *
     * @return array<string, mixed>
     */
    public function retrieve(string $orderId): array
    {
        return $this->client->request(
            'GET',
            '/cash-in/' . Client::pathEscape($orderId),
        );
    }
}
