<?php

declare(strict_types=1);

namespace Clarian;

/** On-chain wallets. */
final class Wallets
{
    public function __construct(private readonly Client $client)
    {
    }

    /**
     * GET /wallets: on-chain wallets (optional network filter).
     *
     * @return list<array<string, mixed>>
     */
    public function list(?string $network = null): array
    {
        $path = '/wallets';
        if ($network !== null && $network !== '') {
            $path .= '?' . http_build_query(['network' => $network]);
        }

        $res = $this->client->request('GET', $path);
        $wallets = $res['wallets'] ?? null;

        return is_array($wallets) ? array_values($wallets) : [];
    }

    /**
     * GET /wallets/{id}/balance.
     *
     * @return array<string, mixed>
     */
    public function retrieveBalance(string $walletId): array
    {
        return $this->client->request(
            'GET',
            '/wallets/' . Client::pathEscape($walletId) . '/balance',
        );
    }
}
