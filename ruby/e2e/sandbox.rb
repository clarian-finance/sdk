# frozen_string_literal: true

# End-to-end run of every SDK method against a sandbox API, plus a spec drift check.
# Usage: CLARIAN_API_KEY=cl_test_sk_... CLARIAN_WORKSPACE_ID=... [CLARIAN_BASE_URL=http://localhost:3000] \
#   ruby -Ilib e2e/sandbox.rb
require "clarian"
require "json"
require "securerandom"

api_key = ENV.fetch("CLARIAN_API_KEY", "").strip
workspace_id = ENV.fetch("CLARIAN_WORKSPACE_ID", "")
unless api_key.start_with?("cl_test_sk_") && !workspace_id.empty?
  warn "CLARIAN_API_KEY (cl_test_sk_...) and CLARIAN_WORKSPACE_ID are required"
  exit 2
end
C = Clarian::Client.new(api_key: api_key, workspace_id: workspace_id, base_url: ENV["CLARIAN_BASE_URL"])
SPEC = JSON.parse(File.read(File.expand_path("../../openapi.json", __dir__)))
OUT = []
DRIFT = Hash.new { |h, k| h[k] = [] }

def check_shape(schema, value)
  props = SPEC.dig("components", "schemas", schema, "properties") || {}
  Array(value.is_a?(Array) ? value : [value]).grep(Hash).first(5).each do |row|
    row.each_key { |k| DRIFT[schema] |= ["+#{k}"] unless props.key?(k) }
  end
end

def step(name, schema = nil)
  v = yield
  check_shape(schema, v) if schema
  OUT << [name, true, JSON.generate(v)[0, 140]]
  v
rescue Clarian::ClarianError => e
  OUT << [name, false, "#{e.status} #{e.code}"]
  nil
rescue StandardError => e
  OUT << [name, false, "#{e.class}: #{e.message}"]
  nil
end

def expect_error(name, code)
  yield
  OUT << [name, false, "accepted, expected #{code}"]
rescue StandardError => e
  got = e.is_a?(Clarian::ClarianError) ? e.code : "Error: #{e.message}"
  OUT << [name, got == code, got]
end

def key = SecureRandom.uuid

def cpf
  base = Array.new(9) { rand(10) }
  digit = ->(ds) { (ds.each_with_index.sum { |d, i| d * (ds.length + 1 - i) } * 10) % 11 % 10 }
  d1 = digit.(base)
  (base + [d1, digit.(base + [d1])]).join
end

def brl(rows)
  Integer((rows || []).find { |r| r["currency"] == "BRL" && r["kind"] == "available" }&.dig("balance_minor") || "0")
end

step("ping") { C.ping }
before = step("balances.list") { C.balances.list }

payin = step("pix.payins.create", "Payment") { C.pix.payins.create({ amount_minor: 10_000, currency: "BRL" }, key) }
if payin&.dig("id")
  step("pix.payins.retrieve", "Payment") { C.pix.payins.retrieve(payin["id"]) }
  step("pix.payins.simulate", "Payment") { C.pix.payins.simulate(payin["id"]) }
end
after_in = step("balances after cash-in") { C.balances.list }
OUT << ["cash-in credited 100.00", brl(after_in) - brl(before) == 10_000, (brl(after_in) - brl(before)).to_s]

step("pix.payouts.fee", "PixFeeQuote") { C.pix.payouts.fee({ amount_minor: "1000" }) }
step("pix.payouts.lookup", "PixDictResult") { C.pix.payouts.lookup({ pix_key: "maria@example.com" }) }
step("pix.payouts.list_accounts", "PayoutAccount") { C.pix.payouts.list_accounts }
idem = key
payout_body = { amount_minor: 1000, currency: "BRL", pix_key: "maria@example.com" }
payout = step("pix.payouts.create", "Payment") { C.pix.payouts.create(payout_body, idem) }
replay = step("pix.payouts.create replay") { C.pix.payouts.create(payout_body, idem) }
OUT << ["payout replay returns same payment", !payout&.dig("id").nil? && replay&.dig("id") == payout["id"],
        "#{payout&.dig('id')} #{replay&.dig('id')}"]
if payout&.dig("id")
  step("pix.payouts.retrieve", "Payment") { C.pix.payouts.retrieve(payout["id"]) }
  step("pix.payouts.simulate", "Payment") { C.pix.payouts.simulate(payout["id"]) }
end
after_out = step("balances after cash-out") { C.balances.list }
OUT << ["cash-out debited 10.00 + fee", brl(after_in) - brl(after_out) > 1000, (brl(after_in) - brl(after_out)).to_s]

step("conversions.prices") { C.conversions.prices(["USDT"]) }
quote = step("conversions.quote", "Quote") do
  C.conversions.quote({ base_currency: "BRL", quote_currency: "USDT", base_amount_minor: 5000 })
end
if quote&.dig("id")
  exec = step("conversions.execute") { C.conversions.execute(quote["id"]) }
  ledger = exec&.dig("ledger") || {}
  OUT << ["convert debited BRL, credited USDT", ledger["from_currency"] == "BRL" && ledger["to_currency"] == "USDT",
          JSON.generate(ledger)]
  expect_error("conversions.execute twice", "quote_not_open") { C.conversions.execute(quote["id"]) }
end

wallets = step("wallets.list", "Wallet") { C.wallets.list }
if (w = wallets&.first&.dig("id"))
  step("wallets.retrieve", "Wallet") { C.wallets.retrieve(w) }
  step("wallets.balance") { C.wallets.balance(w) }
  step("wallets.transactions", "Payment") { C.wallets.transactions(w, limit: 5) }
end
step("crypto.networks", "PayableNetwork") { C.crypto.networks }

step("transactions.list", "Payment") { C.transactions.list(limit: 5) }
step("transactions.retrieve", "Payment") { C.transactions.retrieve(payin["id"]) } if payin&.dig("id")
step("transactions.statement", "StatementLine") { C.transactions.statement(limit: 5) }

customer = step("customers.create", "Customer") do
  C.customers.create({ kind: "person", display_name: "SDK E2E", document_type: "cpf", document_number: cpf })
end
step("customers.list", "Customer") { C.customers.list }
if customer&.dig("id")
  step("customers.retrieve", "Customer") { C.customers.retrieve(customer["id"]) }
  step("customers.update", "Customer") { C.customers.update(customer["id"], { display_name: "SDK E2E 2" }) }
end
step("kyc.list", "KycCase") { C.kyc.list }

hook = step("webhooks.create", "WebhookCreateResponse") do
  C.webhooks.create({ url: "https://hooks.example.com/clarian", events: ["payment.completed"] })
end
OUT << ["webhook secret returned once", hook&.dig("secret").to_s.length > 20, hook&.dig("secret") ? "present" : "missing"]
if (h = hook&.dig("id"))
  step("webhooks.retrieve", "Webhook") { C.webhooks.retrieve(h) }
  step("webhooks.update", "Webhook") { C.webhooks.update(h, { events: %w[payment.completed payment.failed] }) }
  step("webhooks.test") { C.webhooks.test(h) }
  step("webhooks.list", "Webhook") { C.webhooks.list }
  step("webhooks.deliveries", "WebhookDelivery") { C.webhooks.deliveries }
  step("webhooks.delete") { C.webhooks.delete(h) }
end
expect_error("webhooks.create private url", "invalid_webhook_url") { C.webhooks.create({ url: "https://169.254.169.254/x" }) }

link = step("payment_links.create", "PaymentLink") do
  C.payment_links.create({ description: "SDK E2E", amount_minor: "2500", currency: "BRL" })
end
step("payment_links.list", "PaymentLink") { C.payment_links.list }
if (l = link&.dig("id"))
  step("payment_links.retrieve", "PaymentLink") { C.payment_links.retrieve(l) }
  step("payment_links.update", "PaymentLink") { C.payment_links.update(l, { title: "SDK E2E 2" }) }
  step("payment_links.splits", "PaymentLinkSplit") { C.payment_links.splits(l) }
  step("payment_links.charge", "CheckoutPaymentResult") do
    C.payment_links.charge(l, { payer_name: "Maria Silva", payer_document: "52998224725" }, key)
  end
  step("checkout.retrieve", "PublicCheckout") { C.checkout.retrieve(link["slug"]) } if link["slug"]
end
step("payment_links.payments", "Payment") { C.payment_links.payments(limit: 5)["items"] }
step("payment_links.stats") { C.payment_links.stats }

sub = step("subaccounts.create", "SubAccount") { C.subaccounts.create({ display_name: "SDK E2E #{Time.now.to_i}" }) }
step("subaccounts.list", "SubAccount") { C.subaccounts.list }
if (s = sub&.dig("id"))
  step("subaccounts.retrieve", "SubAccount") { C.subaccounts.retrieve(s) }
  step("subaccounts.balances") { C.subaccounts.balances(s) }
  step("subaccounts.transactions", "Payment") { C.subaccounts.transactions(s) }
  step("for_subaccount.balances") { C.for_subaccount(s).balances.list }
end

step("whitelist.list", "WhitelistEntry") { C.whitelist.list }
step("whitelist.settings", "WhitelistSettings") { C.whitelist.settings }
step("bank_accounts.list", "BankAccount") { C.bank_accounts.list }
step("api_keys.list", "ApiKey") { C.api_keys.list }
step("api_keys.audit", "ApiKeyAuditEntry") { C.api_keys.audit(limit: 5) }
step("settings.retrieve", "WorkspaceSettings") { C.settings.retrieve }
step("notifications.list", "Notification") { C.notifications.list }
step("team.list") { C.team.list }
step("workflows.list") { C.workflows.list }
step("workflows.runs") { C.workflows.runs }
step("approvals.list") { C.approvals.list }
step("approvals.policy") { C.approvals.policy }
step("treasury.list") { C.treasury.list }

expect_error("missing idempotency key", "Error: idempotency_key is required: reuse the same key to retry safely") do
  C.pix.payouts.create({ amount_minor: 1 }, "")
end

OUT.each { |name, ok, note| puts "#{ok ? 'PASS' : 'FAIL'} #{name} | #{note}" }
puts "\nSPEC DRIFT (keys returned but not in the schema):"
DRIFT.each { |name, keys| puts "  #{name} #{keys.join(' ')}" }
failed = OUT.count { |_, ok, _| !ok }
puts "\n#{OUT.size - failed}/#{OUT.size} passed"
exit(failed.zero? ? 0 : 1)
