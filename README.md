# @clarian-finance/sdk

TypeScript SDK for the Clarian API v2: PIX cash-in and cash-out, BRL/stablecoin quotes and conversion, wallets and crypto sends, customers and KYC, payment links, sub-accounts and webhooks. Zero runtime dependencies. Node 18+, Bun and Deno. Server-side only: an API key is a secret, so never ship it to a browser or mobile app.

[Leia em português](./README.pt-BR.md)

## Install

```bash
npm install github:clarian-finance/sdk#v0.5.0
```

## Quick start

```ts
import { Clarian } from "@clarian-finance/sdk";

const clarian = new Clarian({
  apiKey: process.env.CLARIAN_API_KEY!, // cl_test_sk_… (sandbox) or cl_live_sk_… (live)
  workspaceId: process.env.CLARIAN_WORKSPACE_ID!,
});

await clarian.ping();
const balances = await clarian.balances.list();
```

The key prefix picks the environment: `cl_test_sk_` calls `/v1/test`, `cl_live_sk_` calls `/v1/live`. Create keys in the dashboard under Negócios > API (needs 2FA). Give each key only the permissions it uses.

## Amounts

Every amount is an integer in minor units, sent and returned as a string. BRL has scale 2 (R$ 10,00 is `"1000"`), USDT and USDC scale 6 (1 USDT is `"1000000"`). Pass a `string`, `number` or `bigint`; never a float. Balances carry their `scale`.

```ts
const brl = balances.find((b) => b.currency === "BRL" && b.kind === "available");
// brl.balance_minor = "1250050", brl.scale = 2 → R$ 12.500,50
```

## Cash-in (receive PIX)

```ts
const charge = await clarian.pix.payins.create({ amount_minor: "25000", currency: "BRL" }, "order-981");
charge.pix_qr_code; // copia-e-cola for the payer
// later, or from the webhook:
const paid = await clarian.pix.payins.retrieve(charge.id!);
```

The QR is valid for 30 minutes. The balance moves from `pending` to `available` when the PIX settles.

## Cash-out (send PIX)

```ts
const fee = await clarian.pix.payouts.fee({ amount_minor: "10000" }); // fee_minor, debit_minor
const payout = await clarian.pix.payouts.create(
  { amount_minor: "10000", currency: "BRL", pix_key: "settlement@yourcompany.com" },
  "withdrawal-2026-10-10-001",
);
```

Over the API, the money goes to your workspace's registered settlement key. Register it once in the dashboard; until then the call answers 403 `settlement_key_required`. Amount plus fee leave `available` at once and return if the PIX fails.

## Quote and convert

The direction is always `base_currency` → `quote_currency`: you pay `base_amount_minor` of the base and receive the quote currency.

```ts
// Buy USDT with R$ 500,00
const quote = await clarian.conversions.quote({ base_currency: "BRL", quote_currency: "USDT", base_amount_minor: "50000" });
quote.quote_amount_minor; // USDT you receive (scale 6), after fees
quote.expires_at;         // execute before this

const result = await clarian.conversions.execute(quote.id!);
result.ledger?.to_minor;  // settled amount; absent while result.status === "processing"
```

A quote converts once: a second execute is 409 `quote_not_open`, an expired one 410 `quote_expired`. To sell USDT for BRL, swap the currencies.

## Wallets and crypto

```ts
const wallets = await clarian.wallets.list();
await clarian.wallets.balance(wallets[0].id!);
await clarian.crypto.networks();
// API keys send only to wallets registered in the dashboard whitelist (403 destination_not_whitelisted).
await clarian.crypto.send({ amount_minor: "5000000", currency: "USDT", network: "polygon", to_address: "0x…" }, "send-17");
```

## Customers, KYC and sub-accounts

```ts
const customer = await clarian.customers.create({ kind: "person", display_name: "Maria Silva", document_type: "cpf", document_number: "52998224725" });
await clarian.kyc.create({ party_id: customer.id! });

const sub = await clarian.subaccounts.create({ display_name: "Store 12" });
const store = clarian.forSubaccount(sub.id!); // every call acts on that sub-account
await store.pix.payins.create({ amount_minor: "1000" }, "store12-order-1");
```

## Payment links

```ts
const link = await clarian.paymentLinks.create({ amount_minor: "4990", currency: "BRL", description: "Plano mensal" });
const page = await clarian.paymentLinks.payments({ limit: 50 }); // { items, next_cursor }
```

## Webhooks

```ts
const hook = await clarian.webhooks.create({ url: "https://example.com/clarian", events: ["payment.completed", "payment.failed"] });
hook.secret; // shown only here: store it
await clarian.webhooks.test(hook.id!); // sandbox key: sends a signed test.ping
```

Verify every delivery on the raw body before trusting it:

```ts
import { constructWebhookEvent } from "@clarian-finance/sdk";

// Express: app.post("/clarian", express.raw({ type: "application/json" }), handler)
const event = await constructWebhookEvent(req.body.toString("utf8"), req.headers, process.env.CLARIAN_WEBHOOK_SECRET!);
// Fetch / Next.js / Bun: await constructWebhookEvent(await request.text(), request.headers, secret)

if (event.type === "payment.completed") {
  const { payment_id, kind, amount_minor, currency } = event.data;
}
```

The signature is HMAC-SHA256 of `${x-clarian-timestamp}.${rawBody}`; deliveries older than 5 minutes are refused. Dedupe on `event.id` and answer 2xx fast. Failed deliveries are listed in `webhooks.deliveries()`. `webhooks.test()` and `webhooks.resend(id)` work with a sandbox key only; in live, reconcile missed events with `transactions.list()`.

## Sandbox

With a `cl_test_sk_` key nothing reaches a bank or chain. Settle or fail anything pending:

```ts
await clarian.pix.payins.simulate(charge.id!);                       // completed
await clarian.pix.payouts.simulate(payout.id!, { status: "failed", reason: "test" });
await clarian.crypto.simulate(send.id!);
```

## Errors, retries and idempotency

Non-2xx replies throw `ClarianError` with `status` and the API's stable `code`:

```ts
import { ClarianError } from "@clarian-finance/sdk";

try {
  await clarian.pix.payouts.create({ amount_minor: "10000", pix_key: "x" }, "w-1");
} catch (e) {
  if (e instanceof ClarianError && e.code === "insufficient_balance") { /* … */ }
}
```

Money-moving creates require an idempotency key: retrying with the same key returns the same payment and never pays twice. The SDK retries 429/502/503/504 and network errors up to `maxRetries` (default 2), but only reads, deletes and calls that carry an idempotency key. The API allows 120 requests per minute per key.

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

Request and response types are generated from the OpenAPI reference (`openapi.json`, also at `https://app.clarian.finance/openapi.json`).

## Development

```bash
npm install
npm run gen     # src/schema.ts from openapi.json
npm run lint
npm test
npm run build
CLARIAN_API_KEY=cl_test_sk_… CLARIAN_WORKSPACE_ID=… CLARIAN_BASE_URL=http://localhost:3000 npm run e2e
```
