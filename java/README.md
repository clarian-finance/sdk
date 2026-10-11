# clarian-sdk (Java)

Java SDK for the Clarian API v2: PIX cash-in and cash-out, BRL/stablecoin quotes and conversion, wallets and crypto sends, customers and KYC, payment links, sub-accounts and webhooks. Standard library only (no runtime dependencies). Java 17+.

## Install

From this repo:

```bash
cd java
gradle publishToMavenLocal
```

```kotlin
// build.gradle.kts
repositories { mavenLocal() }
dependencies { implementation("finance.clarian:clarian-sdk:1.0.0") }
```

## Quick start

```java
import finance.clarian.sdk.ClarianClient;

ClarianClient clarian = ClarianClient.builder()
        .apiKey(System.getenv("CLARIAN_API_KEY"))        // cl_test_sk_… (sandbox) or cl_live_sk_… (live)
        .workspaceId(System.getenv("CLARIAN_WORKSPACE_ID"))
        .build();

clarian.ping();
List<Map<String, Object>> balances = clarian.balances.list();
```

The key prefix picks the environment: `cl_test_sk_` calls `/v1/test`, `cl_live_sk_` calls `/v1/live`. Override the origin with `.baseUrl("http://localhost:3000")`. Create keys in the dashboard under Negócios > API (needs 2FA). Give each key only the permissions it uses.

Requests take a `Map` with the field names of the OpenAPI reference; objects come back as `Map<String, Object>` and lists as `List<Map<String, Object>>`.

## Amounts

Every amount is an integer in minor units, sent and returned as a string. BRL has scale 2 (R$ 10,00 is `"1000"`), USDT and USDC scale 6 (1 USDT is `"1000000"`). Pass a `String`, `int`/`long` or `BigInteger`; a `double`, `float` or `BigDecimal` is rejected before the request is sent. Balances carry their `scale`.

```java
Map<String, Object> brl = balances.stream()
        .filter(b -> "BRL".equals(b.get("currency")) && "available".equals(b.get("kind")))
        .findFirst().orElseThrow();
new BigInteger((String) brl.get("balance_minor")); // 1250050, scale 2 → R$ 12.500,50
```

## Cash-in (receive PIX)

```java
var charge = clarian.pix.payins.create(Map.of("amount_minor", "25000", "currency", "BRL"), "order-981");
charge.get("pix_qr_code"); // copia-e-cola for the payer
// later, or from the webhook:
var paid = clarian.pix.payins.retrieve((String) charge.get("id"));
```

The QR is valid for 30 minutes. The balance moves from `pending` to `available` when the PIX settles.

## Cash-out (send PIX)

```java
var fee = clarian.pix.payouts.fee(Map.of("amount_minor", "10000")); // fee_minor, debit_minor
var payout = clarian.pix.payouts.create(
        Map.of("amount_minor", "10000", "currency", "BRL", "pix_key", "settlement@yourcompany.com"),
        "withdrawal-2026-10-10-001");
```

Over the API, the money goes to your workspace's registered settlement key. Register it once in the dashboard; until then the call answers 403 `settlement_key_required`. Amount plus fee leave `available` at once and return if the PIX fails.

## Quote and convert

The direction is always `base_currency` → `quote_currency`: you pay `base_amount_minor` of the base and receive the quote currency.

```java
// Buy USDT with R$ 500,00
var quote = clarian.conversions.quote(Map.of("base_currency", "BRL", "quote_currency", "USDT", "base_amount_minor", "50000"));
quote.get("quote_amount_minor"); // USDT you receive (scale 6), after fees
quote.get("expires_at");         // execute before this

var result = clarian.conversions.execute((String) quote.get("id"));
result.get("ledger");            // settled amounts; absent while status is "processing"
```

A quote converts once: a second execute is 409 `quote_not_open`, an expired one 410 `quote_expired`. To sell USDT for BRL, swap the currencies.

## Wallets and crypto

```java
var wallets = clarian.wallets.list();
clarian.wallets.balance((String) wallets.get(0).get("id"));
clarian.crypto.networks();
// API keys send only to wallets registered in the dashboard whitelist (403 destination_not_whitelisted).
clarian.crypto.send(Map.of("amount_minor", "5000000", "currency", "USDT", "network", "polygon", "to_address", "0x…"), "send-17");
```

## Customers, KYC and sub-accounts

```java
var customer = clarian.customers.create(Map.of("kind", "person", "display_name", "Maria Silva",
        "document_type", "cpf", "document_number", "52998224725"));
clarian.kyc.create(Map.of("party_id", customer.get("id")));

var sub = clarian.subaccounts.create(Map.of("display_name", "Store 12"));
ClarianClient store = clarian.forSubaccount((String) sub.get("id")); // every call acts on that sub-account
store.pix.payins.create(Map.of("amount_minor", "1000"), "store12-order-1");
```

## Payment links

```java
var link = clarian.paymentLinks.create(Map.of("amount_minor", "4990", "currency", "BRL", "description", "Plano mensal"));
var page = clarian.paymentLinks.payments(Map.of("limit", 50)); // {items, next_cursor}
```

## Webhooks

```java
var hook = clarian.webhooks.create(Map.of("url", "https://example.com/clarian",
        "events", List.of("payment.completed", "payment.failed")));
hook.get("secret"); // shown only here: store it
clarian.webhooks.test((String) hook.get("id")); // sandbox key: sends a signed test.ping
```

Verify every delivery on the raw body before trusting it:

```java
import finance.clarian.sdk.Webhooks;

// rawBody: the exact request body as received; headers: name → value (case-insensitive)
Webhooks.Event event = Webhooks.constructEvent(rawBody, headers, System.getenv("CLARIAN_WEBHOOK_SECRET"));

if (event.type().equals("payment.completed")) {
    Object paymentId = event.data().get("payment_id");
}
```

The signature is HMAC-SHA256 of `<x-clarian-timestamp>.<rawBody>`; deliveries older than 5 minutes or with an unparseable timestamp are refused (`IllegalArgumentException`). Dedupe on `event.id()` and answer 2xx fast. Failed deliveries are listed in `webhooks.deliveries()`. `webhooks.test()` and `webhooks.resend(id)` work with a sandbox key only; in live, reconcile missed events with `transactions.list()`.

## Sandbox

With a `cl_test_sk_` key nothing reaches a bank or chain. Settle or fail anything pending:

```java
clarian.pix.payins.simulate(chargeId);                                          // completed
clarian.pix.payouts.simulate(payoutId, Map.of("status", "failed", "reason", "test"));
clarian.crypto.simulate(sendId);
```

## Errors, retries and idempotency

Non-2xx replies throw `ClarianException` (unchecked) with `status()` and the API's stable `code()`:

```java
try {
    clarian.pix.payouts.create(Map.of("amount_minor", "10000", "pix_key", "x"), "w-1");
} catch (ClarianException e) {
    if (e.code().equals("insufficient_balance")) { /* … */ }
}
```

Money-moving creates (`pix.payins.create`, `pix.payouts.create`, `crypto.send`, `paymentLinks.charge`, `checkout.pay`) require an idempotency key: a missing one throws `IllegalArgumentException` before any request, and retrying with the same key returns the same payment and never pays twice. The SDK retries 429/502/503/504 and network errors up to `maxRetries` (default 2) and honors `Retry-After`, but only for reads, deletes and calls that carry an idempotency key; `conversions.execute` is never retried. The API allows 120 requests per minute per key.

## Not on the API

Actions that need a person's own second factor stay in the dashboard: inviting or changing team members, voting on approvals, creating or rotating API keys, saving payout accounts and whitelist entries, and changing workspace settings. Their read endpoints are in the SDK.

## Reference

| Area | Methods |
|---|---|
| `balances` | `list` |
| `pix.payins` | `create`, `retrieve`, `simulate` |
| `pix.payouts` | `create`, `retrieve`, `fee`, `lookup`, `simulate`, `listAccounts` |
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
| `workflows`, `treasury` | `list`, `create` (+ `workflows.runs`) |

Field names follow the OpenAPI reference (`../openapi.json`, also at `https://app.clarian.finance/openapi.json`).

## Development

```bash
gradle test
CLARIAN_API_KEY=cl_test_sk_… CLARIAN_WORKSPACE_ID=… CLARIAN_BASE_URL=http://localhost:3000 gradle e2e
```
