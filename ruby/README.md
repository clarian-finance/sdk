# clarian (Ruby)

Official Ruby SDK for the [Clarian Finance API](https://api.clarian.finance). PIX cash-in/cash-out, balances, wallets, transactions, and webhooks.

Gem: `clarian` · **stdlib only** at runtime (`net/http`, `json`, `openssl`). Requires Ruby >= 3.0.

## Install

Until the gem is published to RubyGems, install from this monorepo with Bundler:

```ruby
# Gemfile
gem "clarian",
  git: "https://github.com/clarian-finance/sdk.git",
  tag: "v0.4.0",
  glob: "ruby/*.gemspec"
```

```bash
bundle install
```

## Quickstart

```ruby
require "clarian"

client = Clarian::Client.new(
  api_key: ENV.fetch("CLARIAN_API_KEY"),       # cl_live_sk_... or cl_test_sk_...
  workspace_id: ENV.fetch("CLARIAN_WORKSPACE_ID") # workspace UUID
)
# cl_test_sk_ -> sandbox; cl_live_sk_ (and any other prefix) -> production/live.
# Override with base_url: "..." or timeout: 60.

client.ping

# Receive BRL via PIX (Idempotency-Key is required).
charge = client.cash_in.create(
  {
    "amount" => 250.00,
    "payer" => {
      "name" => "Maria Silva",
      "document_number" => "12345678900"
    },
    "description" => "Order #1234"
  },
  idempotency_key: "order-1234"
)
puts charge.dig("order", "pix", "copy_paste")

# Send BRL via PIX (idempotency key required; retries never double-send).
payout = client.cash_out.create(
  { "amount" => 100.00 },
  idempotency_key: "withdrawal-2026-07-01-001"
)
puts payout.dig("order", "id"), payout.dig("order", "status")

# Optional: preview a PIX key owner before paying out.
info = client.cash_out.dict_check("maria@example.com", key_type: "EMAIL")

balances = client.balances.list
```

Auth headers (set automatically): `Authorization: Bearer <key>` and `X-Workspace-Id: <uuid>`.

## Environments

| Key prefix       | Base URL                                                    |
|------------------|-------------------------------------------------------------|
| `cl_test_sk_…`   | `https://api.clarian.finance/functions/v1/api-gateway/test` |
| `cl_live_sk_…`   | `https://api.clarian.finance/functions/v1/api-gateway/live` |
| any other prefix | **live** (same as `cl_live_sk_…`); not rejected             |

Only the `cl_test_sk_` prefix selects sandbox. Unknown prefixes default to live so a mis-typed key can hit production; pass `base_url:` when you need an explicit host.

## Error handling

Non-2xx responses raise `Clarian::Error` with `status`, `code` (JSON `error` when present), `message`, and `meta`. The exception text truncates the raw body to 500 characters.

```ruby
begin
  client.cash_out.create({ "amount" => 100.00 }, idempotency_key: "payout-inv-42")
rescue Clarian::Error => e
  puts e.status, e.code, e.message, e.meta
end
```

## Verifying webhooks

Deliveries are signed with **HMAC-SHA256** (hex) over `` `{X-Clarian-Timestamp}.{rawBody}` `` using the full `whsec_…` secret (prefix included; do not strip it). Reject timestamps older than **5 minutes** (replay protection).

```ruby
require "clarian"

def handle(raw_body, headers)
  secret = ENV.fetch("CLARIAN_WEBHOOK_SECRET") # whsec_...
  extracted = Clarian::Webhooks.extract_headers(headers)
  ok = Clarian::Webhooks.verify_signature(
    payload: raw_body,
    timestamp: extracted[:timestamp],
    signature: extracted[:signature],
    secret: secret
  )
  raise "invalid webhook signature" unless ok
  # process JSON body...
end
```

For local handler tests, sign a payload the same way the server does:

```ruby
sig = Clarian::Webhooks.sign_payload(
  secret: secret,
  timestamp: timestamp,
  payload: raw_body
)
```

Create a subscription (secret shown **once**):

```ruby
sub = client.webhooks.create({
  "url" => "https://example.com/webhooks/clarian",
  "events" => ["pix_payin.completed", "pix_payout.completed"]
})
# store sub["secret"] immediately; it is never returned again
puts sub["id"], sub["secret"]
```

## Sandbox testing

Sandbox helpers live on `client.sandbox`. They require a `cl_test_sk_…` key and raise `ArgumentError` for live keys **before** any HTTP call. Outside the test environment the gateway returns `404 {error:"sandbox_only"}`.

```ruby
# Advance a pending cash-in (nil/empty status -> completed).
order = client.sandbox.simulate_cash_in(charge.dig("order", "id"), "completed")
# status: "completed" | "expired" | "failed"

# Advance a cash-out: "completed" | "failed"
client.sandbox.simulate_cash_out(payout.dig("order", "id"), "failed")

# Enqueue a sample webhook for a subscription.
result = client.sandbox.send_webhook_event(sub["id"], Clarian::EVENT_PIX_PAYIN_COMPLETED)
new_id = client.sandbox.resend_webhook_delivery(result["delivery_id"])
```

### Magic PIX keys

| Constant / key            | Sandbox payout behavior                                      |
|---------------------------|--------------------------------------------------------------|
| `SANDBOX_FAIL_PIX_KEY`    | `fail@sandbox.clarian` (fails + refund)                      |
| `SANDBOX_PENDING_PIX_KEY` | `pending@sandbox.clarian` (stays pending until simulated)    |

```ruby
client.cash_out.create(
  { "amount" => 10.00, "pix_key" => Clarian::SANDBOX_FAIL_PIX_KEY },
  idempotency_key: "payout-fail-1"
)
```

Sample event type constants: `EVENT_PIX_PAYIN_CREATED`, `EVENT_PIX_PAYIN_COMPLETED`, `EVENT_PIX_PAYIN_EXPIRED`, `EVENT_PIX_PAYOUT_CREATED`, `EVENT_PIX_PAYOUT_COMPLETED`, `EVENT_PIX_PAYOUT_FAILED`, `EVENT_CHECKOUT_PAID`.

## API surface

| Method | Path | Client |
|--------|------|--------|
| `GET` | `/ping` | `client.ping` |
| `POST` | `/cash-in/pix` | `client.cash_in.create(params, idempotency_key:)` |
| `GET` | `/cash-in/{id}` | `client.cash_in.retrieve(id)` |
| `POST` | `/pix/payouts/dict` | `client.cash_out.dict_check(pix_key, key_type: nil)` |
| `POST` | `/cash-out/pix` | `client.cash_out.create(params, idempotency_key:)` |
| `GET` | `/cash-out/{id}` | `client.cash_out.retrieve(id)` |
| `GET` | `/account/balances` | `client.balances.list` |
| `GET` | `/transactions` | `client.transactions.list(...)` |
| `GET` | `/transactions/{id}` | `client.transactions.retrieve(id)` |
| `GET` | `/wallets` | `client.wallets.list(network: nil)` |
| `GET` | `/wallets/{id}/balance` | `client.wallets.retrieve_balance(id)` |
| | webhooks CRUD | `client.webhooks.create/list/update/delete` |
| | verify delivery | `Clarian::Webhooks.verify_signature` / `extract_headers` / `sign_payload` |
| `POST` | `/pix/payins/{id}/simulate` | `client.sandbox.simulate_cash_in(id, status)` |
| `POST` | `/pix/payouts/{id}/simulate` | `client.sandbox.simulate_cash_out(id, status)` |
| `POST` | `/webhooks/{id}/test` | `client.sandbox.send_webhook_event(id, event_type)` |
| `POST` | `/webhooks/deliveries/{id}/resend` | `client.sandbox.resend_webhook_delivery(id)` |

## Development

```bash
cd ruby
ruby -Ilib -Itest test/all.rb
```
