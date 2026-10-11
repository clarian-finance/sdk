# Clarian Go SDK

Go SDK for the Clarian API v2: PIX cash-in and cash-out, BRL/stablecoin quotes and conversion, wallets and crypto sends, customers and KYC, payment links, sub-accounts and webhooks. Standard library only. Go 1.21+.

## Install

```bash
go get github.com/clarian-finance/sdk/go
```

## Quick start

```go
import clarian "github.com/clarian-finance/sdk/go"

c, err := clarian.New(
	os.Getenv("CLARIAN_API_KEY"), // cl_test_sk_… (sandbox) or cl_live_sk_… (live)
	os.Getenv("CLARIAN_WORKSPACE_ID"),
)
if err != nil { log.Fatal(err) }

ctx := context.Background()
_, err = c.Ping(ctx)
balances, err := c.Balances.List(ctx)
```

The key prefix picks the environment: `cl_test_sk_` calls `/v1/test`, `cl_live_sk_` calls `/v1/live`. Create keys in the dashboard under Negócios > API (needs 2FA). Give each key only the permissions it uses.

Options: `WithBaseURL`, `WithHTTPClient` (timeouts, proxies; default 30s), `WithMaxRetries` (default 2), `WithSubaccount`.

Requests and replies are `clarian.Row` (`map[string]any`); the fields are documented in `openapi.json`. Numbers in replies decode as `json.Number`.

## Amounts

Every amount is an integer in minor units, sent and returned as a string. BRL has scale 2 (R$ 10,00 is `"1000"`), USDT and USDC scale 6 (1 USDT is `"1000000"`). Pass a `string`, an integer or a `*big.Int`; a float is refused before the request is sent. Balances carry their `scale`.

```go
for _, b := range balances {
	if b["currency"] == "BRL" && b["kind"] == "available" {
		// b["balance_minor"] = "1250050", b["scale"] = 2 → R$ 12.500,50
	}
}
```

## Cash-in (receive PIX)

```go
charge, err := c.Pix.Payins.Create(ctx, clarian.Row{"amount_minor": "25000", "currency": "BRL"}, "order-981")
charge["pix_qr_code"] // copia-e-cola for the payer
// later, or from the webhook:
paid, err := c.Pix.Payins.Retrieve(ctx, charge["id"].(string))
```

The QR is valid for 30 minutes. The balance moves from `pending` to `available` when the PIX settles.

## Cash-out (send PIX)

```go
fee, err := c.Pix.Payouts.Fee(ctx, clarian.Row{"amount_minor": "10000"}) // fee_minor, debit_minor
payout, err := c.Pix.Payouts.Create(ctx,
	clarian.Row{"amount_minor": "10000", "currency": "BRL", "pix_key": "settlement@yourcompany.com"},
	"withdrawal-2026-10-10-001",
)
```

Over the API, the money goes to your workspace's registered settlement key. Register it once in the dashboard; until then the call answers 403 `settlement_key_required`. Amount plus fee leave `available` at once and return if the PIX fails.

## Quote and convert

The direction is always `base_currency` → `quote_currency`: you pay `base_amount_minor` of the base and receive the quote currency.

```go
// Buy USDT with R$ 500,00
quote, err := c.Conversions.Quote(ctx, clarian.Row{"base_currency": "BRL", "quote_currency": "USDT", "base_amount_minor": "50000"})
quote["quote_amount_minor"] // USDT you receive (scale 6), after fees
quote["expires_at"]         // execute before this

result, err := c.Conversions.Execute(ctx, quote["id"].(string))
// result["ledger"]["to_minor"]: settled amount; absent while result["status"] == "processing"
```

A quote converts once: a second execute is 409 `quote_not_open`, an expired one 410 `quote_expired`. To sell USDT for BRL, swap the currencies.

## Wallets and crypto

```go
wallets, err := c.Wallets.List(ctx)
_, err = c.Wallets.Balance(ctx, wallets[0]["id"].(string))
_, err = c.Crypto.Networks(ctx)
// API keys send only to wallets registered in the dashboard whitelist (403 destination_not_whitelisted).
_, err = c.Crypto.Send(ctx, clarian.Row{"amount_minor": "5000000", "currency": "USDT", "network": "polygon", "to_address": "0x…"}, "send-17")
```

## Customers, KYC and sub-accounts

```go
customer, err := c.Customers.Create(ctx, clarian.Row{"kind": "person", "display_name": "Maria Silva", "document_type": "cpf", "document_number": "52998224725"})
_, err = c.Kyc.Create(ctx, clarian.Row{"party_id": customer["id"]})

sub, err := c.Subaccounts.Create(ctx, clarian.Row{"display_name": "Store 12"})
store := c.ForSubaccount(sub["id"].(string)) // every call acts on that sub-account
_, err = store.Pix.Payins.Create(ctx, clarian.Row{"amount_minor": "1000"}, "store12-order-1")
```

## Payment links

```go
link, err := c.PaymentLinks.Create(ctx, clarian.Row{"amount_minor": "4990", "currency": "BRL", "description": "Plano mensal"})
page, err := c.PaymentLinks.Payments(ctx, clarian.Params{"limit": "50"}) // {items, next_cursor}
```

## Webhooks

```go
hook, err := c.Webhooks.Create(ctx, clarian.Row{"url": "https://example.com/clarian", "events": []any{"payment.completed", "payment.failed"}})
hook["secret"] // shown only here: store it
_, err = c.Webhooks.Test(ctx, hook["id"].(string)) // sandbox key: sends a signed test.ping
```

Verify every delivery on the raw body before trusting it:

```go
http.HandleFunc("/clarian", func(w http.ResponseWriter, r *http.Request) {
	raw, _ := io.ReadAll(r.Body)
	event, err := clarian.ConstructWebhookEvent(raw, r.Header, os.Getenv("CLARIAN_WEBHOOK_SECRET"))
	if err != nil {
		http.Error(w, "bad signature", http.StatusBadRequest)
		return
	}
	if event.Type == "payment.completed" {
		_ = event.Data["payment_id"]
	}
	w.WriteHeader(http.StatusNoContent)
})
```

The signature is HMAC-SHA256 of `<x-clarian-timestamp>.<rawBody>`; deliveries older than 5 minutes and unparseable timestamps are refused. Dedupe on `event.ID` and answer 2xx fast. Failed deliveries are listed in `Webhooks.Deliveries`. `Webhooks.Test` and `Webhooks.Resend` work with a sandbox key only; in live, reconcile missed events with `Transactions.List`.

## Sandbox

With a `cl_test_sk_` key nothing reaches a bank or chain. Settle or fail anything pending (a nil body means `completed`):

```go
c.Pix.Payins.Simulate(ctx, chargeID, nil)
c.Pix.Payouts.Simulate(ctx, payoutID, clarian.Row{"status": "failed", "reason": "test"})
c.Crypto.Simulate(ctx, sendID, nil)
```

## Errors, retries and idempotency

Non-2xx replies return `*clarian.Error` with `Status` and the API's stable `Code`:

```go
_, err := c.Pix.Payouts.Create(ctx, clarian.Row{"amount_minor": "10000", "pix_key": "x"}, "w-1")
var e *clarian.Error
if errors.As(err, &e) && e.Code == "insufficient_balance" { /* … */ }
```

Money-moving creates (`Pix.Payins.Create`, `Pix.Payouts.Create`, `Crypto.Send`, `PaymentLinks.Charge`, `Checkout.Pay`) require an idempotency key; an empty key returns `ErrIdempotencyKeyRequired` without a request. Retrying with the same key returns the same payment and never pays twice. The SDK retries 429/502/503/504 and network errors up to `WithMaxRetries` (default 2) and honors `Retry-After`, but only for reads, deletes and calls that carry an idempotency key. `Conversions.Execute` is never retried. The API allows 120 requests per minute per key.

## Not on the API

Actions that need a person's own second factor stay in the dashboard: inviting or changing team members, voting on approvals, creating or rotating API keys, saving payout accounts and whitelist entries, and changing workspace settings. Their read endpoints are in the SDK.

## Reference

| Area | Methods |
|---|---|
| `Balances` | `List` |
| `Pix.Payins` | `Create`, `Retrieve`, `Simulate` |
| `Pix.Payouts` | `Create`, `Retrieve`, `Fee`, `Lookup`, `Simulate`, `ListAccounts` |
| `Conversions` | `Quote`, `Execute`, `Prices` |
| `Wallets` | `List`, `Create`, `Retrieve`, `Balance`, `Transactions` |
| `Crypto` | `Send`, `Simulate`, `Networks` |
| `Transactions` | `List`, `Retrieve`, `Statement` |
| `Customers` | `List`, `Create`, `Retrieve`, `Update` |
| `Kyc` | `List`, `Create`, `Retrieve`, `ListRfi`, `AnswerRfi` |
| `Webhooks` | `List`, `Create`, `Retrieve`, `Update`, `Delete`, `Test`, `Deliveries`, `Resend` |
| `PaymentLinks` | `List`, `Create`, `Retrieve`, `Update`, `Delete`, `Payments`, `Stats`, `Splits`, `ReplaceSplits`, `Charge` |
| `Checkout` | `Retrieve`, `Pay` |
| `Subaccounts` | `List`, `Create`, `Retrieve`, `Balances`, `Transactions` |
| `Whitelist`, `BankAccounts`, `APIKeys`, `Settings`, `Notifications`, `Team`, `Approvals` | read |
| `Workflows`, `Treasury` | `List`, `Create` (+ `Workflows.Runs`) |

The full contract is `openapi.json` at the repo root, also at `https://app.clarian.finance/openapi.json`.

## Development

```bash
cd go
go vet ./...
go test ./...
CLARIAN_API_KEY=cl_test_sk_… CLARIAN_WORKSPACE_ID=… CLARIAN_BASE_URL=http://localhost:3000 go run ./e2e
```
