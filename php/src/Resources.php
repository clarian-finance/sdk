<?php

declare(strict_types=1);

// One class per API area, in the order of the public reference. Bodies and replies are associative arrays
// shaped like the schemas in openapi.json.

namespace Clarian\Resources;

use Clarian\Http;

abstract class Resource
{
    public function __construct(protected readonly Http $h)
    {
    }

    protected static function seg(string $id): string
    {
        // URL parsers resolve "." and ".." (even encoded) to another route: refuse them.
        if ($id === '' || $id === '.' || $id === '..') {
            throw new \InvalidArgumentException('id must not be empty, "." or ".."');
        }
        return str_replace('.', '%2E', rawurlencode($id));
    }

    /** Money-moving creates: a missing key throws before any network call. */
    protected static function requireKey(string $idempotencyKey): string
    {
        if ($idempotencyKey === '') {
            throw new \InvalidArgumentException('idempotencyKey is required: reuse the same key to retry safely');
        }
        return $idempotencyKey;
    }
}

final class Balances extends Resource
{
    public function list(): array { return $this->h->request('GET', 'balances'); }
}

final class Wallets extends Resource
{
    public function list(): array { return $this->h->request('GET', 'wallets'); }
    public function create(array $body = []): array { return $this->h->request('POST', 'wallets', body: (object) $body); }
    public function retrieve(string $id): array { return $this->h->request('GET', 'wallets/' . self::seg($id)); }
    public function balance(string $id): array { return $this->h->request('GET', 'wallets/' . self::seg($id) . '/balance'); }
    /** @param array{limit?: int} $params */
    public function transactions(string $id, array $params = []): array { return $this->h->request('GET', 'wallets/' . self::seg($id) . '/transactions', $params); }
}

final class Transactions extends Resource
{
    /** @param array{limit?: int, before?: string} $params */
    public function list(array $params = []): array { return $this->h->request('GET', 'transactions', $params); }
    public function retrieve(string $id): array { return $this->h->request('GET', 'transactions/' . self::seg($id)); }
    /** @param array{limit?: int, before?: string, from?: string, to?: string, currency?: string, kind?: string} $params */
    public function statement(array $params = []): array { return $this->h->request('GET', 'statement', $params); }
}

/** PIX cash-in: a charge the payer pays with a QR code / copia-e-cola. */
final class PixPayins extends Resource
{
    public function create(array $body, string $idempotencyKey): array
    {
        return $this->h->request('POST', 'pix/payins', body: $body, idempotencyKey: self::requireKey($idempotencyKey));
    }
    public function retrieve(string $id): array { return $this->h->request('GET', 'pix/payins/' . self::seg($id)); }
    /** Sandbox only: settle or fail a pending charge. */
    public function simulate(string $id, array $body = ['status' => 'completed']): array
    {
        return $this->h->request('POST', 'pix/payins/' . self::seg($id) . '/simulate', body: $body);
    }
}

/** PIX cash-out. Over the API the workspace's registered settlement key receives the money (403 settlement_key_required until set). */
final class PixPayouts extends Resource
{
    public function create(array $body, string $idempotencyKey): array
    {
        return $this->h->request('POST', 'pix/payouts', body: $body, idempotencyKey: self::requireKey($idempotencyKey));
    }
    public function retrieve(string $id): array { return $this->h->request('GET', 'pix/payouts/' . self::seg($id)); }
    /** Fee and total debit before sending. */
    public function fee(array $body): array { return $this->h->request('POST', 'pix/payouts/fee', body: $body); }
    /** Resolve a PIX key to its holder (masked) before paying. */
    public function lookup(array $body): array { return $this->h->request('POST', 'pix/payouts/dict', body: $body); }
    /** Sandbox only. */
    public function simulate(string $id, array $body = ['status' => 'completed']): array
    {
        return $this->h->request('POST', 'pix/payouts/' . self::seg($id) . '/simulate', body: $body);
    }
    public function listAccounts(): array { return $this->h->request('GET', 'pix/payout-accounts'); }
}

final class Pix
{
    public readonly PixPayins $payins;
    public readonly PixPayouts $payouts;

    public function __construct(Http $h)
    {
        $this->payins = new PixPayins($h);
        $this->payouts = new PixPayouts($h);
    }
}

/** Quote then convert. Direction is always base_currency -> quote_currency. */
final class Conversions extends Resource
{
    public function quote(array $body): array { return $this->h->request('POST', 'rfq/quote', body: $body); }
    /**
     * A quote converts once: a second execute is 409 quote_not_open, an old one 410 quote_expired.
     * Never retried automatically; after a lost reply, read the payment with transactions->list().
     */
    public function execute(string $quoteId): array { return $this->h->request('POST', 'rfq/execute', body: ['quote_id' => $quoteId]); }
    /** @param list<string>|null $symbols */
    public function prices(?array $symbols = null): array
    {
        return $this->h->request('GET', 'prices', $symbols === null ? [] : ['symbols' => implode(',', $symbols)]);
    }
}

final class Trading extends Resource
{
    public function summary(): array { return $this->h->request('GET', 'trading/summary'); }
    /** BRL `available` -> `trading`. Conversions spend BRL from `trading`. */
    public function deposit(array $body, string $idempotencyKey): array
    {
        return $this->h->request('POST', 'trading/deposits', body: $body, idempotencyKey: self::requireKey($idempotencyKey));
    }
    /** BRL `trading` -> `available`. */
    public function withdraw(array $body, string $idempotencyKey): array
    {
        return $this->h->request('POST', 'trading/withdrawals', body: $body, idempotencyKey: self::requireKey($idempotencyKey));
    }
    /** USDT/USDC `trading` -> your own wallet on `network`. Body: amount_minor, currency?, network?. */
    public function cryptoWithdraw(array $body, string $idempotencyKey): array
    {
        return $this->h->request('POST', 'trading/crypto-withdrawals', body: $body, idempotencyKey: self::requireKey($idempotencyKey));
    }
}

final class Crypto extends Resource
{
    public function send(array $body, string $idempotencyKey): array
    {
        return $this->h->request('POST', 'crypto/send', body: $body, idempotencyKey: self::requireKey($idempotencyKey));
    }
    /** Sandbox only. */
    public function simulate(string $id, array $body = ['status' => 'completed']): array
    {
        return $this->h->request('POST', 'crypto/send/' . self::seg($id) . '/simulate', body: $body);
    }
    public function networks(): array { return $this->h->request('GET', 'crypto/networks'); }
}

final class Customers extends Resource
{
    public function list(): array { return $this->h->request('GET', 'customers'); }
    public function create(array $body): array { return $this->h->request('POST', 'customers', body: $body); }
    public function retrieve(string $id): array { return $this->h->request('GET', 'customers/' . self::seg($id)); }
    public function update(string $id, array $body): array { return $this->h->request('PATCH', 'customers/' . self::seg($id), body: $body); }
}

final class Kyc extends Resource
{
    public function list(): array { return $this->h->request('GET', 'kyc'); }
    public function create(array $body): array { return $this->h->request('POST', 'kyc', body: $body); }
    public function retrieve(string $id): array { return $this->h->request('GET', 'kyc/' . self::seg($id)); }
    public function listRfi(string $id): array { return $this->h->request('GET', 'kyc/' . self::seg($id) . '/rfi'); }
    public function answerRfi(string $id, array $body): array { return $this->h->request('POST', 'kyc/' . self::seg($id) . '/rfi', body: $body); }
}

final class Whitelist extends Resource
{
    public function list(): array { return $this->h->request('GET', 'whitelist'); }
    public function settings(): array { return $this->h->request('GET', 'whitelist/settings'); }
}

final class BankAccounts extends Resource
{
    public function list(): array { return $this->h->request('GET', 'bank-accounts'); }
}

final class Webhooks extends Resource
{
    public function list(): array { return $this->h->request('GET', 'webhooks'); }
    /** The signing `secret` is in this reply only. Store it. */
    public function create(array $body): array { return $this->h->request('POST', 'webhooks', body: $body); }
    public function retrieve(string $id): array { return $this->h->request('GET', 'webhooks/' . self::seg($id)); }
    public function update(string $id, array $body): array { return $this->h->request('PATCH', 'webhooks/' . self::seg($id), body: $body); }
    public function delete(string $id): array { return $this->h->request('DELETE', 'webhooks/' . self::seg($id)); }
    /** Sandbox key only: queues a signed test.ping to the endpoint. */
    public function test(string $id): array { return $this->h->request('POST', 'webhooks/' . self::seg($id) . '/test'); }
    public function deliveries(): array { return $this->h->request('GET', 'webhooks/deliveries'); }
    /** Sandbox key only. */
    public function resend(string $deliveryId): array { return $this->h->request('POST', 'webhooks/deliveries/' . self::seg($deliveryId) . '/resend'); }
}

final class PaymentLinks extends Resource
{
    public function list(): array { return $this->h->request('GET', 'payment-links'); }
    public function create(array $body): array { return $this->h->request('POST', 'payment-links', body: $body); }
    public function retrieve(string $id): array { return $this->h->request('GET', 'payment-links/' . self::seg($id)); }
    public function update(string $id, array $body): array { return $this->h->request('PATCH', 'payment-links/' . self::seg($id), body: $body); }
    public function delete(string $id): array { return $this->h->request('DELETE', 'payment-links/' . self::seg($id)); }
    /** Paginated `{items, next_cursor}`: pass next_cursor back as `cursor`. */
    public function payments(array $params = []): array { return $this->h->request('GET', 'payment-links/payments', $params); }
    public function stats(array $params = []): array { return $this->h->request('GET', 'payment-links/stats', $params); }
    public function splits(string $id): array { return $this->h->request('GET', 'payment-links/' . self::seg($id) . '/splits'); }
    public function replaceSplits(string $id, array $body): array { return $this->h->request('PUT', 'payment-links/' . self::seg($id) . '/splits', body: $body); }
    public function charge(string $id, array $body, string $idempotencyKey): array
    {
        return $this->h->request('POST', 'payment-links/' . self::seg($id) . '/charges', body: $body, idempotencyKey: self::requireKey($idempotencyKey));
    }
}

/** The public checkout page of a payment link. */
final class Checkout extends Resource
{
    public function retrieve(string $slug): array { return $this->h->request('GET', 'checkout/' . self::seg($slug)); }
    public function pay(string $slug, array $body, string $idempotencyKey): array
    {
        return $this->h->request('POST', 'checkout/' . self::seg($slug) . '/pay', body: $body, idempotencyKey: self::requireKey($idempotencyKey));
    }
}

final class SubAccounts extends Resource
{
    public function list(): array { return $this->h->request('GET', 'subaccounts'); }
    public function create(array $body): array { return $this->h->request('POST', 'subaccounts', body: $body); }
    public function retrieve(string $id): array { return $this->h->request('GET', 'subaccounts/' . self::seg($id)); }
    public function balances(string $id): array { return $this->h->request('GET', 'subaccounts/' . self::seg($id) . '/balances'); }
    public function transactions(string $id, array $params = []): array { return $this->h->request('GET', 'subaccounts/' . self::seg($id) . '/transactions', $params); }
}

final class ApiKeys extends Resource
{
    public function list(): array { return $this->h->request('GET', 'api-keys'); }
    public function audit(array $params = []): array { return $this->h->request('GET', 'api-keys/audit', $params); }
}

final class Settings extends Resource
{
    public function retrieve(): array { return $this->h->request('GET', 'settings'); }
}

final class Notifications extends Resource
{
    public function list(): array { return $this->h->request('GET', 'notifications'); }
}

final class Team extends Resource
{
    public function list(): array { return $this->h->request('GET', 'team'); }
}

final class Workflows extends Resource
{
    public function list(): array { return $this->h->request('GET', 'workflows'); }
    /** @param array{name: string, enabled?: bool, trigger?: string, steps?: list<mixed>, definition?: array} $body */
    public function create(array $body): array { return $this->h->request('POST', 'workflows', body: $body); }
    public function runs(): array { return $this->h->request('GET', 'workflows/runs'); }
}

final class Approvals extends Resource
{
    public function list(): array { return $this->h->request('GET', 'approvals'); }
    public function policy(): array { return $this->h->request('GET', 'approval-policy'); }
}

final class Treasury extends Resource
{
    public function list(): array { return $this->h->request('GET', 'treasury'); }
    /** @param array{address: string, network?: string, label?: string} $body */
    public function create(array $body): array { return $this->h->request('POST', 'treasury', body: $body); }
}
