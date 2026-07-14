# @clarian-finance/sdk

Official TypeScript SDK for the [Clarian Finance](https://clarian.finance) API.

> **Leia em pt-BR:** [README.pt-BR.md](./README.pt-BR.md)

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

// Check balances
const balances = await clarian.balances.list();
console.log(balances);

// Create a PIX cash-in (deposit)
const order = await clarian.cashIn.create({
  amount: 100.00,
  payer: {
    name: "Maria Silva",
    document: { number: "12345678900" },
  },
});
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

### RFQ (Quotes)

```typescript
// Get a quote
const quote = await clarian.rfq.quote({
  base_currency: "BRL",
  quote_currency: "USDT",
  amount: 5000,
  amount_currency: "BRL",
});

// Execute the quote
const result = await clarian.rfq.execute(
  { quote_id: quote.quote_id },
  "idempotency-key-123",
);
```

### PIX Cash-in (Deposits)

```typescript
// Create a deposit QR code
const deposit = await clarian.cashIn.create({
  amount: 50.00,
  payer: {
    name: "João Santos",
    document: { number: "12345678900" },
  },
  description: "Invoice #42",
  expiration_seconds: 3600,
});

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
  events: ["pix_payin.completed", "pix_payout.completed"],
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
