# clarian-finance/sdk-php

Official PHP SDK for the [Clarian Finance API](https://api.clarian.finance). PIX cash-in/cash-out, balances, wallets, transactions, and webhooks.

Package: `clarian-finance/sdk-php` · namespace `Clarian\` · **no third-party runtime dependencies** (curl + json extensions only). Requires PHP 8.1+.

## Install

Until the package is published on Packagist, install from this monorepo with a Composer [path repository](https://getcomposer.org/doc/05-repositories.md#path). Composer VCS repositories do not support Git subdirectories, so a path (or path-style) install is required.

```bash
# clone the monorepo (or use a sibling checkout)
git clone https://github.com/clarian-finance/sdk.git

# in your app composer.json:
{
  "repositories": [
    {
      "type": "path",
      "url": "../sdk/php"
    }
  ],
  "require": {
    "clarian-finance/sdk-php": "0.4.0"
  }
}
```

Then:

```bash
composer update clarian-finance/sdk-php
```

Adjust the path so it points at the `php/` directory of your local clone.

**Plan:** once published on Packagist as `clarian-finance/sdk-php`, install with:

```bash
composer require clarian-finance/sdk-php
```

## Quickstart

```php
<?php

use Clarian\Client;

$client = new Client(
    getenv('CLARIAN_API_KEY'),       // cl_live_sk_... or cl_test_sk_...
    getenv('CLARIAN_WORKSPACE_ID'),  // workspace UUID
);
// cl_test_sk_ -> sandbox; cl_live_sk_ (and any other prefix) -> production/live.
// Override with baseUrl: new Client($key, $ws, 'https://...');
// Timeout seconds: new Client($key, $ws, null, 60.0);

$client->ping();

// Receive BRL via PIX (Idempotency-Key is sent automatically).
$charge = $client->cashIn->create([
    'amount' => 250.00,
    'payer' => [
        'name' => 'Maria Silva',
        'document_number' => '12345678900',
    ],
    'description' => 'Order #1234',
], 'order-1234');
echo $charge['order']['pix']['copy_paste'], "\n";

// Send BRL via PIX (idempotency key required; retries never double-send).
$payout = $client->cashOut->create(
    ['amount' => 100.00],
    'withdrawal-2026-07-01-001',
);
echo $payout['order']['id'], ' ', $payout['order']['status'], "\n";

// Optional: preview a PIX key owner before paying out.
$info = $client->cashOut->dictCheck('maria@example.com', 'EMAIL');

$balances = $client->balances->list();
```

Auth headers (set automatically): `Authorization: Bearer <key>` and `X-Workspace-Id: <uuid>`.

## Environments

| Key prefix       | Base URL                                                    |
|------------------|-------------------------------------------------------------|
| `cl_test_sk_…`   | `https://api.clarian.finance/functions/v1/api-gateway/test` |
| `cl_live_sk_…`   | `https://api.clarian.finance/functions/v1/api-gateway/live` |
| any other prefix | **live** (same as `cl_live_sk_…`); not rejected             |

Only the `cl_test_sk_` prefix selects sandbox. Unknown prefixes default to live so a mis-typed key can hit production; pass `$baseUrl` when you need an explicit host.

## Error handling

Non-2xx responses throw `Clarian\ClarianException` with `status`, `code` (JSON `error` when present), `message` (detail / code / truncated body), and `meta`. The exception string (`getMessage()`) truncates the raw body to 500 characters.

```php
use Clarian\ClarianException;

try {
    $client->cashOut->create(['amount' => 100.00], 'payout-inv-42');
} catch (ClarianException $e) {
    echo $e->status, ' ', $e->code, ' ', $e->message, "\n";
    // $e->meta, $e->body (full untruncated body)
}
```

## Verifying webhooks

Deliveries are signed with **HMAC-SHA256** (hex) over `` `{X-Clarian-Timestamp}.{rawBody}` `` using the full `whsec_…` secret (prefix included; do not strip it). Reject timestamps older than **5 minutes** (replay protection).

Headers on each delivery:

| Header                      | Purpose                        |
|-----------------------------|--------------------------------|
| `X-Clarian-Signature`       | HMAC-SHA256 hex digest         |
| `X-Clarian-Timestamp`       | ISO 8601 / RFC3339             |
| `X-Clarian-Event`           | Event type                     |
| `X-Clarian-Delivery-Id`     | Unique delivery id             |
| `X-Clarian-Idempotency-Key` | Dedupe key for the delivery    |
| `X-Clarian-Attempt`         | Retry attempt number           |

```php
use Clarian\Webhooks;

$secret = getenv('CLARIAN_WEBHOOK_SECRET'); // whsec_...
$rawBody = file_get_contents('php://input');
$headers = Webhooks::extractHeaders(getallheaders() ?: $_SERVER);

if (!Webhooks::verifySignature(
    $rawBody,
    $headers['timestamp'],
    $headers['signature'],
    $secret,
)) {
    http_response_code(401);
    exit('invalid signature');
}

// process json_decode($rawBody, true) ...
```

For local handler tests, sign a payload the same way the server does:

```php
$sig = Webhooks::signPayload($secret, $timestamp, $rawBody);
```

Create a subscription (secret shown **once**):

```php
$sub = $client->webhooks->create([
    'url' => 'https://example.com/webhooks/clarian', // must be public https
    'events' => ['pix_payin.completed', 'pix_payout.completed'],
]);
// store $sub['secret'] immediately; it is never returned again
echo $sub['id'], ' ', $sub['secret'], "\n";
```

## Sandbox testing

Sandbox helpers live on `$client->sandbox`. They require a `cl_test_sk_…` key and throw `\LogicException` for live keys **before** any HTTP call. Outside the test environment the gateway returns `404 {error:"sandbox_only"}`.

```php
use Clarian\Sandbox;

// Advance a pending cash-in (empty/null status -> completed).
$order = $client->sandbox->simulateCashIn($charge['order']['id'], 'completed');
// status: "completed" | "expired" | "failed"

// Advance a cash-out: "completed" | "failed"
$client->sandbox->simulateCashOut($payout['order']['id'], 'failed');

// Enqueue a sample webhook for a subscription.
$result = $client->sandbox->sendWebhookEvent(
    $sub['id'],
    Sandbox::EVENT_PIX_PAYIN_COMPLETED,
);
$newId = $client->sandbox->resendWebhookDelivery($result['delivery_id']);
```

### Magic PIX keys

| Constant / key                      | Sandbox payout behavior                                  |
|-------------------------------------|----------------------------------------------------------|
| `Sandbox::SANDBOX_FAIL_PIX_KEY`     | `fail@sandbox.clarian` (fails + refund)                  |
| `Sandbox::SANDBOX_PENDING_PIX_KEY`  | `pending@sandbox.clarian` (stays pending until simulated)|

```php
$client->cashOut->create([
    'amount' => 10.00,
    'pix_key' => Sandbox::SANDBOX_FAIL_PIX_KEY,
], 'payout-fail-1');
```

Sample event type constants: `EVENT_PIX_PAYIN_CREATED`, `EVENT_PIX_PAYIN_COMPLETED`, `EVENT_PIX_PAYIN_EXPIRED`, `EVENT_PIX_PAYOUT_CREATED`, `EVENT_PIX_PAYOUT_COMPLETED`, `EVENT_PIX_PAYOUT_FAILED`, `EVENT_CHECKOUT_PAID`.

## API surface

| Method | Path | Client |
|--------|------|--------|
| `GET` | `/ping` | `$client->ping()` |
| `POST` | `/cash-in/pix` | `$client->cashIn->create($params, $idempotencyKey)` |
| `GET` | `/cash-in/{id}` | `$client->cashIn->retrieve($id)` |
| `POST` | `/pix/payouts/dict` | `$client->cashOut->dictCheck($pixKey, $keyType)` |
| `POST` | `/cash-out/pix` | `$client->cashOut->create($params, $idempotencyKey)` |
| `GET` | `/cash-out/{id}` | `$client->cashOut->retrieve($id)` |
| `GET` | `/account/balances` | `$client->balances->list()` |
| `GET` | `/transactions` | `$client->transactions->list(...)` |
| `GET` | `/transactions/{id}` | `$client->transactions->retrieve($id)` |
| `GET` | `/wallets` | `$client->wallets->list($network)` |
| `GET` | `/wallets/{id}/balance` | `$client->wallets->retrieveBalance($id)` |
| | webhooks CRUD | `$client->webhooks->create/list/update/delete` |
| | verify delivery | `Webhooks::verifySignature` / `extractHeaders` / `signPayload` |
| `POST` | `/pix/payins/{id}/simulate` | `$client->sandbox->simulateCashIn($id, $status)` |
| `POST` | `/pix/payouts/{id}/simulate` | `$client->sandbox->simulateCashOut($id, $status)` |
| `POST` | `/webhooks/{id}/test` | `$client->sandbox->sendWebhookEvent($id, $eventType)` |
| `POST` | `/webhooks/deliveries/{id}/resend` | `$client->sandbox->resendWebhookDelivery($id)` |

## Development

```bash
cd php
composer install
vendor/bin/phpunit tests
```
