<?php

declare(strict_types=1);

namespace Clarian;

/** Workspace balances. */
final class Balances
{
    public function __construct(private readonly Client $client)
    {
    }

    /**
     * GET /account/balances.
     *
     * @return list<array<string, mixed>>
     */
    public function list(): array
    {
        $res = $this->client->request('GET', '/account/balances');
        $balances = $res['balances'] ?? null;

        return is_array($balances) ? array_values($balances) : [];
    }
}
