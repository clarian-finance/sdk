"""End-to-end run of every SDK method against a sandbox API, plus a spec drift check.

Usage: CLARIAN_API_KEY=cl_test_sk_... CLARIAN_WORKSPACE_ID=... [CLARIAN_BASE_URL=http://localhost:3000] python e2e/sandbox.py
"""
import json
import os
import random
import sys
import time
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from clarian import Clarian, ClarianError  # noqa: E402

api_key = os.environ.get("CLARIAN_API_KEY", "")
workspace_id = os.environ.get("CLARIAN_WORKSPACE_ID", "")
if not api_key.startswith("cl_test_sk_") or not workspace_id:
    sys.exit("CLARIAN_API_KEY (cl_test_sk_...) and CLARIAN_WORKSPACE_ID are required")
clarian = Clarian(api_key, workspace_id, base_url=os.environ.get("CLARIAN_BASE_URL"))
spec = json.loads((Path(__file__).resolve().parents[2] / "openapi.json").read_text())
outcomes = []
drift = {}


def check_shape(schema, value):
    props = spec["components"]["schemas"].get(schema, {}).get("properties", {})
    rows = [r for r in (value if isinstance(value, list) else [value]) if isinstance(r, dict)]
    for row in rows[:5]:
        for k in row:
            if k not in props:
                drift.setdefault(schema, set()).add(f"+{k}")


def step(name, fn, schema=None):
    try:
        v = fn()
        if schema:
            check_shape(schema, v)
        outcomes.append((name, True, json.dumps(v)[:140]))
        return v
    except Exception as e:  # noqa: BLE001
        outcomes.append((name, False, f"{e.status} {e.code}" if isinstance(e, ClarianError) else repr(e)))
        return None


def expect_error(name, code, fn):
    try:
        fn()
        outcomes.append((name, False, "accepted, expected " + code))
    except Exception as e:  # noqa: BLE001
        got = e.code if isinstance(e, ClarianError) else str(e)
        outcomes.append((name, got == code, got))


def key():
    return str(uuid.uuid4())


def cpf():
    base = [random.randint(0, 9) for _ in range(9)]
    digit = lambda ds: (sum(d * (len(ds) + 1 - i) for i, d in enumerate(ds)) * 10) % 11 % 10  # noqa: E731
    d1 = digit(base)
    return "".join(map(str, base + [d1, digit(base + [d1])]))


def brl(rows):
    return next((int(r["balance_minor"]) for r in rows or [] if r["currency"] == "BRL" and r["kind"] == "available"), 0)


step("ping", clarian.ping)
before = step("balances.list", clarian.balances.list)

payin = step("pix.payins.create", lambda: clarian.pix.payins.create({"amount_minor": 10000, "currency": "BRL"}, key()), "Payment")
if payin and payin.get("id"):
    step("pix.payins.retrieve", lambda: clarian.pix.payins.retrieve(payin["id"]), "Payment")
    step("pix.payins.simulate", lambda: clarian.pix.payins.simulate(payin["id"]), "Payment")
after_in = step("balances after cash-in", clarian.balances.list)
outcomes.append(("cash-in credited 100.00", brl(after_in) - brl(before) == 10000, str(brl(after_in) - brl(before))))

step("pix.payouts.fee", lambda: clarian.pix.payouts.fee({"amount_minor": "1000"}), "PixFeeQuote")
step("pix.payouts.lookup", lambda: clarian.pix.payouts.lookup({"pix_key": "maria@example.com"}), "PixDictResult")
step("pix.payouts.list_accounts", clarian.pix.payouts.list_accounts, "PayoutAccount")
idem = key()
payout_body = {"amount_minor": 1000, "currency": "BRL", "pix_key": "maria@example.com"}
payout = step("pix.payouts.create", lambda: clarian.pix.payouts.create(payout_body, idem), "Payment")
replay = step("pix.payouts.create replay", lambda: clarian.pix.payouts.create(payout_body, idem))
outcomes.append(("payout replay returns same payment", bool(payout and replay and payout.get("id") == replay.get("id")),
                 f"{(payout or {}).get('id')} {(replay or {}).get('id')}"))
if payout and payout.get("id"):
    step("pix.payouts.retrieve", lambda: clarian.pix.payouts.retrieve(payout["id"]), "Payment")
    step("pix.payouts.simulate", lambda: clarian.pix.payouts.simulate(payout["id"]), "Payment")
after_out = step("balances after cash-out", clarian.balances.list)
outcomes.append(("cash-out debited 10.00 + fee", brl(after_in) - brl(after_out) > 1000, str(brl(after_in) - brl(after_out))))

step("conversions.prices", lambda: clarian.conversions.prices(["USDT"]))
quote = step("conversions.quote", lambda: clarian.conversions.quote({"base_currency": "BRL", "quote_currency": "USDT", "base_amount_minor": "5000"}), "Quote")
if quote and quote.get("id"):
    ex = step("conversions.execute", lambda: clarian.conversions.execute(quote["id"]))
    ledger = (ex or {}).get("ledger") or {}
    outcomes.append(("convert debited BRL, credited USDT", ledger.get("from_currency") == "BRL" and ledger.get("to_currency") == "USDT", json.dumps(ledger)))
    expect_error("conversions.execute twice", "quote_not_open", lambda: clarian.conversions.execute(quote["id"]))

wallets = step("wallets.list", clarian.wallets.list, "Wallet")
w = (wallets or [None])[0]
if w and w.get("id"):
    step("wallets.retrieve", lambda: clarian.wallets.retrieve(w["id"]), "Wallet")
    step("wallets.balance", lambda: clarian.wallets.balance(w["id"]))
    step("wallets.transactions", lambda: clarian.wallets.transactions(w["id"], limit=5), "Payment")
step("crypto.networks", clarian.crypto.networks, "PayableNetwork")

step("transactions.list", lambda: clarian.transactions.list(limit=5), "Payment")
if payin and payin.get("id"):
    step("transactions.retrieve", lambda: clarian.transactions.retrieve(payin["id"]), "Payment")
step("transactions.statement", lambda: clarian.transactions.statement(limit=5), "StatementLine")

customer = step("customers.create", lambda: clarian.customers.create({"kind": "person", "display_name": "SDK E2E", "document_type": "cpf", "document_number": cpf()}), "Customer")
step("customers.list", clarian.customers.list, "Customer")
if customer and customer.get("id"):
    step("customers.retrieve", lambda: clarian.customers.retrieve(customer["id"]), "Customer")
    step("customers.update", lambda: clarian.customers.update(customer["id"], {"display_name": "SDK E2E 2"}), "Customer")
step("kyc.list", clarian.kyc.list, "KycCase")

hook = step("webhooks.create", lambda: clarian.webhooks.create({"url": "https://hooks.example.com/clarian", "events": ["payment.completed"]}), "WebhookCreateResponse")
secret = (hook or {}).get("secret")
outcomes.append(("webhook secret returned once", isinstance(secret, str) and len(secret) > 20, "present" if secret else "missing"))
if hook and hook.get("id"):
    hid = hook["id"]
    step("webhooks.retrieve", lambda: clarian.webhooks.retrieve(hid), "Webhook")
    step("webhooks.update", lambda: clarian.webhooks.update(hid, {"events": ["payment.completed", "payment.failed"]}), "Webhook")
    step("webhooks.test", lambda: clarian.webhooks.test(hid))
    step("webhooks.list", clarian.webhooks.list, "Webhook")
    step("webhooks.deliveries", clarian.webhooks.deliveries, "WebhookDelivery")
    step("webhooks.delete", lambda: clarian.webhooks.delete(hid))
expect_error("webhooks.create private url", "invalid_webhook_url", lambda: clarian.webhooks.create({"url": "https://169.254.169.254/x"}))

link = step("payment_links.create", lambda: clarian.payment_links.create({"description": "SDK E2E", "amount_minor": "2500", "currency": "BRL"}), "PaymentLink")
step("payment_links.list", clarian.payment_links.list, "PaymentLink")
if link and link.get("id"):
    lid = link["id"]
    step("payment_links.retrieve", lambda: clarian.payment_links.retrieve(lid), "PaymentLink")
    step("payment_links.update", lambda: clarian.payment_links.update(lid, {"title": "SDK E2E 2"}), "PaymentLink")
    step("payment_links.splits", lambda: clarian.payment_links.splits(lid), "PaymentLinkSplit")
    step("payment_links.charge", lambda: clarian.payment_links.charge(lid, {"payer_name": "Maria Silva", "payer_document": "52998224725"}, key()), "CheckoutPaymentResult")
    if link.get("slug"):
        step("checkout.retrieve", lambda: clarian.checkout.retrieve(link["slug"]), "PublicCheckout")
step("payment_links.payments", lambda: clarian.payment_links.payments(limit=5)["items"], "Payment")
step("payment_links.stats", clarian.payment_links.stats)

sub = step("subaccounts.create", lambda: clarian.subaccounts.create({"display_name": f"SDK E2E {int(time.time() * 1000)}"}), "SubAccount")
step("subaccounts.list", clarian.subaccounts.list, "SubAccount")
if sub and sub.get("id"):
    sid = sub["id"]
    step("subaccounts.retrieve", lambda: clarian.subaccounts.retrieve(sid), "SubAccount")
    step("subaccounts.balances", lambda: clarian.subaccounts.balances(sid))
    step("subaccounts.transactions", lambda: clarian.subaccounts.transactions(sid), "Payment")
    step("for_subaccount().balances", lambda: clarian.for_subaccount(sid).balances.list())

step("whitelist.list", clarian.whitelist.list, "WhitelistEntry")
step("whitelist.settings", clarian.whitelist.settings, "WhitelistSettings")
step("bank_accounts.list", clarian.bank_accounts.list, "BankAccount")
step("api_keys.list", clarian.api_keys.list, "ApiKey")
step("api_keys.audit", lambda: clarian.api_keys.audit(limit=5), "ApiKeyAuditEntry")
step("settings.retrieve", clarian.settings.retrieve, "WorkspaceSettings")
step("notifications.list", clarian.notifications.list, "Notification")
step("team.list", clarian.team.list)
step("workflows.list", clarian.workflows.list)
step("workflows.runs", clarian.workflows.runs)
step("approvals.list", clarian.approvals.list)
step("approvals.policy", clarian.approvals.policy)
step("treasury.list", clarian.treasury.list)

expect_error("missing idempotency key", "idempotency_key is required: reuse the same key to retry safely",
             lambda: clarian.pix.payouts.create({"amount_minor": 1}, ""))

for name, ok, note in outcomes:
    print("PASS" if ok else "FAIL", name, "|", note)
print("\nSPEC DRIFT (keys returned but not in the schema):")
for name, keys in drift.items():
    print(" ", name, " ".join(sorted(keys)))
failed = sum(1 for o in outcomes if not o[1])
print(f"\n{len(outcomes) - failed}/{len(outcomes)} passed")
sys.exit(1 if failed else 0)
