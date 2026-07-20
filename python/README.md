# clarian-sdk

Official Python SDK for the [Clarian Finance API](https://api.clarian.finance). PIX cash-in/cash-out, balances, wallets, transactions, and webhooks.

Package: `clarian` · distribution: `clarian-sdk` · **stdlib only** (no third-party runtime dependencies). Requires Python 3.9+.

## Install

```bash
pip install "git+https://github.com/clarian-finance/sdk.git@v0.4.1#subdirectory=python"
```

## Quickstart

```python
import os
from clarian import Clarian

client = Clarian(
    os.environ["CLARIAN_API_KEY"],       # cl_live_sk_... or cl_test_sk_...
    os.environ["CLARIAN_WORKSPACE_ID"],  # workspace UUID
)
# cl_test_sk_ -> sandbox; cl_live_sk_ (and any other prefix) -> production/live.
# Override with base_url="..." or timeout=60.

client.ping()

# Receive BRL via PIX (Idempotency-Key is sent automatically).
charge = client.cash_in.create(
    {
        "amount": 250.00,
        "payer": {
            "name": "Maria Silva",
            "document_number": "12345678900",
        },
        "description": "Order #1234",
    },
    "order-1234",
)
print(charge["order"]["pix"]["copy_paste"])

# Send BRL via PIX (idempotency key required; retries never double-send).
payout = client.cash_out.create(
    {"amount": 100.00},
    "withdrawal-2026-07-01-001",
)
print(payout["order"]["id"], payout["order"]["status"])

# Optional: preview a PIX key owner before paying out.
info = client.cash_out.dict_check("maria@example.com", "EMAIL")

balances = client.balances.list()
```

Auth headers (set automatically): `Authorization: Bearer <key>` and `X-Workspace-Id: <uuid>`.

## Environments

| Key prefix       | Base URL                                                    |
|------------------|-------------------------------------------------------------|
| `cl_test_sk_…`   | `https://api.clarian.finance/functions/v1/api-gateway/test` |
| `cl_live_sk_…`   | `https://api.clarian.finance/functions/v1/api-gateway/live` |
| any other prefix | **live** (same as `cl_live_sk_…`); not rejected             |

Only the `cl_test_sk_` prefix selects sandbox. Unknown prefixes default to live so a mis-typed key can hit production; pass `base_url` when you need an explicit host.

## Error handling

Non-2xx responses raise `ClarianError` with `status`, `code` (JSON `error` when present), `message`, and `meta`. The exception text truncates the raw body to 500 characters.

```python
from clarian import Clarian, ClarianError

try:
    client.cash_out.create({"amount": 100.00}, "payout-inv-42")
except ClarianError as e:
    print(e.status, e.code, e.message, e.meta)
```

## Verifying webhooks

Deliveries are signed with **HMAC-SHA256** (hex) over `` `{X-Clarian-Timestamp}.{rawBody}` `` using the full `whsec_…` secret (prefix included; do not strip it). Reject timestamps older than **5 minutes** (replay protection).

```python
from clarian.webhooks import extract_headers, verify_signature
# or: from clarian import extract_headers, verify_signature

def handle(raw_body: bytes, headers: dict) -> None:
    secret = os.environ["CLARIAN_WEBHOOK_SECRET"]  # whsec_...
    timestamp, signature = extract_headers(headers)
    if not verify_signature(raw_body, timestamp, signature, secret):
        raise PermissionError("invalid webhook signature")
    # process JSON body...
```

For local handler tests, sign a payload the same way the server does:

```python
from clarian import sign_payload

sig = sign_payload(secret, timestamp, raw_body)
```

Create a subscription (secret shown **once**):

```python
sub = client.webhooks.create({
    "url": "https://example.com/webhooks/clarian",
    "events": ["pix_payin.completed", "pix_payout.completed"],
})
# store sub["secret"] immediately; it is never returned again
print(sub["id"], sub["secret"])
```

## Sandbox testing

Sandbox helpers live on `client.sandbox`. They require a `cl_test_sk_…` key and raise `ValueError` for live keys **before** any HTTP call. Outside the test environment the gateway returns `404 {error:"sandbox_only"}`.

```python
# Advance a pending cash-in (empty/None status -> completed).
order = client.sandbox.simulate_cash_in(charge["order"]["id"], "completed")
# status: "completed" | "expired" | "failed"

# Advance a cash-out: "completed" | "failed"
client.sandbox.simulate_cash_out(payout["order"]["id"], "failed")

# Enqueue a sample webhook for a subscription.
from clarian import EVENT_PIX_PAYIN_COMPLETED

result = client.sandbox.send_webhook_event(sub["id"], EVENT_PIX_PAYIN_COMPLETED)
new_id = client.sandbox.resend_webhook_delivery(result["delivery_id"])
```

### Magic PIX keys

| Constant / key            | Sandbox payout behavior                         |
|---------------------------|-------------------------------------------------|
| `SANDBOX_FAIL_PIX_KEY`    | `fail@sandbox.clarian` (fails + refund)         |
| `SANDBOX_PENDING_PIX_KEY` | `pending@sandbox.clarian` (stays pending until simulated) |

```python
from clarian import SANDBOX_FAIL_PIX_KEY

client.cash_out.create(
    {"amount": 10.00, "pix_key": SANDBOX_FAIL_PIX_KEY},
    "payout-fail-1",
)
```

Sample event type constants: `EVENT_PIX_PAYIN_CREATED`, `EVENT_PIX_PAYIN_COMPLETED`, `EVENT_PIX_PAYIN_EXPIRED`, `EVENT_PIX_PAYOUT_CREATED`, `EVENT_PIX_PAYOUT_COMPLETED`, `EVENT_PIX_PAYOUT_FAILED`, `EVENT_CHECKOUT_PAID`.

## API surface

| Method | Path | Client |
|--------|------|--------|
| `GET` | `/ping` | `client.ping()` |
| `POST` | `/cash-in/pix` | `client.cash_in.create(params, idempotency_key)` |
| `GET` | `/cash-in/{id}` | `client.cash_in.retrieve(id)` |
| `POST` | `/pix/payouts/dict` | `client.cash_out.dict_check(pix_key, key_type=None)` |
| `POST` | `/cash-out/pix` | `client.cash_out.create(params, idempotency_key)` |
| `GET` | `/cash-out/{id}` | `client.cash_out.retrieve(id)` |
| `GET` | `/account/balances` | `client.balances.list()` |
| `GET` | `/transactions` | `client.transactions.list(...)` |
| `GET` | `/transactions/{id}` | `client.transactions.retrieve(id)` |
| `GET` | `/wallets` | `client.wallets.list(network=None)` |
| `GET` | `/wallets/{id}/balance` | `client.wallets.retrieve_balance(id)` |
| | webhooks CRUD | `client.webhooks.create/list/update/delete` |
| | verify delivery | `verify_signature` / `extract_headers` / `sign_payload` |
| `POST` | `/pix/payins/{id}/simulate` | `client.sandbox.simulate_cash_in(id, status)` |
| `POST` | `/pix/payouts/{id}/simulate` | `client.sandbox.simulate_cash_out(id, status)` |
| `POST` | `/webhooks/{id}/test` | `client.sandbox.send_webhook_event(id, event_type)` |
| `POST` | `/webhooks/deliveries/{id}/resend` | `client.sandbox.resend_webhook_delivery(id)` |

## Development

```bash
# from repo root
python3 -m unittest discover -s python/tests -t python
```
