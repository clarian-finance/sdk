<?php

declare(strict_types=1);

namespace Clarian;

/** PIX cash-out (payout) orders. */
final class CashOut
{
    public function __construct(private readonly Client $client)
    {
    }

    /**
     * POST /pix/payouts/dict: preview PIX key owner before payout.
     *
     * @return array<string, mixed>
     */
    public function dictCheck(string $pixKey, ?string $keyType = null): array
    {
        $body = ['pix_key' => $pixKey];
        if ($keyType !== null && $keyType !== '') {
            $body['key_type'] = $keyType;
        }

        return $this->client->request('POST', '/pix/payouts/dict', $body);
    }

    /**
     * POST /cash-out/pix: send BRL via PIX (idempotency key required).
     *
     * @param array<string, mixed> $params
     * @return array<string, mixed>
     */
    public function create(array $params, string $idempotencyKey): array
    {
        return $this->client->request('POST', '/cash-out/pix', $params, $idempotencyKey);
    }

    /**
     * GET /cash-out/{id}.
     *
     * @return array<string, mixed>
     */
    public function retrieve(string $orderId): array
    {
        return $this->client->request(
            'GET',
            '/cash-out/' . Client::pathEscape($orderId),
        );
    }
}
