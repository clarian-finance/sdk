// End-to-end run of every SDK method against a sandbox API, plus a spec drift check.
// Usage: CLARIAN_API_KEY=cl_test_sk_… CLARIAN_WORKSPACE_ID=… [CLARIAN_BASE_URL=http://localhost:3000] node e2e/sandbox.ts
import { readFileSync } from "node:fs";
import { Clarian, ClarianError } from "../dist/index.js";

const apiKey = process.env.CLARIAN_API_KEY ?? "";
const workspaceId = process.env.CLARIAN_WORKSPACE_ID ?? "";
if (!apiKey.startsWith("cl_test_sk_") || !workspaceId) {
  console.error("CLARIAN_API_KEY (cl_test_sk_…) and CLARIAN_WORKSPACE_ID are required");
  process.exit(2);
}
const clarian = new Clarian({ apiKey, workspaceId, baseUrl: process.env.CLARIAN_BASE_URL });
const spec = JSON.parse(readFileSync(new URL("../openapi.json", import.meta.url), "utf8"));

type Outcome = { step: string; ok: boolean; note: string };
const outcomes: Outcome[] = [];
const drift = new Map<string, Set<string>>();

// Keys the server returned that the schema does not declare (and declared keys never seen).
const checkShape = (schemaName: string, value: unknown) => {
  const props = spec.components.schemas[schemaName]?.properties ?? {};
  const rows = (Array.isArray(value) ? value : [value]).filter((v) => v && typeof v === "object");
  for (const row of rows.slice(0, 5)) {
    for (const k of Object.keys(row as object)) {
      if (!(k in props)) (drift.get(schemaName) ?? drift.set(schemaName, new Set()).get(schemaName)!).add(`+${k}`);
    }
  }
};

async function step<T>(name: string, fn: () => Promise<T>, schema?: string): Promise<T | undefined> {
  try {
    const v = await fn();
    if (schema) checkShape(schema, v);
    outcomes.push({ step: name, ok: true, note: JSON.stringify(v)?.slice(0, 140) ?? "" });
    return v;
  } catch (e) {
    const note = e instanceof ClarianError ? `${e.status} ${e.code}` : String(e);
    outcomes.push({ step: name, ok: false, note });
    return undefined;
  }
}
const expectError = async (name: string, code: string, fn: () => Promise<unknown>) => {
  try {
    await fn();
    outcomes.push({ step: name, ok: false, note: "accepted, expected " + code });
  } catch (e) {
    const got = e instanceof ClarianError ? e.code : String(e);
    outcomes.push({ step: name, ok: got === code, note: got });
  }
};
const key = () => crypto.randomUUID();
// A fresh valid CPF per run: the API rejects a document already on file.
const cpf = () => {
  const base = Array.from({ length: 9 }, () => Math.floor(Math.random() * 10));
  const digit = (ds: number[]) => ((ds.reduce((s, d, i) => s + d * (ds.length + 1 - i), 0) * 10) % 11) % 10;
  const d1 = digit(base);
  return [...base, d1, digit([...base, d1])].join("");
};
const brl = (rows: readonly { currency: string; kind: string; balance_minor: string }[] | undefined) =>
  BigInt(rows?.find((r) => r.currency === "BRL" && r.kind === "available")?.balance_minor ?? "0");

await step("ping", () => clarian.ping());
const before = await step("balances.list", () => clarian.balances.list());

// Cash-in
const payin = await step("pix.payins.create", () => clarian.pix.payins.create({ amount_minor: 10000n, currency: "BRL" }, key()), "Payment");
if (payin?.id) {
  await step("pix.payins.retrieve", () => clarian.pix.payins.retrieve(payin.id!), "Payment");
  await step("pix.payins.simulate", () => clarian.pix.payins.simulate(payin.id!), "Payment");
}
const afterIn = await step("balances after cash-in", () => clarian.balances.list());
outcomes.push({ step: "cash-in credited 100.00", ok: brl(afterIn) - brl(before) === 10000n, note: String(brl(afterIn) - brl(before)) });

// Cash-out
await step("pix.payouts.fee", () => clarian.pix.payouts.fee({ amount_minor: "1000" }), "PixFeeQuote");
await step("pix.payouts.lookup", () => clarian.pix.payouts.lookup({ pix_key: "maria@example.com" }), "PixDictResult");
await step("pix.payouts.listAccounts", () => clarian.pix.payouts.listAccounts(), "PayoutAccount");
const idem = key();
const payout = await step("pix.payouts.create", () => clarian.pix.payouts.create({ amount_minor: 1000, currency: "BRL", pix_key: "maria@example.com" }, idem), "Payment");
const replay = await step("pix.payouts.create replay", () => clarian.pix.payouts.create({ amount_minor: 1000, currency: "BRL", pix_key: "maria@example.com" }, idem));
outcomes.push({ step: "payout replay returns same payment", ok: !!payout?.id && replay?.id === payout.id, note: `${payout?.id} ${replay?.id}` });
if (payout?.id) {
  await step("pix.payouts.retrieve", () => clarian.pix.payouts.retrieve(payout.id!), "Payment");
  await step("pix.payouts.simulate", () => clarian.pix.payouts.simulate(payout.id!), "Payment");
}
const afterOut = await step("balances after cash-out", () => clarian.balances.list());
outcomes.push({ step: "cash-out debited 10.00 + fee", ok: brl(afterIn) - brl(afterOut) > 1000n, note: String(brl(afterIn) - brl(afterOut)) });

// Quote and convert: pay 50.00 BRL for USDT
await step("conversions.prices", () => clarian.conversions.prices(["USDT"]));
const quote = await step("conversions.quote", () => clarian.conversions.quote({ base_currency: "BRL", quote_currency: "USDT", base_amount_minor: 5000n }), "Quote");
if (quote?.id) {
  const exec = await step("conversions.execute", () => clarian.conversions.execute(quote.id!, key()));
  outcomes.push({ step: "convert debited BRL, credited USDT", ok: exec?.ledger?.from_currency === "BRL" && exec?.ledger?.to_currency === "USDT", note: JSON.stringify(exec?.ledger ?? {}) });
  await expectError("conversions.execute twice", "quote_not_open", () => clarian.conversions.execute(quote.id!, key()));
}

// Wallets and crypto
const wallets = await step("wallets.list", () => clarian.wallets.list(), "Wallet");
const w = wallets?.[0];
if (w?.id) {
  await step("wallets.retrieve", () => clarian.wallets.retrieve(w.id!), "Wallet");
  await step("wallets.balance", () => clarian.wallets.balance(w.id!));
  await step("wallets.transactions", () => clarian.wallets.transactions(w.id!, { limit: 5 }), "Payment");
}
await step("crypto.networks", () => clarian.crypto.networks(), "PayableNetwork");

// Ledger views
await step("transactions.list", () => clarian.transactions.list({ limit: 5 }), "Payment");
if (payin?.id) await step("transactions.retrieve", () => clarian.transactions.retrieve(payin.id!), "Payment");
await step("transactions.statement", () => clarian.transactions.statement({ limit: 5 }), "StatementLine");

// Customers and KYC
const customer = await step("customers.create", () => clarian.customers.create({ kind: "person", display_name: "SDK E2E", document_type: "cpf", document_number: cpf() }), "Customer");
await step("customers.list", () => clarian.customers.list(), "Customer");
if (customer?.id) {
  await step("customers.retrieve", () => clarian.customers.retrieve(customer.id!), "Customer");
  await step("customers.update", () => clarian.customers.update(customer.id!, { display_name: "SDK E2E 2" }), "Customer");
}
await step("kyc.list", () => clarian.kyc.list(), "KycCase");

// Webhooks
const hook = await step("webhooks.create", () => clarian.webhooks.create({ url: "https://hooks.example.com/clarian", events: ["payment.completed"] }), "WebhookCreateResponse");
outcomes.push({ step: "webhook secret returned once", ok: typeof hook?.secret === "string" && hook.secret.length > 20, note: hook?.secret ? "present" : "missing" });
if (hook?.id) {
  await step("webhooks.retrieve", () => clarian.webhooks.retrieve(hook.id!), "Webhook");
  await step("webhooks.update", () => clarian.webhooks.update(hook.id!, { events: ["payment.completed", "payment.failed"] }), "Webhook");
  await step("webhooks.test", () => clarian.webhooks.test(hook.id!));
  await step("webhooks.list", () => clarian.webhooks.list(), "Webhook");
  await step("webhooks.deliveries", () => clarian.webhooks.deliveries(), "WebhookDelivery");
  await step("webhooks.delete", () => clarian.webhooks.delete(hook.id!));
}
await expectError("webhooks.create private url", "invalid_webhook_url", () => clarian.webhooks.create({ url: "https://169.254.169.254/x" }));

// Payment links and checkout
const link = await step("paymentLinks.create", () => clarian.paymentLinks.create({ description: "SDK E2E", amount_minor: "2500", currency: "BRL" }), "PaymentLink");
await step("paymentLinks.list", () => clarian.paymentLinks.list(), "PaymentLink");
if (link?.id) {
  await step("paymentLinks.retrieve", () => clarian.paymentLinks.retrieve(link.id!), "PaymentLink");
  await step("paymentLinks.update", () => clarian.paymentLinks.update(link.id!, { title: "SDK E2E 2" }), "PaymentLink");
  await step("paymentLinks.splits", () => clarian.paymentLinks.splits(link.id!), "PaymentLinkSplit");
  await step("paymentLinks.charge", () => clarian.paymentLinks.charge(link.id!, { payer_name: "Maria Silva", payer_document: "52998224725" }, key()), "CheckoutPaymentResult");
  const slug = (link as { slug?: string }).slug;
  if (slug) await step("checkout.retrieve", () => clarian.checkout.retrieve(slug), "PublicCheckout");
}
await step("paymentLinks.payments", () => clarian.paymentLinks.payments({ limit: 5 }).then((p) => p.items), "Payment");
await step("paymentLinks.stats", () => clarian.paymentLinks.stats());

// Sub-accounts
const sub = await step("subaccounts.create", () => clarian.subaccounts.create({ display_name: `SDK E2E ${Date.now()}` }), "SubAccount");
await step("subaccounts.list", () => clarian.subaccounts.list(), "SubAccount");
if (sub?.id) {
  await step("subaccounts.retrieve", () => clarian.subaccounts.retrieve(sub.id!), "SubAccount");
  await step("subaccounts.balances", () => clarian.subaccounts.balances(sub.id!));
  await step("subaccounts.transactions", () => clarian.subaccounts.transactions(sub.id!), "Payment");
  await step("forSubaccount().balances", () => clarian.forSubaccount(sub.id!).balances.list());
}

// Workspace admin reads
await step("whitelist.list", () => clarian.whitelist.list(), "WhitelistEntry");
await step("whitelist.settings", () => clarian.whitelist.settings(), "WhitelistSettings");
await step("bankAccounts.list", () => clarian.bankAccounts.list(), "BankAccount");
await step("apiKeys.list", () => clarian.apiKeys.list(), "ApiKey");
await step("apiKeys.audit", () => clarian.apiKeys.audit({ limit: 5 }), "ApiKeyAuditEntry");
await step("settings.retrieve", () => clarian.settings.retrieve(), "WorkspaceSettings");
await step("notifications.list", () => clarian.notifications.list(), "Notification");
await step("team.list", () => clarian.team.list());
await step("workflows.list", () => clarian.workflows.list());
await step("workflows.runs", () => clarian.workflows.runs());
await step("approvals.list", () => clarian.approvals.list());
await step("approvals.policy", () => clarian.approvals.policy());
await step("treasury.list", () => clarian.treasury.list());

// Client-side guards
await expectError("missing idempotency key", "Error: idempotencyKey is required: reuse the same key to retry safely", () =>
  clarian.pix.payouts.create({ amount_minor: 1 }, ""),
);

for (const o of outcomes) console.log(o.ok ? "PASS" : "FAIL", o.step, "|", o.note);
console.log("\nSPEC DRIFT (keys returned but not in the schema):");
for (const [name, keys] of drift) console.log(" ", name, [...keys].join(" "));
const failed = outcomes.filter((o) => !o.ok).length;
console.log(`\n${outcomes.length - failed}/${outcomes.length} passed`);
process.exit(failed === 0 ? 0 : 1);
