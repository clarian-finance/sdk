# sdk-go

Official Go SDK for the [Clarian Finance API](https://api.clarian.finance) — PIX cash-in/cash-out, balances, transactions, and webhooks.

Module: `github.com/clarian-finance/sdk/go` · package `clarian` · **stdlib only** (no third-party dependencies).

## Install

```bash
go get github.com/clarian-finance/sdk/go@v0.4.1
```

Requires Go 1.21+.

## Quickstart

```go
package main

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"os"

	"github.com/clarian-finance/sdk/go"
)

func main() {
	client := clarian.New(
		os.Getenv("CLARIAN_API_KEY"),       // cl_live_sk_... or cl_test_sk_...
		os.Getenv("CLARIAN_WORKSPACE_ID"),  // workspace UUID
	)
	// cl_test_sk_ → sandbox; cl_live_sk_ (and any other prefix) → production/live.
	// Override with clarian.WithBaseURL("...") or clarian.WithHTTPClient(...).

	ctx := context.Background()
	if _, err := client.Ping(ctx); err != nil {
		log.Fatal(err)
	}

	// Receive BRL via PIX (Idempotency-Key is required).
	charge, err := client.CashIn.Create(ctx, "order-1234", clarian.CashInRequest{
		Amount: json.Number("250.00"),
		Payer: clarian.Payer{
			Name:           "Maria Silva",
			DocumentNumber: "12345678900", // CPF 11 digits or CNPJ 14 digits
		},
		Description: "Pedido #1234",
	})
	if err != nil {
		log.Fatal(err)
	}
	fmt.Println(*charge.Order.Pix.CopyPaste) // EMV copia-e-cola

	// Send BRL via PIX (idempotency key required — retries never double-send).
	// With a registered settlement key, amount alone is enough:
	payout, err := client.CashOut.Create(ctx, "withdrawal-2026-07-01-001", clarian.CashOutRequest{
		Amount: json.Number("100.00"),
	})
	if err != nil {
		log.Fatal(err)
	}
	fmt.Println(payout.Order.ID, payout.Order.Status)

	// Or target a PIX key explicitly:
	_, _ = client.CashOut.Create(ctx, "payout-inv-42", clarian.CashOutRequest{
		Amount:     json.Number("50.00"),
		PixKey:     "maria@example.com",
		PixKeyType: clarian.PixKeyEmail,
	})
}
```

Amounts are BRL reais as JSON numbers (`encoding/json.Number`). Convert with `amount.Float64()`, `amount.Int64()`, or your preferred decimal library.

## Environments

| Key prefix       | Base URL                                                              |
|------------------|-----------------------------------------------------------------------|
| `cl_test_sk_…`   | `https://api.clarian.finance/functions/v1/api-gateway/test`           |
| `cl_live_sk_…`   | `https://api.clarian.finance/functions/v1/api-gateway/live`           |
| any other prefix | **live** (same as `cl_live_sk_…`) — not rejected                     |

Only the `cl_test_sk_` prefix selects sandbox. Unknown or empty prefixes default to the **live** base URL so a mis-typed key can hit production — set `WithBaseURL` when you need an explicit host.

Auth headers (set automatically): `Authorization: Bearer <key>` and `X-Workspace-Id: <uuid>`.

## Error handling

Non-2xx responses return `*clarian.APIError` with `Status`, `Code` (from JSON `error` when present), and raw `Body`. Network and decode failures are wrapped with `%w`.

```go
import (
	"errors"
	"log"

	"github.com/clarian-finance/sdk/go"
)

_, err := client.CashOut.Create(ctx, "payout-inv-42", clarian.CashOutRequest{
	Amount: json.Number("100.00"),
})
if err != nil {
	var apiErr *clarian.APIError
	if errors.As(err, &apiErr) {
		log.Printf("API %d code=%s body=%s", apiErr.Status, apiErr.Code, apiErr.Body)
		return
	}
	log.Printf("transport: %v", err)
}
```

## Verifying webhooks

Deliveries are signed with **HMAC-SHA256** (hex) over `` `${X-Clarian-Timestamp}.${rawBody}` `` using the full `whsec_…` secret (prefix included — do not strip it or base64-decode). Reject timestamps older than **5 minutes** (replay protection).

Headers on each delivery:

| Header                        | Purpose                          |
|-------------------------------|----------------------------------|
| `X-Clarian-Signature`         | HMAC-SHA256 hex digest           |
| `X-Clarian-Timestamp`         | ISO 8601 / RFC3339Nano           |
| `X-Clarian-Event`             | Event type                       |
| `X-Clarian-Delivery-Id`       | Unique delivery id               |
| `X-Clarian-Idempotency-Key`   | Dedupe key for the delivery      |
| `X-Clarian-Attempt`           | Retry attempt number             |

```go
package main

import (
	"io"
	"log"
	"net/http"
	"os"

	"github.com/clarian-finance/sdk/go"
)

func main() {
	secret := os.Getenv("CLARIAN_WEBHOOK_SECRET") // whsec_...

	http.HandleFunc("/webhooks/clarian", func(w http.ResponseWriter, r *http.Request) {
		// raw body must be the exact bytes received — do not re-serialize JSON.
		payload, err := io.ReadAll(r.Body)
		if err != nil {
			http.Error(w, "read body", http.StatusBadRequest)
			return
		}
		event, err := clarian.VerifyWebhook(payload, r.Header, secret)
		if err != nil {
			http.Error(w, "invalid signature", http.StatusUnauthorized)
			return
		}
		log.Printf("event %s type=%s tx=%s amount=%s",
			event.ID, event.Type, event.Data.TransactionID, event.Data.Amount)
		// event.RawData has the original data object for forward-compatible fields.
		w.WriteHeader(http.StatusOK)
	})
	log.Fatal(http.ListenAndServe(":8080", nil))
}
```

Create a subscription (secret shown **once**):

```go
sub, err := client.Webhooks.Create(ctx, clarian.WebhookInput{
	URL:    "https://example.com/webhooks/clarian", // must be public https
	Events: []string{"pix_payin.completed", "pix_payout.completed"},
})
if err != nil {
	log.Fatal(err)
}
// Gateway returns {subscription, secret}; SDK flattens to WebhookWithSecret.
// store sub.Secret immediately — it is never returned again
fmt.Println(sub.ID, sub.Secret)
```

Server-side rules that commonly cause `400` on create/update:

- **`url` must be public `https`** — the gateway rejects `http`, loopback, and internal hosts.
- **`events` must be from the server's subscribable list** (e.g. `pix_payin.completed`, `pix_payout.failed`, …). Unknown event names are rejected.

## Sandbox testing

Sandbox-only helpers live on `client.Sandbox`. They require a `cl_test_sk_…` key and refuse live keys **before** any HTTP call. Outside the test environment the gateway returns `404 {error:"sandbox_only"}`.

```go
// Advance a pending cash-in (empty status → completed).
order, err := client.Sandbox.SimulateCashIn(ctx, charge.Order.ID, "completed")
// status: "completed" | "expired" | "failed"

// Advance a cash-out: "completed" | "failed"
_, err = client.Sandbox.SimulateCashOut(ctx, payout.Order.ID, "failed")

// Magic PIX keys the sandbox payout rail honors:
//   clarian.SandboxFailPixKey    → fail@sandbox.clarian
//   clarian.SandboxPendingPixKey → pending@sandbox.clarian
_, _ = client.CashOut.Create(ctx, "payout-fail-1", clarian.CashOutRequest{
	Amount: json.Number("10.00"),
	PixKey: clarian.SandboxFailPixKey,
})

// Enqueue a sample webhook for a subscription (event type constants available).
result, err := client.Sandbox.SendWebhookEvent(ctx, sub.ID, clarian.EventPixPayinCompleted)
// result.DeliveryID is the new delivery id when present

// Re-send an existing delivery
newID, err := client.Sandbox.ResendWebhookDelivery(ctx, result.DeliveryID)

// Local handler tests: sign a payload the same way the server does
sig := clarian.SignPayload(secret, timestamp, rawBody)
```

Sample event types: `pix_payin.created`, `pix_payin.completed`, `pix_payin.expired`, `pix_payout.created`, `pix_payout.completed`, `pix_payout.failed`, `checkout.paid` (constants `EventPixPayinCreated`, …).

## API surface

| Method | Path | Client |
|--------|------|--------|
| `GET` | `/ping` | `client.Ping(ctx)` |
| `POST` | `/cash-in/pix` | `client.CashIn.Create(ctx, idempotencyKey, req)` |
| `GET` | `/cash-in/{id}` | `client.CashIn.Get(ctx, id)` |
| `POST` | `/cash-out/pix` | `client.CashOut.Create(ctx, idempotencyKey, req)` |
| `GET` | `/cash-out/{id}` | `client.CashOut.Get(ctx, id)` |
| `GET` | `/account/balances` | `client.Balances.List(ctx)` |
| `GET` | `/transactions` | `client.Transactions.List(ctx, params)` |
| `GET` | `/transactions/{id}` | `client.Transactions.Get(ctx, id)` |
| — | webhooks CRUD | `client.Webhooks.Create/List/Get/Update/Delete` |
| — | verify delivery | `clarian.VerifyWebhook(payload, headers, secret)` |
| — | sign payload (tests) | `clarian.SignPayload(secret, timestamp, payload)` |
| `POST` | `/pix/payins/{id}/simulate` | `client.Sandbox.SimulateCashIn(ctx, id, status)` |
| `POST` | `/pix/payouts/{id}/simulate` | `client.Sandbox.SimulateCashOut(ctx, id, status)` |
| `POST` | `/webhooks/{id}/test` | `client.Sandbox.SendWebhookEvent(ctx, id, eventType)` |
| `POST` | `/webhooks/deliveries/{id}/resend` | `client.Sandbox.ResendWebhookDelivery(ctx, id)` |

## Development

```bash
go build ./...
go vet ./...
go test ./...
gofmt -l .
```
