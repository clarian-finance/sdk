# clarian-sdk (Python)

Python SDK for the Clarian API v2: PIX cash-in and cash-out, BRL/stablecoin quotes and conversion, wallets and crypto sends, customers and KYC, payment links, sub-accounts and webhooks. Standard library only. Python 3.9+.

Requests and replies are plain `dict`s shaped as in the OpenAPI reference (`openapi.json`).

## Install

```bash
pip install "git+https://github.com/clarian-finance/sdk@v1.0.0#subdirectory=python"
```

## Quick start

```python
import os
from clarian import Clarian

clarian = Clarian(
    os.environ["CLARIAN_API_KEY"],  # cl_test_sk_... (sandbox) or cl_live_sk_... (live)
    os.environ["CLARIAN_WORKSPACE_ID"],
)

clarian.ping()
balances = clarian.balances.list()
```

The key prefix picks the environment: `cl_test_sk_` calls `/v1/test`, `cl_live_sk_` calls `/v1/live`. Pass `base_url=` to point at another origin. Create keys in the dashboard under Negócios > API (needs 2FA). Give each key only the permissions it uses.

## Amounts

Every amount is an integer in minor units, sent and returned as a string. BRL has scale 2 (R$ 10,00 is `"1000"`), USDT and USDC scale 6 (1 USDT is `"1000000"`). Pass a `str` or `int`; a `float` anywhere in a request body raises `TypeError`. Balances carry their `scale`.

```python
brl = next(b for b in balances if b["currency"] == "BRL" and b["kind"] == "available")
# brl["balance_minor"] = "1250050", brl["scale"] = 2 -> R$ 12.500,50
```

## Cash-in (receive PIX)

```python
charge = clarian.pix.payins.create({"amount_minor": "25000", "currency": "BRL"}, "order-981")
charge["pix_qr_code"]  # copia-e-cola for the payer
# later, or from the webhook:
paid = clarian.pix.payins.retrieve(charge["id"])
```

The QR is valid for 30 minutes. The balance moves from `pending` to `available` when the PIX settles.

## Cash-out (send PIX)

```python
fee = clarian.pix.payouts.fee({"amount_minor": "10000"})  # fee_minor, debit_minor
payout = clarian.pix.payouts.create(
    {"amount_minor": "10000", "currency": "BRL", "pix_key": "settlement@yourcompany.com"},
    "withdrawal-2026-10-10-001",
)
```

Over the API, the money goes to your workspace's registered settlement key. Register it once in the dashboard; until then the call answers 403 `settlement_key_required`. Amount plus fee leave `available` at once and return if the PIX fails.

## Quote and convert

The direction is always `base_currency` -> `quote_currency`: you pay `base_amount_minor` of the base and receive the quote currency.

```python
# Buy USDT with R$ 500,00
quote = clarian.conversions.quote({"base_currency": "BRL", "quote_currency": "USDT", "base_amount_minor": "50000"})
quote["quote_amount_minor"]  # USDT you receive (scale 6), after fees
quote["expires_at"]          # execute before this

result = clarian.conversions.execute(quote["id"])
result.get("ledger")  # settled amounts; absent while result["status"] == "processing"
```

A quote converts once: a second execute is 409 `quote_not_open`, an expired one 410 `quote_expired`. `execute` is never retried automatically; after a lost reply, check `transactions.list()`. To sell USDT for BRL, swap the currencies.

## Wallets and crypto

```python
wallets = clarian.wallets.list()
clarian.wallets.balance(wallets[0]["id"])
clarian.crypto.networks()
# API keys send only to wallets registered in the dashboard whitelist (403 destination_not_whitelisted).
clarian.crypto.send({"amount_minor": "5000000", "currency": "USDT", "network": "polygon", "to_address": "0x..."}, "send-17")
```

## Customers, KYC and sub-accounts

```python
customer = clarian.customers.create({"kind": "person", "display_name": "Maria Silva", "document_type": "cpf", "document_number": "52998224725"})
clarian.kyc.create({"party_id": customer["id"]})

sub = clarian.subaccounts.create({"display_name": "Store 12"})
store = clarian.for_subaccount(sub["id"])  # every call acts on that sub-account
store.pix.payins.create({"amount_minor": "1000"}, "store12-order-1")
```

## Payment links

```python
link = clarian.payment_links.create({"amount_minor": "4990", "currency": "BRL", "description": "Plano mensal"})
page = clarian.payment_links.payments(limit=50)  # {"items": [...], "next_cursor": ...}
```

## Webhooks

```python
hook = clarian.webhooks.create({"url": "https://example.com/clarian", "events": ["payment.completed", "payment.failed"]})
hook["secret"]  # shown only here: store it
clarian.webhooks.test(hook["id"])  # sandbox key: sends a signed test.ping
```

Verify every delivery on the raw body before trusting it:

```python
from clarian import construct_webhook_event

# Flask
@app.post("/clarian")
def clarian_webhook():
    event = construct_webhook_event(request.get_data(), request.headers, os.environ["CLARIAN_WEBHOOK_SECRET"])
    if event["type"] == "payment.completed":
        data = event["data"]  # payment_id, kind, amount_minor, currency
    return "", 204
```

`construct_webhook_event` raises `ValueError` on a bad signature or timestamp. The signature is HMAC-SHA256 hex of `f"{x-clarian-timestamp}.{raw_body}"`; deliveries older than 5 minutes, or with an unparseable timestamp, are refused. Dedupe on `event["id"]` and answer 2xx fast. Failed deliveries are listed in `webhooks.deliveries()`. `webhooks.test()` and `webhooks.resend(id)` work with a sandbox key only; in live, reconcile missed events with `transactions.list()`.

## Sandbox

With a `cl_test_sk_` key nothing reaches a bank or chain. Settle or fail anything pending:

```python
clarian.pix.payins.simulate(charge["id"])  # completed
clarian.pix.payouts.simulate(payout["id"], {"status": "failed", "reason": "test"})
clarian.crypto.simulate(send["id"])
```

## Errors, retries and idempotency

Non-2xx replies raise `ClarianError` with `status`, the API's stable `code` and any extra fields in `meta`:

```python
from clarian import ClarianError

try:
    clarian.pix.payouts.create({"amount_minor": "10000", "pix_key": "x"}, "w-1")
except ClarianError as e:
    if e.code == "insufficient_balance":
        ...
```

Money-moving creates (`pix.payins.create`, `pix.payouts.create`, `crypto.send`, `payment_links.charge`, `checkout.pay`) require an idempotency key; an empty one raises `ValueError` before any request. Retrying with the same key returns the same payment and never pays twice. The SDK retries 429/502/503/504 and network errors up to `max_retries` (default 2), honoring `Retry-After`, but only reads, deletes and calls that carry an idempotency key. The API allows 120 requests per minute per key.

## Not on the API

Actions that need a person's own second factor stay in the dashboard: inviting or changing team members, voting on approvals, creating or rotating API keys, saving payout accounts and whitelist entries, and changing workspace settings. Their read endpoints are in the SDK.

## Reference

| Area | Methods |
|---|---|
| `balances` | `list` |
| `pix.payins` | `create`, `retrieve`, `simulate` |
| `pix.payouts` | `create`, `retrieve`, `fee`, `lookup`, `simulate`, `list_accounts` |
| `conversions` | `quote`, `execute`, `prices` |
| `wallets` | `list`, `create`, `retrieve`, `balance`, `transactions` |
| `crypto` | `send`, `simulate`, `networks` |
| `transactions` | `list`, `retrieve`, `statement` |
| `customers` | `list`, `create`, `retrieve`, `update` |
| `kyc` | `list`, `create`, `retrieve`, `list_rfi`, `answer_rfi` |
| `webhooks` | `list`, `create`, `retrieve`, `update`, `delete`, `test`, `deliveries`, `resend` |
| `payment_links` | `list`, `create`, `retrieve`, `update`, `delete`, `payments`, `stats`, `splits`, `replace_splits`, `charge` |
| `checkout` | `retrieve`, `pay` |
| `subaccounts` | `list`, `create`, `retrieve`, `balances`, `transactions` |
| `whitelist`, `bank_accounts`, `api_keys`, `settings`, `notifications`, `team`, `approvals` | read |
| `workflows`, `treasury` | `list`, `create` (+ `workflows.runs`) |

Field names and shapes are in the OpenAPI reference (`openapi.json`, also at `https://app.clarian.finance/openapi.json`).

## Development

```bash
python3 -m venv .venv && .venv/bin/pip install pytest
.venv/bin/python -m pytest
CLARIAN_API_KEY=cl_test_sk_... CLARIAN_WORKSPACE_ID=... CLARIAN_BASE_URL=http://localhost:3000 .venv/bin/python e2e/sandbox.py
```
