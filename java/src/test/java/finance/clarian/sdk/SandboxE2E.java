package finance.clarian.sdk;

import java.math.BigInteger;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.Callable;
import java.util.concurrent.ThreadLocalRandom;

/**
 * End-to-end run of every SDK method against a sandbox API, plus a spec drift check. Port of e2e/sandbox.ts.
 * Usage: CLARIAN_API_KEY=cl_test_sk_... CLARIAN_WORKSPACE_ID=... [CLARIAN_BASE_URL=http://localhost:3000] gradle e2e
 */
final class SandboxE2E {
    record Outcome(String step, boolean ok, String note) {}

    static final List<Outcome> outcomes = new ArrayList<>();
    static final Map<String, Set<String>> drift = new LinkedHashMap<>();
    static Map<String, Object> schemas;

    @SuppressWarnings("unchecked")
    static void checkShape(String schema, Object value) {
        Object s = schemas.get(schema);
        Map<String, Object> props = s instanceof Map<?, ?> m && m.get("properties") instanceof Map<?, ?> p ? (Map<String, Object>) p : Map.of();
        List<?> rows = value instanceof List<?> l ? l : List.of(value == null ? List.of() : value);
        rows.stream().filter(r -> r instanceof Map).limit(5).forEach(r -> ((Map<String, Object>) r).keySet().forEach(k -> {
            if (!props.containsKey(k)) drift.computeIfAbsent(schema, x -> new LinkedHashSet<>()).add("+" + k);
        }));
    }

    static <T> T step(String name, Callable<T> fn) { return step(name, fn, null); }

    static <T> T step(String name, Callable<T> fn, String schema) {
        try {
            T v = fn.call();
            if (schema != null) checkShape(schema, v);
            String note = String.valueOf(v);
            outcomes.add(new Outcome(name, true, note.substring(0, Math.min(140, note.length()))));
            return v;
        } catch (ClarianException e) {
            outcomes.add(new Outcome(name, false, e.status() + " " + e.code()));
        } catch (Exception e) {
            outcomes.add(new Outcome(name, false, e.toString()));
        }
        return null;
    }

    static void expectError(String name, String code, Callable<?> fn) {
        try {
            fn.call();
            outcomes.add(new Outcome(name, false, "accepted, expected " + code));
        } catch (ClarianException e) {
            outcomes.add(new Outcome(name, e.code().equals(code), e.code()));
        } catch (Exception e) {
            outcomes.add(new Outcome(name, String.valueOf(e.getMessage()).equals(code), String.valueOf(e.getMessage())));
        }
    }

    static void check(String name, boolean ok, Object note) { outcomes.add(new Outcome(name, ok, String.valueOf(note))); }

    static String key() { return UUID.randomUUID().toString(); }

    // A fresh valid CPF per run: the API rejects a document already on file.
    static String cpf() {
        int[] d = new int[11];
        for (int i = 0; i < 9; i++) d[i] = ThreadLocalRandom.current().nextInt(10);
        for (int len = 9; len <= 10; len++) {
            int sum = 0;
            for (int i = 0; i < len; i++) sum += d[i] * (len + 1 - i);
            d[len] = (sum * 10) % 11 % 10;
        }
        StringBuilder sb = new StringBuilder();
        for (int x : d) sb.append(x);
        return sb.toString();
    }

    static BigInteger brl(List<Map<String, Object>> rows) {
        if (rows == null) return BigInteger.ZERO;
        return rows.stream().filter(r -> "BRL".equals(r.get("currency")) && "available".equals(r.get("kind")))
                .findFirst().map(r -> new BigInteger(String.valueOf(r.get("balance_minor")))).orElse(BigInteger.ZERO);
    }

    static String id(Map<String, Object> m) { return m == null ? null : (String) m.get("id"); }

    @SuppressWarnings("unchecked")
    public static void main(String[] args) throws Exception {
        String apiKey = Objects.requireNonNullElse(System.getenv("CLARIAN_API_KEY"), "").trim();
        String workspaceId = Objects.requireNonNullElse(System.getenv("CLARIAN_WORKSPACE_ID"), "");
        if (!apiKey.startsWith("cl_test_sk_") || workspaceId.isEmpty()) {
            System.err.println("CLARIAN_API_KEY (cl_test_sk_...) and CLARIAN_WORKSPACE_ID are required");
            System.exit(2);
        }
        ClarianClient clarian = ClarianClient.builder().apiKey(apiKey).workspaceId(workspaceId).baseUrl(System.getenv("CLARIAN_BASE_URL")).build();
        Map<String, Object> spec = Json.decodeObject(Files.readString(Path.of(args.length > 0 ? args[0] : "../openapi.json")));
        schemas = (Map<String, Object>) ((Map<String, Object>) spec.get("components")).get("schemas");

        step("ping", clarian::ping);
        var before = step("balances.list", clarian.balances::list);

        // Cash-in
        var payin = step("pix.payins.create", () -> clarian.pix.payins.create(Map.of("amount_minor", BigInteger.valueOf(10000), "currency", "BRL"), key()), "Payment");
        if (id(payin) != null) {
            step("pix.payins.retrieve", () -> clarian.pix.payins.retrieve(id(payin)), "Payment");
            step("pix.payins.simulate", () -> clarian.pix.payins.simulate(id(payin)), "Payment");
        }
        var afterIn = step("balances after cash-in", clarian.balances::list);
        check("cash-in credited 100.00", brl(afterIn).subtract(brl(before)).equals(BigInteger.valueOf(10000)), brl(afterIn).subtract(brl(before)));

        // Cash-out
        step("pix.payouts.fee", () -> clarian.pix.payouts.fee(Map.of("amount_minor", "1000")), "PixFeeQuote");
        step("pix.payouts.lookup", () -> clarian.pix.payouts.lookup(Map.of("pix_key", "maria@example.com")), "PixDictResult");
        step("pix.payouts.listAccounts", clarian.pix.payouts::listAccounts, "PayoutAccount");
        String idem = key();
        Map<String, Object> payoutBody = Map.of("amount_minor", 1000, "currency", "BRL", "pix_key", "maria@example.com");
        var payout = step("pix.payouts.create", () -> clarian.pix.payouts.create(payoutBody, idem), "Payment");
        var replay = step("pix.payouts.create replay", () -> clarian.pix.payouts.create(payoutBody, idem));
        check("payout replay returns same payment", id(payout) != null && id(payout).equals(id(replay)), id(payout) + " " + id(replay));
        if (id(payout) != null) {
            step("pix.payouts.retrieve", () -> clarian.pix.payouts.retrieve(id(payout)), "Payment");
            step("pix.payouts.simulate", () -> clarian.pix.payouts.simulate(id(payout)), "Payment");
        }
        var afterOut = step("balances after cash-out", clarian.balances::list);
        check("cash-out debited 10.00 + fee", brl(afterIn).subtract(brl(afterOut)).compareTo(BigInteger.valueOf(1000)) > 0, brl(afterIn).subtract(brl(afterOut)));

        // Quote and convert: pay 50.00 BRL for USDT
        step("conversions.prices", () -> clarian.conversions.prices(List.of("USDT")));
        var quote = step("conversions.quote", () -> clarian.conversions.quote(Map.of("base_currency", "BRL", "quote_currency", "USDT", "base_amount_minor", BigInteger.valueOf(5000))), "Quote");
        if (id(quote) != null) {
            var exec = step("conversions.execute", () -> clarian.conversions.execute(id(quote)));
            Map<String, Object> ledger = exec != null && exec.get("ledger") instanceof Map<?, ?> l ? (Map<String, Object>) l : Map.of();
            check("convert debited BRL, credited USDT", "BRL".equals(ledger.get("from_currency")) && "USDT".equals(ledger.get("to_currency")), ledger);
            expectError("conversions.execute twice", "quote_not_open", () -> clarian.conversions.execute(id(quote)));
        }

        // Wallets and crypto
        var wallets = step("wallets.list", clarian.wallets::list, "Wallet");
        String w = wallets == null || wallets.isEmpty() ? null : id(wallets.get(0));
        if (w != null) {
            step("wallets.retrieve", () -> clarian.wallets.retrieve(w), "Wallet");
            step("wallets.balance", () -> clarian.wallets.balance(w));
            step("wallets.transactions", () -> clarian.wallets.transactions(w, 5), "Payment");
        }
        step("crypto.networks", clarian.crypto::networks, "PayableNetwork");

        // Ledger views
        step("transactions.list", () -> clarian.transactions.list(Map.of("limit", 5)), "Payment");
        if (id(payin) != null) step("transactions.retrieve", () -> clarian.transactions.retrieve(id(payin)), "Payment");
        step("transactions.statement", () -> clarian.transactions.statement(Map.of("limit", 5)), "StatementLine");

        // Customers and KYC
        var customer = step("customers.create", () -> clarian.customers.create(Map.of("kind", "person", "display_name", "SDK E2E", "document_type", "cpf", "document_number", cpf())), "Customer");
        step("customers.list", clarian.customers::list, "Customer");
        if (id(customer) != null) {
            step("customers.retrieve", () -> clarian.customers.retrieve(id(customer)), "Customer");
            step("customers.update", () -> clarian.customers.update(id(customer), Map.of("display_name", "SDK E2E 2")), "Customer");
        }
        step("kyc.list", clarian.kyc::list, "KycCase");

        // Webhooks
        var hook = step("webhooks.create", () -> clarian.webhooks.create(Map.of("url", "https://hooks.example.com/clarian", "events", List.of("payment.completed"))), "WebhookCreateResponse");
        check("webhook secret returned once", hook != null && hook.get("secret") instanceof String s && s.length() > 20, hook != null && hook.get("secret") != null ? "present" : "missing");
        if (id(hook) != null) {
            step("webhooks.retrieve", () -> clarian.webhooks.retrieve(id(hook)), "Webhook");
            step("webhooks.update", () -> clarian.webhooks.update(id(hook), Map.of("events", List.of("payment.completed", "payment.failed"))), "Webhook");
            step("webhooks.test", () -> clarian.webhooks.test(id(hook)));
            step("webhooks.list", clarian.webhooks::list, "Webhook");
            step("webhooks.deliveries", clarian.webhooks::deliveries, "WebhookDelivery");
            step("webhooks.delete", () -> clarian.webhooks.delete(id(hook)));
        }
        expectError("webhooks.create private url", "invalid_webhook_url", () -> clarian.webhooks.create(Map.of("url", "https://169.254.169.254/x")));

        // Payment links and checkout
        var link = step("paymentLinks.create", () -> clarian.paymentLinks.create(Map.of("description", "SDK E2E", "amount_minor", "2500", "currency", "BRL")), "PaymentLink");
        step("paymentLinks.list", clarian.paymentLinks::list, "PaymentLink");
        if (id(link) != null) {
            step("paymentLinks.retrieve", () -> clarian.paymentLinks.retrieve(id(link)), "PaymentLink");
            step("paymentLinks.update", () -> clarian.paymentLinks.update(id(link), Map.of("title", "SDK E2E 2")), "PaymentLink");
            step("paymentLinks.splits", () -> clarian.paymentLinks.splits(id(link)), "PaymentLinkSplit");
            step("paymentLinks.charge", () -> clarian.paymentLinks.charge(id(link), Map.of("payer_name", "Maria Silva", "payer_document", "52998224725"), key()), "CheckoutPaymentResult");
            if (link.get("slug") instanceof String slug) step("checkout.retrieve", () -> clarian.checkout.retrieve(slug), "PublicCheckout");
        }
        step("paymentLinks.payments", () -> clarian.paymentLinks.payments(Map.of("limit", 5)).get("items"), "Payment");
        step("paymentLinks.stats", () -> clarian.paymentLinks.stats(null));

        // Sub-accounts
        var sub = step("subaccounts.create", () -> clarian.subaccounts.create(Map.of("display_name", "SDK E2E " + System.currentTimeMillis())), "SubAccount");
        step("subaccounts.list", clarian.subaccounts::list, "SubAccount");
        if (id(sub) != null) {
            step("subaccounts.retrieve", () -> clarian.subaccounts.retrieve(id(sub)), "SubAccount");
            step("subaccounts.balances", () -> clarian.subaccounts.balances(id(sub)));
            step("subaccounts.transactions", () -> clarian.subaccounts.transactions(id(sub), null), "Payment");
            step("forSubaccount().balances", () -> clarian.forSubaccount(id(sub)).balances.list());
        }

        // Workspace admin reads
        step("whitelist.list", clarian.whitelist::list, "WhitelistEntry");
        step("whitelist.settings", clarian.whitelist::settings, "WhitelistSettings");
        step("bankAccounts.list", clarian.bankAccounts::list, "BankAccount");
        step("apiKeys.list", clarian.apiKeys::list, "ApiKey");
        step("apiKeys.audit", () -> clarian.apiKeys.audit(5), "ApiKeyAuditEntry");
        step("settings.retrieve", clarian.settings::retrieve, "WorkspaceSettings");
        step("notifications.list", clarian.notifications::list, "Notification");
        step("team.list", clarian.team::list);
        step("workflows.list", clarian.workflows::list);
        step("workflows.runs", clarian.workflows::runs);
        step("approvals.list", clarian.approvals::list);
        step("approvals.policy", clarian.approvals::policy);
        step("treasury.list", clarian.treasury::list);

        // Client-side guards
        expectError("missing idempotency key", "idempotencyKey is required: reuse the same key to retry safely",
                () -> clarian.pix.payouts.create(Map.of("amount_minor", 1), ""));

        for (Outcome o : outcomes) System.out.println((o.ok() ? "PASS " : "FAIL ") + o.step() + " | " + o.note());
        System.out.println("\nSPEC DRIFT (keys returned but not in the schema):");
        drift.forEach((n, k) -> System.out.println("  " + n + " " + String.join(" ", k)));
        long failed = outcomes.stream().filter(o -> !o.ok()).count();
        System.out.println("\n" + (outcomes.size() - failed) + "/" + outcomes.size() + " passed");
        System.exit(failed == 0 ? 0 : 1);
    }
}
