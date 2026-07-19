# @clarian-finance/sdk

Official TypeScript SDK for the [Clarian Finance](https://clarian.finance) API.

> **Leia em pt-BR:** [README.pt-BR.md](./README.pt-BR.md)
>
> **Go:** an official Go SDK lives in [`go/`](./go/) — `go get github.com/clarian-finance/sdk/go`.

## Install

```bash
npm install github:clarian-finance/sdk
```

## Quick Start

```typescript
import { Clarian } from "@clarian-finance/sdk";

const clarian = new Clarian({
  apiKey: "cl_test_sk_your_key_here",
  workspaceId: "your-workspace-uuid",
});

// Health check
const ping = await clarian.ping();
console.log(ping.environment); // "production" or "sandbox"

// Check balances
const balances = await clarian.balances.list();
console.log(balances);

// Create a PIX cash-in (deposit) — idempotency key required
const order = await clarian.cashIn.create({
  amount: 100.00,
  payer: {
    name: "Maria Silva",
    document: { number: "12345678900" },
  },
}, "deposit-001");
console.log(order.pix.copy_paste); // PIX copy-paste code
```

## Authentication

API keys follow the pattern `cl_{env}_sk_{secret}`:

| Prefix | Environment |
|--------|-------------|
| `cl_live_sk_` | Production (`/live/` routes) |
| `cl_test_sk_` | Sandbox (`/test/` routes) |

The SDK detects the environment from the key prefix automatically.

## Resources

### Ping

```typescript
const ping = await clarian.ping();
// { ok: true, environment: "production", master_account_id: "...", scope: "master" }
```

### RFQ (Quotes)

```typescript
// Get a quote
const quote = await clarian.rfq.quote({
  base_currency: "BRL",
  quote_currency: "USDT",
  amount: 5000,
  amount_currency: "BRL",
});

// Execute the quote (coming soon)
// const result = await clarian.rfq.execute(
//   { quote_id: quote.quote_id },
//   "rfq-exec-001",
// );
```

### PIX Cash-in (Deposits)

```typescript
// Create a deposit QR code (idempotency key required)
const deposit = await clarian.cashIn.create({
  amount: 50.00,
  payer: {
    name: "João Santos",
    document: { number: "12345678900" },
  },
  description: "Invoice #42",
  expiration_seconds: 3600,
}, "deposit-inv-42");

// Retrieve a deposit
const order = await clarian.cashIn.retrieve(deposit.id);
```

### PIX Cash-out (Payouts)

```typescript
// DICT lookup (preview the receiver)
const dict = await clarian.cashOut.dictCheck({
  pix_key: "email@example.com",
});

// Create a payout (idempotency key is required)
const payout = await clarian.cashOut.create({
  amount: 200.00,
  pix_key: "email@example.com",
  description: "Supplier payment",
}, "unique-idempotency-key");

// Retrieve a payout
const order = await clarian.cashOut.retrieve(payout.id);
```

### Wallets

```typescript
// List all wallets
const wallets = await clarian.wallets.list();

// Filter by network
const tronWallets = await clarian.wallets.list("tron");

// Get live on-chain balance
const balance = await clarian.wallets.retrieveBalance(wallets[0].id);
```

### Transactions

```typescript
// List recent transactions
const { transactions } = await clarian.transactions.list({ limit: 20 });

// Filter by type
const deposits = await clarian.transactions.list({ type: "pix_in" });

// Retrieve one transaction
const tx = await clarian.transactions.retrieve("tx-uuid");
```

### Balances

```typescript
const balances = await clarian.balances.list();
// [{ currency: "BRL", available: 1500.00, pending: 200.00, locked: 0 }]
```

### Webhooks

```typescript
// Create a webhook subscription
const { subscription, secret } = await clarian.webhooks.create({
  url: "https://yourapp.com/webhooks/clarian",
  events: [
    "pix_payin.completed",
    "subscription.invoice.paid",
    "subscription.activated",
  ],
  description: "Main webhook",
});
// Save `secret` securely - shown only once

// List subscriptions
const subs = await clarian.webhooks.list();

// Update
await clarian.webhooks.update(subscription.id, { is_active: false });

// Delete
await clarian.webhooks.delete(subscription.id);
```

### Products & Subscriptions (v0.2)

Requires the product to be enabled for the workspace in Clarian backoffice
(`pix_recurring` for PIX subscriptions; `card_payment` / `card_recurring` for card).

```typescript
// Create a monthly plan (product with cycle)
const plan = await clarian.products.create({
  external_id: "vizu-pro-monthly",
  name: "Vizu Pro",
  price_cents: 9900,
  cycle: "monthly",
});

// Create a PIX subscription — first invoice + QR returned inline
const { subscription, invoice } = await clarian.subscriptions.create({
  product_id: plan.id,
  payment_method: "pix",
  payer: { name: "Cliente", document: "12345678900", email: "c@example.com" },
}, "sub-create-001");

console.log(invoice.pix?.emv); // deliver QR / copy-paste to the payer

// List invoices, cancel, change plan
const invoices = await clarian.subscriptions.listInvoices(subscription.id);
await clarian.subscriptions.changePlan(subscription.id, { productId: plan.id });
await clarian.subscriptions.cancel(subscription.id, { at_period_end: true });

// Card charges (returns card_rail_not_ready until GOWD acquiring ships)
// await clarian.cards.charge({ amount_cents: 5000, card_token_id: "..." }, "chg-001");
```

### Sandbox testing

Sandbox-only helpers (`cl_test_sk_` keys). Outside sandbox they throw before any network call.

Magic PIX keys for the sandbox payout rail:

| Key | Behavior |
|-----|----------|
| `fail@sandbox.clarian` | Payout fails and funds are refunded |
| `pending@sandbox.clarian` | Payout stays pending until you simulate it |

```typescript
import {
  Clarian,
  SANDBOX_FAIL_PIX_KEY,
  SANDBOX_PENDING_PIX_KEY,
  signWebhookPayload,
} from "@clarian-finance/sdk";

const clarian = new Clarian({
  apiKey: "cl_test_sk_your_key_here",
  workspaceId: "your-workspace-uuid",
});

// Simulate a payin settling (default status: completed)
const paid = await clarian.sandbox.simulateCashIn(deposit.id);
// Or: "expired" | "failed"
await clarian.sandbox.simulateCashIn(deposit.id, "expired");

// Create a payout that stays pending, then complete it
const pendingPayout = await clarian.cashOut.create(
  { amount: 10, pix_key: SANDBOX_PENDING_PIX_KEY },
  "payout-sim-001",
);
const settled = await clarian.sandbox.simulateCashOut(pendingPayout.id, "completed");

// Force a failed payout
await clarian.cashOut.create(
  { amount: 10, pix_key: SANDBOX_FAIL_PIX_KEY },
  "payout-fail-001",
);

// Enqueue a sample webhook to a subscription
const { enqueued, delivery_id } = await clarian.sandbox.sendWebhookEvent(
  subscription.id,
  "pix_payin.completed",
);

// Re-queue a previous delivery
const { deliveryId } = await clarian.sandbox.resendWebhookDelivery(delivery_id!);

// Sign a payload locally to unit-test your webhook handler
const timestamp = new Date().toISOString();
const body = JSON.stringify({ amount: 100, status: "completed" });
const signature = await signWebhookPayload(body, timestamp, webhookSecret);
```

## Webhook Verification

Verify incoming webhook signatures to ensure authenticity:

```typescript
import {
  constructWebhookEvent,
  extractWebhookHeaders,
} from "@clarian-finance/sdk";

// Express / Node.js example
app.post("/webhooks/clarian", async (req, res) => {
  const headers = extractWebhookHeaders(req.headers);
  const rawBody = req.body; // must be the raw string, not parsed JSON

  try {
    const event = await constructWebhookEvent(rawBody, headers, WEBHOOK_SECRET);
    console.log(`Received ${event.event}`, event.data);
    res.sendStatus(200);
  } catch (err) {
    console.error("Webhook verification failed:", err);
    res.sendStatus(401);
  }
});
```

### Signature Format

| Header | Description |
|--------|-------------|
| `X-Clarian-Signature` | HMAC-SHA256 hex of `{timestamp}.{body}` |
| `X-Clarian-Timestamp` | ISO 8601 delivery timestamp |
| `X-Clarian-Event` | Event type (e.g. `pix_payin.completed`) |
| `X-Clarian-Delivery-Id` | Unique delivery UUID |
| `X-Clarian-Idempotency-Key` | Idempotency key for dedup |
| `X-Clarian-Attempt` | Delivery attempt number |

## Error Handling

```typescript
import { Clarian, ClarianError } from "@clarian-finance/sdk";

try {
  await clarian.cashOut.create({ amount: 100, pix_key: "..." }, "key-1");
} catch (err) {
  if (err instanceof ClarianError) {
    console.error(err.status);  // 400, 401, 403, 429...
    console.error(err.code);    // "invalid_amount", "rate_limited"...
    console.error(err.detail);  // human-readable detail
  }
}
```

## cURL Examples

```bash
# Ping (auth check)
curl -X GET \
  "https://api.clarian.finance/functions/v1/api-gateway/test/ping" \
  -H "Authorization: Bearer cl_test_sk_YOUR_KEY" \
  -H "X-Workspace-Id: YOUR_WORKSPACE_ID"

# Create a PIX cash-in
curl -X POST \
  "https://api.clarian.finance/functions/v1/api-gateway/test/pix/payins" \
  -H "Authorization: Bearer cl_test_sk_YOUR_KEY" \
  -H "X-Workspace-Id: YOUR_WORKSPACE_ID" \
  -H "Content-Type: application/json" \
  -d '{"amount": 10, "payer": {"name": "Test", "document": {"number": "12345678900"}}}'

# List balances
curl -X GET \
  "https://api.clarian.finance/functions/v1/api-gateway/test/account/balances" \
  -H "Authorization: Bearer cl_test_sk_YOUR_KEY" \
  -H "X-Workspace-Id: YOUR_WORKSPACE_ID"
```

## License

MIT
