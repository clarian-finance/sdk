# frozen_string_literal: true

# One class per API area, in the order of the public reference. Bodies and replies are
# plain Hashes with the API's snake_case keys (see openapi.json for the shapes).
module Clarian
  # Base: holds the Http and the path-segment escaper.
  class Resource
    def initialize(http)
      @http = http
    end

    private

    def get(path, query = nil) = @http.request("GET", path, query: query)
    def post(path, body = nil, **opts) = @http.request("POST", path, body: body, **opts)
    def patch(path, body) = @http.request("PATCH", path, body: body)
    def put(path, body) = @http.request("PUT", path, body: body)
    def delete_at(path) = @http.request("DELETE", path)
    def seg(id) = Http.seg(id)

    # Money-moving creates: a missing key raises before any network call.
    def with_key(idempotency_key)
      if idempotency_key.nil? || idempotency_key.to_s.empty?
        raise ArgumentError, "idempotency_key is required: reuse the same key to retry safely"
      end

      yield idempotency_key.to_s
    end
  end

  class Balances < Resource
    def list = get("balances")
  end

  class Wallets < Resource
    def list = get("wallets")
    def create(body = {}) = post("wallets", body)
    def retrieve(id) = get("wallets/#{seg(id)}")
    def balance(id) = get("wallets/#{seg(id)}/balance")
    def transactions(id, params = {}) = get("wallets/#{seg(id)}/transactions", params)
  end

  class Transactions < Resource
    def list(params = {}) = get("transactions", params)
    def retrieve(id) = get("transactions/#{seg(id)}")
    def statement(params = {}) = get("statement", params)
  end

  # PIX cash-in: a charge the payer pays with a QR code / copia-e-cola.
  class PixPayins < Resource
    def create(body, idempotency_key)
      with_key(idempotency_key) { |k| post("pix/payins", body, idempotency_key: k) }
    end

    def retrieve(id) = get("pix/payins/#{seg(id)}")
    # Sandbox only: settle or fail a pending charge.
    def simulate(id, body = { status: "completed" }) = post("pix/payins/#{seg(id)}/simulate", body)
  end

  # PIX cash-out. Over the API the workspace's registered settlement key receives the money
  # (403 settlement_key_required until one is set in the dashboard).
  class PixPayouts < Resource
    def create(body, idempotency_key)
      with_key(idempotency_key) { |k| post("pix/payouts", body, idempotency_key: k) }
    end

    def retrieve(id) = get("pix/payouts/#{seg(id)}")
    def fee(body) = post("pix/payouts/fee", body)
    def lookup(body) = post("pix/payouts/dict", body)
    def simulate(id, body = { status: "completed" }) = post("pix/payouts/#{seg(id)}/simulate", body)
    def list_accounts = get("pix/payout-accounts")
  end

  class Pix
    attr_reader :payins, :payouts

    def initialize(http)
      @payins = PixPayins.new(http)
      @payouts = PixPayouts.new(http)
    end
  end

  # Quote then convert. Direction is always base_currency -> quote_currency.
  class Conversions < Resource
    def quote(body) = post("rfq/quote", body)

    # A quote converts once (409 quote_not_open, 410 quote_expired). Never retried automatically.
    def execute(quote_id) = post("rfq/execute", { quote_id: quote_id })

    def prices(symbols = nil) = get("prices", { symbols: symbols&.join(",") })
  end

  # BRL moves between `available` and `trading`; conversions spend BRL from `trading`.
  class Trading < Resource
    def summary = get("trading/summary")

    # BRL available -> trading.
    def deposit(body, idempotency_key)
      with_key(idempotency_key) { |k| post("trading/deposits", body, idempotency_key: k) }
    end

    # BRL trading -> available.
    def withdraw(body, idempotency_key)
      with_key(idempotency_key) { |k| post("trading/withdrawals", body, idempotency_key: k) }
    end

    # USDT/USDC trading -> your own wallet on `network`. Body: amount_minor, currency?, network?
    def crypto_withdraw(body, idempotency_key)
      with_key(idempotency_key) { |k| post("trading/crypto-withdrawals", body, idempotency_key: k) }
    end
  end

  class Crypto < Resource
    def send(body, idempotency_key) # ponytail: shadows Object#send to match the TS name; use __send__ for dynamic dispatch
      with_key(idempotency_key) { |k| post("crypto/send", body, idempotency_key: k) }
    end

    def simulate(id, body = { status: "completed" }) = post("crypto/send/#{seg(id)}/simulate", body)
    def networks = get("crypto/networks")
  end

  class Customers < Resource
    def list = get("customers")
    def create(body) = post("customers", body)
    def retrieve(id) = get("customers/#{seg(id)}")
    def update(id, body) = patch("customers/#{seg(id)}", body)
  end

  class Kyc < Resource
    def list = get("kyc")
    def create(body) = post("kyc", body)
    def retrieve(id) = get("kyc/#{seg(id)}")
    def list_rfi(id) = get("kyc/#{seg(id)}/rfi")
    def answer_rfi(id, body) = post("kyc/#{seg(id)}/rfi", body)
  end

  class Whitelist < Resource
    def list = get("whitelist")
    def settings = get("whitelist/settings")
  end

  class BankAccounts < Resource
    def list = get("bank-accounts")
  end

  class Webhooks < Resource
    def list = get("webhooks")
    # The signing "secret" is in this reply only. Store it.
    def create(body) = post("webhooks", body)
    def retrieve(id) = get("webhooks/#{seg(id)}")
    def update(id, body) = patch("webhooks/#{seg(id)}", body)
    def delete(id) = delete_at("webhooks/#{seg(id)}")
    # Sandbox key only: queues a signed test.ping.
    def test(id) = post("webhooks/#{seg(id)}/test")
    def deliveries = get("webhooks/deliveries")
    def resend(delivery_id) = post("webhooks/deliveries/#{seg(delivery_id)}/resend")
  end

  class PaymentLinks < Resource
    def list = get("payment-links")
    def create(body) = post("payment-links", body)
    def retrieve(id) = get("payment-links/#{seg(id)}")
    def update(id, body) = patch("payment-links/#{seg(id)}", body)
    def delete(id) = delete_at("payment-links/#{seg(id)}")
    # Paginated: pass "next_cursor" back as cursor:.
    def payments(params = {}) = get("payment-links/payments", params)
    def stats(params = {}) = get("payment-links/stats", params)
    def splits(id) = get("payment-links/#{seg(id)}/splits")
    def replace_splits(id, body) = put("payment-links/#{seg(id)}/splits", body)

    def charge(id, body, idempotency_key)
      with_key(idempotency_key) { |k| post("payment-links/#{seg(id)}/charges", body, idempotency_key: k) }
    end
  end

  # The public checkout page of a payment link.
  class Checkout < Resource
    def retrieve(slug) = get("checkout/#{seg(slug)}")

    def pay(slug, body, idempotency_key)
      with_key(idempotency_key) { |k| post("checkout/#{seg(slug)}/pay", body, idempotency_key: k) }
    end
  end

  class SubAccounts < Resource
    def list = get("subaccounts")
    def create(body) = post("subaccounts", body)
    def retrieve(id) = get("subaccounts/#{seg(id)}")
    def balances(id) = get("subaccounts/#{seg(id)}/balances")
    def transactions(id, params = {}) = get("subaccounts/#{seg(id)}/transactions", params)
  end

  class ApiKeys < Resource
    def list = get("api-keys")
    def audit(params = {}) = get("api-keys/audit", params)
  end

  class Settings < Resource
    def retrieve = get("settings")
  end

  class Notifications < Resource
    def list = get("notifications")
  end

  class Team < Resource
    def list = get("team")
  end

  class Workflows < Resource
    def list = get("workflows")
    def create(body) = post("workflows", body)
    def runs = get("workflows/runs")
  end

  class Approvals < Resource
    def list = get("approvals")
    def policy = get("approval-policy")
  end

  class Treasury < Resource
    def list = get("treasury")
    def create(body) = post("treasury", body)
  end
end
