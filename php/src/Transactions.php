<?php

declare(strict_types=1);

namespace Clarian;

/** Ledger transactions. */
final class Transactions
{
    public function __construct(private readonly Client $client)
    {
    }

    /**
     * GET /transactions with optional type/status/limit filters.
     *
     * @return list<array<string, mixed>>
     */
    public function list(
        ?string $type = null,
        ?string $status = null,
        ?int $limit = null,
    ): array {
        $q = [];
        if ($type !== null && $type !== '') {
            $q['type'] = $type;
        }
        if ($status !== null && $status !== '') {
            $q['status'] = $status;
        }
        if ($limit !== null && $limit > 0) {
            $q['limit'] = (string) $limit;
        }

        $path = '/transactions';
        if ($q !== []) {
            $path .= '?' . http_build_query($q);
        }

        $res = $this->client->request('GET', $path);
        $txs = $res['transactions'] ?? null;

        return is_array($txs) ? array_values($txs) : [];
    }

    /**
     * GET /transactions/{id}.
     *
     * @return array<string, mixed>
     */
    public function retrieve(string $transactionId): array
    {
        $res = $this->client->request(
            'GET',
            '/transactions/' . Client::pathEscape($transactionId),
        );
        $tx = $res['transaction'] ?? null;

        return is_array($tx) ? $tx : $res;
    }
}
