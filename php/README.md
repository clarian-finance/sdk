# clarian-finance/sdk-php

PHP SDK for the Clarian API v2: PIX cash-in and cash-out, BRL/stablecoin quotes and conversion, wallets and crypto sends, customers and KYC, payment links, sub-accounts and webhooks. PHP 8.1+, `ext-curl` and `ext-json`, no other runtime dependencies.

## Install

```bash
composer config repositories.clarian vcs https://github.com/clarian-finance/sdk
composer require clarian-finance/sdk-php:^0.5
```

## Quick start

```php
use Clarian\Clarian;

$clarian = new Clarian(
    apiKey: getenv('CLARIAN_API_KEY'),           // cl_test_sk_... (sandbox) or cl_live_sk_... (live)
    workspaceId: getenv('CLARIAN_WORKSPACE_ID'),
);

$clarian->ping();
$balances = $clarian->balances->list();
```

The key prefix picks the environment: `cl_test_sk_` calls `/v1/test`, `cl_live_sk_` calls `/v1/live`. Create keys in the dashboard under Negócios > API (needs 2FA). Give each key only the permissions it uses. Pass `baseUrl:` to point at another origin, `timeout:` (seconds) and `maxRetries:` to tune requests.

Request bodies and replies are associative arrays shaped like the schemas in `openapi.json`.

## Amounts

Every amount is an integer in minor units, sent and returned as a string. BRL has scale 2 (R$ 10,00 is `"1000"`), USDT and USDC scale 6 (1 USDT is `"1000000"`). Pass a `string`, `int`, `GMP` or integer `BcMath\Number`; a float in a `*_minor` field throws `InvalidArgumentException`. Balances carry their `scale`.

```php
$brl = array_values(array_filter($balances, fn ($b) => $b['currency'] === 'BRL' && $b['kind'] === 'available'))[0];
// $brl['balance_minor'] = "1250050", $brl['scale'] = 2 -> R$ 12.500,50
```

## Cash-in (receive PIX)

```php
$charge = $clarian->pix->payins->create(['amount_minor' => '25000', 'currency' => 'BRL'], 'order-981');
$charge['pix_qr_code']; // copia-e-cola for the payer
// later, or from the webhook:
$paid = $clarian->pix->payins->retrieve($charge['id']);
```

The QR is valid for 30 minutes. The balance moves from `pending` to `available` when the PIX settles.

## Cash-out (send PIX)

```php
$fee = $clarian->pix->payouts->fee(['amount_minor' => '10000']); // fee_minor, debit_minor
$payout = $clarian->pix->payouts->create(
    ['amount_minor' => '10000', 'currency' => 'BRL', 'pix_key' => 'settlement@yourcompany.com'],
    'withdrawal-2026-10-10-001',
);
```

Over the API, the money goes to your workspace's registered settlement key. Register it once in the dashboard; until then the call answers 403 `settlement_key_required`. Amount plus fee leave `available` at once and return if the PIX fails.

## Quote and convert

The direction is always `base_currency` -> `quote_currency`: you pay `base_amount_minor` of the base and receive the quote currency.

```php
// Buy USDT with R$ 500,00
$quote = $clarian->conversions->quote(['base_currency' => 'BRL', 'quote_currency' => 'USDT', 'base_amount_minor' => '50000']);
$quote['quote_amount_minor']; // USDT you receive (scale 6), after fees
$quote['expires_at'];         // execute before this

$result = $clarian->conversions->execute($quote['id']);
$result['ledger']['to_minor'] ?? null; // settled amount; absent while $result['status'] === 'processing'
```

A quote converts once: a second execute is 409 `quote_not_open`, an expired one 410 `quote_expired`. To sell USDT for BRL, swap the currencies.

## Wallets and crypto

```php
$wallets = $clarian->wallets->list();
$clarian->wallets->balance($wallets[0]['id']);
$clarian->crypto->networks();
// API keys send only to wallets registered in the dashboard whitelist (403 destination_not_whitelisted).
$send = $clarian->crypto->send(['amount_minor' => '5000000', 'currency' => 'USDT', 'network' => 'polygon', 'to_address' => '0x...'], 'send-17');
```

## Customers, KYC and sub-accounts

```php
$customer = $clarian->customers->create(['kind' => 'person', 'display_name' => 'Maria Silva', 'document_type' => 'cpf', 'document_number' => '52998224725']);
$clarian->kyc->create(['party_id' => $customer['id']]);

$sub = $clarian->subaccounts->create(['display_name' => 'Store 12']);
$store = $clarian->forSubaccount($sub['id']); // every call acts on that sub-account
$store->pix->payins->create(['amount_minor' => '1000'], 'store12-order-1');
```

## Payment links

```php
$link = $clarian->paymentLinks->create(['amount_minor' => '4990', 'currency' => 'BRL', 'description' => 'Plano mensal']);
$page = $clarian->paymentLinks->payments(['limit' => 50]); // ['items' => [...], 'next_cursor' => ...]
```

## Webhooks

```php
$hook = $clarian->webhooks->create(['url' => 'https://example.com/clarian', 'events' => ['payment.completed', 'payment.failed']]);
$hook['secret']; // shown only here: store it
$clarian->webhooks->test($hook['id']); // sandbox key: sends a signed test.ping
```

Verify every delivery on the raw body before trusting it:

```php
use Clarian\Webhook;

try {
    $event = Webhook::constructEvent(file_get_contents('php://input'), getallheaders(), getenv('CLARIAN_WEBHOOK_SECRET'));
} catch (UnexpectedValueException $e) {
    http_response_code(400);
    exit;
}
// PSR-7: Webhook::constructEvent((string) $request->getBody(), $request->getHeaders(), $secret)

if ($event['type'] === 'payment.completed') {
    ['payment_id' => $paymentId, 'kind' => $kind, 'amount_minor' => $amount, 'currency' => $currency] = $event['data'];
}
```

The signature is HMAC-SHA256 of `<x-clarian-timestamp>.<rawBody>`; deliveries older than 5 minutes, or with an unparseable timestamp, are refused. Dedupe on `$event['id']` and answer 2xx fast. Failed deliveries are listed in `webhooks->deliveries()`. `webhooks->test()` and `webhooks->resend($id)` work with a sandbox key only; in live, reconcile missed events with `transactions->list()`.

## Sandbox

With a `cl_test_sk_` key nothing reaches a bank or chain. Settle or fail anything pending:

```php
$clarian->pix->payins->simulate($charge['id']);                                  // completed
$clarian->pix->payouts->simulate($payout['id'], ['status' => 'failed', 'reason' => 'test']);
$clarian->crypto->simulate($send['id']);
```

## Errors, retries and idempotency

Non-2xx replies throw `Clarian\ClarianError` with `status`, the API's stable `errorCode` and the remaining fields in `meta`:

```php
use Clarian\ClarianError;

try {
    $clarian->pix->payouts->create(['amount_minor' => '10000', 'pix_key' => 'x'], 'w-1');
} catch (ClarianError $e) {
    if ($e->errorCode === 'insufficient_balance') { /* ... */ }
}
```

Money-moving creates (`pix->payins->create`, `pix->payouts->create`, `crypto->send`, `paymentLinks->charge`, `checkout->pay`) require an idempotency key; an empty one throws before any request. Retrying with the same key returns the same payment and never pays twice. The SDK retries 429/502/503/504 and network errors up to `maxRetries` (default 2), honoring `Retry-After`, but only reads, deletes and calls that carry an idempotency key. `conversions->execute` is never retried. The API allows 120 requests per minute per key.

## Not on the API

Actions that need a person's own second factor stay in the dashboard: inviting or changing team members, voting on approvals, creating or rotating API keys, saving payout accounts and whitelist entries, and changing workspace settings. Their read endpoints are in the SDK.

## Reference

| Area | Methods |
|---|---|
| `balances` | `list` |
| `pix->payins` | `create`, `retrieve`, `simulate` |
| `pix->payouts` | `create`, `retrieve`, `fee`, `lookup`, `simulate`, `listAccounts` |
| `conversions` | `quote`, `execute`, `prices` |
| `wallets` | `list`, `create`, `retrieve`, `balance`, `transactions` |
| `crypto` | `send`, `simulate`, `networks` |
| `transactions` | `list`, `retrieve`, `statement` |
| `customers` | `list`, `create`, `retrieve`, `update` |
| `kyc` | `list`, `create`, `retrieve`, `listRfi`, `answerRfi` |
| `webhooks` | `list`, `create`, `retrieve`, `update`, `delete`, `test`, `deliveries`, `resend` |
| `paymentLinks` | `list`, `create`, `retrieve`, `update`, `delete`, `payments`, `stats`, `splits`, `replaceSplits`, `charge` |
| `checkout` | `retrieve`, `pay` |
| `subaccounts` | `list`, `create`, `retrieve`, `balances`, `transactions` |
| `whitelist`, `bankAccounts`, `apiKeys`, `settings`, `notifications`, `team`, `approvals` | read |
| `workflows`, `treasury` | `list`, `create` (+ `workflows->runs`) |

Field names follow the OpenAPI reference (`../openapi.json`, also at `https://app.clarian.finance/openapi.json`).

## Development

```bash
composer install
vendor/bin/phpunit
CLARIAN_API_KEY=cl_test_sk_... CLARIAN_WORKSPACE_ID=... CLARIAN_BASE_URL=http://localhost:3000 php e2e/sandbox.php
```
