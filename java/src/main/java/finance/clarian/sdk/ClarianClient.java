package finance.clarian.sdk;

import java.io.IOException;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.StringJoiner;
import java.util.concurrent.ThreadLocalRandom;

/**
 * Clarian API v2 client. Objects come back as {@code Map<String, Object>}, lists as
 * {@code List<Map<String, Object>>}; field names match the OpenAPI reference ({@code openapi.json}).
 * Amounts are integer minor units: pass a {@code String}, {@code int}/{@code long} or {@code BigInteger}, never a float.
 */
public final class ClarianClient {

    public static final String DEFAULT_BASE_URL = "https://api.clarian.finance";
    public static final Duration DEFAULT_TIMEOUT = Duration.ofSeconds(30);

    public enum Environment { LIVE, SANDBOX }

    public final Environment environment;
    public final Balances balances = new Balances();
    public final Wallets wallets = new Wallets();
    public final Transactions transactions = new Transactions();
    public final Pix pix = new Pix();
    public final Conversions conversions = new Conversions();
    public final Crypto crypto = new Crypto();
    public final Customers customers = new Customers();
    public final Kyc kyc = new Kyc();
    public final Whitelist whitelist = new Whitelist();
    public final BankAccounts bankAccounts = new BankAccounts();
    public final WebhookEndpoints webhooks = new WebhookEndpoints();
    public final PaymentLinks paymentLinks = new PaymentLinks();
    public final Checkout checkout = new Checkout();
    public final SubAccounts subaccounts = new SubAccounts();
    public final ApiKeys apiKeys = new ApiKeys();
    public final Settings settings = new Settings();
    public final Notifications notifications = new Notifications();
    public final Team team = new Team();
    public final Workflows workflows = new Workflows();
    public final Approvals approvals = new Approvals();
    public final Treasury treasury = new Treasury();

    private final Builder cfg;

    public ClarianClient(String apiKey, String workspaceId) {
        this(builder().apiKey(apiKey).workspaceId(workspaceId));
    }

    private ClarianClient(Builder b) {
        if (b.apiKey == null || b.apiKey.isEmpty()) throw new IllegalArgumentException("apiKey is required");
        if (b.workspaceId == null || b.workspaceId.isEmpty()) throw new IllegalArgumentException("workspaceId is required");
        if (b.apiKey.startsWith("cl_live_sk_")) environment = Environment.LIVE;
        else if (b.apiKey.startsWith("cl_test_sk_")) environment = Environment.SANDBOX;
        else throw new IllegalArgumentException("Invalid API key format: must start with cl_live_sk_ or cl_test_sk_");
        this.cfg = b.copy();
        this.cfg.baseUrl = cfg.baseUrl.replaceAll("/+$", "");
        if (cfg.transport == null) cfg.transport = defaultTransport();
    }

    public static Builder builder() {
        return new Builder();
    }

    /** Checks the key and workspace. */
    public Map<String, Object> ping() {
        return obj(request("GET", "ping", null, null, null, null));
    }

    /** A client that acts on one child workspace (sends {@code X-Subaccount-Id}). */
    public ClarianClient forSubaccount(String subaccountId) {
        Builder b = cfg.copy();
        b.subaccountId = subaccountId;
        return new ClarianClient(b);
    }

    // ---- HTTP -------------------------------------------------------------------------

    private static final Set<Integer> RETRY_STATUS = Set.of(429, 502, 503, 504);

    /** Calls {@code /v1/{live|test}/<path>} and returns the {@code data} member of the reply. */
    Object request(String method, String path, Map<String, ?> query, Object body, String idempotencyKey, String otpCode) {
        String url = cfg.baseUrl + "/v1/" + (environment == Environment.LIVE ? "live" : "test") + "/" + path + queryString(query);
        Map<String, String> headers = new LinkedHashMap<>();
        headers.put("Authorization", "Bearer " + cfg.apiKey);
        headers.put("X-Workspace-Id", cfg.workspaceId);
        if (body != null) headers.put("Content-Type", "application/json");
        if (cfg.subaccountId != null && !cfg.subaccountId.isEmpty()) headers.put("X-Subaccount-Id", cfg.subaccountId);
        if (idempotencyKey != null && !idempotencyKey.isEmpty()) headers.put("Idempotency-Key", idempotencyKey);
        if (otpCode != null && !otpCode.isEmpty()) headers.put("X-OTP-Code", otpCode);
        byte[] payload = body == null ? null : Json.encode(body).getBytes(StandardCharsets.UTF_8);
        // Only a request that cannot double-apply is retried: reads, deletes, or a create with an idempotency key.
        boolean retryable = method.equals("GET") || method.equals("DELETE") || headers.containsKey("Idempotency-Key");
        HttpTransport.Response resp = send(method, url, headers, payload, retryable ? cfg.maxRetries : 0);
        Map<String, Object> json = parseBody(resp.body());
        if (resp.status() < 200 || resp.status() >= 300) throw new ClarianException(resp.status(), json);
        return json.containsKey("data") ? json.get("data") : json;
    }

    private HttpTransport.Response send(String method, String url, Map<String, String> headers, byte[] body, int retries) {
        for (int attempt = 0; ; attempt++) {
            HttpTransport.Response resp = null;
            IOException error = null;
            try {
                resp = cfg.transport.exchange(method, url, headers, body, cfg.timeout);
            } catch (IOException e) {
                error = e;
            }
            boolean again = resp == null || RETRY_STATUS.contains(resp.status());
            if (!again || attempt >= retries) {
                if (error != null) throw new java.io.UncheckedIOException(error);
                return resp;
            }
            sleep(backoffMs(attempt, resp == null ? null : resp.headers().get("retry-after")));
        }
    }

    private static long backoffMs(int attempt, String retryAfter) {
        if (retryAfter != null) {
            try {
                double s = Double.parseDouble(retryAfter.trim());
                if (s >= 0 && Double.isFinite(s)) return (long) (Math.min(s, 30) * 1000);
            } catch (NumberFormatException ignored) {
                // HTTP-date form: fall back to jittered backoff
            }
        }
        return (long) (ThreadLocalRandom.current().nextDouble() * Math.min(8000, 500L << attempt));
    }

    private static void sleep(long ms) {
        try {
            Thread.sleep(ms);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("interrupted while waiting to retry", e);
        }
    }

    private static Map<String, Object> parseBody(byte[] bytes) {
        String text = new String(bytes, StandardCharsets.UTF_8);
        if (text.isEmpty()) return new LinkedHashMap<>();
        try {
            Object v = Json.decode(text);
            if (v instanceof Map<?, ?>) {
                @SuppressWarnings("unchecked") Map<String, Object> m = (Map<String, Object>) v;
                return m;
            }
            Map<String, Object> wrap = new LinkedHashMap<>();
            wrap.put("data", v);
            return wrap;
        } catch (IllegalArgumentException e) {
            Map<String, Object> m = new LinkedHashMap<>();
            m.put("error", "invalid_json_response");
            m.put("message", text.substring(0, Math.min(500, text.length())));
            return m;
        }
    }

    private static String queryString(Map<String, ?> query) {
        if (query == null) return "";
        StringJoiner j = new StringJoiner("&", "?", "");
        j.setEmptyValue("");
        query.forEach((k, v) -> {
            if (v != null) j.add(enc(k) + "=" + enc(String.valueOf(v)));
        });
        return j.toString();
    }

    private static String enc(String s) {
        return URLEncoder.encode(s, StandardCharsets.UTF_8).replace("+", "%20");
    }

    private static String seg(String id) {
        // Dots are encoded so "." and ".." never become path dot-segments.
        if (id == null || id.isEmpty()) throw new IllegalArgumentException("id is required");
        return enc(id).replace(".", "%2E");
    }

    private static HttpTransport defaultTransport() {
        HttpClient client = HttpClient.newHttpClient();
        return (method, url, headers, body, timeout) -> {
            HttpRequest.Builder rb = HttpRequest.newBuilder(URI.create(url)).timeout(timeout)
                    .method(method, body == null ? HttpRequest.BodyPublishers.noBody() : HttpRequest.BodyPublishers.ofByteArray(body));
            headers.forEach(rb::header);
            try {
                HttpResponse<byte[]> r = client.send(rb.build(), HttpResponse.BodyHandlers.ofByteArray());
                Map<String, String> h = new LinkedHashMap<>();
                r.headers().map().forEach((k, v) -> h.put(k.toLowerCase(Locale.ROOT), v.isEmpty() ? "" : v.get(0)));
                return new HttpTransport.Response(r.statusCode(), h, r.body());
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new IOException("interrupted", e);
            }
        };
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> obj(Object v) {
        return v instanceof Map ? (Map<String, Object>) v : Map.of();
    }

    @SuppressWarnings("unchecked")
    private static List<Map<String, Object>> asList(Object v) {
        return v instanceof List ? (List<Map<String, Object>>) v : List.of();
    }

    private static String requireKey(String idempotencyKey) {
        if (idempotencyKey == null || idempotencyKey.isEmpty()) {
            throw new IllegalArgumentException("idempotencyKey is required: reuse the same key to retry safely");
        }
        return idempotencyKey;
    }

    private Map<String, Object> get(String path) {
        return obj(request("GET", path, null, null, null, null));
    }

    private Map<String, Object> get(String path, Map<String, ?> query) {
        return obj(request("GET", path, query, null, null, null));
    }

    private List<Map<String, Object>> getList(String path) {
        return asList(request("GET", path, null, null, null, null));
    }

    private List<Map<String, Object>> getList(String path, Map<String, ?> query) {
        return asList(request("GET", path, query, null, null, null));
    }

    private Map<String, Object> send(String method, String path, Object body) {
        return obj(request(method, path, null, body, null, null));
    }

    private Map<String, Object> money(String path, Object body, String idempotencyKey) {
        return obj(request("POST", path, null, body, requireKey(idempotencyKey), null));
    }

    private static Map<String, Object> completed() {
        return Map.of("status", "completed");
    }

    private static Map<String, Object> limit(Integer limit) {
        Map<String, Object> q = new LinkedHashMap<>();
        q.put("limit", limit);
        return q;
    }

    // ---- Resources (same order and names as the TypeScript SDK) ------------------------

    public final class Balances {
        public List<Map<String, Object>> list() { return getList("balances"); }
    }

    public final class Wallets {
        public List<Map<String, Object>> list() { return getList("wallets"); }
        public Map<String, Object> create(Map<String, ?> body) { return send("POST", "wallets", body == null ? Map.of() : body); }
        public Map<String, Object> retrieve(String id) { return get("wallets/" + seg(id)); }
        public Map<String, Object> balance(String id) { return get("wallets/" + seg(id) + "/balance"); }
        public List<Map<String, Object>> transactions(String id, Integer limit) { return getList("wallets/" + seg(id) + "/transactions", limit(limit)); }
    }

    public final class Transactions {
        /** Params: {@code limit}, {@code before}. */
        public List<Map<String, Object>> list(Map<String, ?> params) { return getList("transactions", params); }
        public Map<String, Object> retrieve(String id) { return get("transactions/" + seg(id)); }
        /** Params: {@code limit}, {@code before}, {@code from}, {@code to}, {@code currency}, {@code kind}. */
        public List<Map<String, Object>> statement(Map<String, ?> params) { return getList("statement", params); }
    }

    public final class Pix {
        public final PixPayins payins = new PixPayins();
        public final PixPayouts payouts = new PixPayouts();
    }

    /** PIX cash-in: a charge the payer pays with a QR code / copia-e-cola. */
    public final class PixPayins {
        public Map<String, Object> create(Map<String, ?> body, String idempotencyKey) { return money("pix/payins", body, idempotencyKey); }
        public Map<String, Object> retrieve(String id) { return get("pix/payins/" + seg(id)); }
        /** Sandbox only: settle the pending charge. */
        public Map<String, Object> simulate(String id) { return simulate(id, completed()); }
        public Map<String, Object> simulate(String id, Map<String, ?> outcome) { return send("POST", "pix/payins/" + seg(id) + "/simulate", outcome); }
    }

    /** PIX cash-out to the workspace's registered settlement key (403 {@code settlement_key_required} until set). */
    public final class PixPayouts {
        public Map<String, Object> create(Map<String, ?> body, String idempotencyKey) { return money("pix/payouts", body, idempotencyKey); }
        public Map<String, Object> retrieve(String id) { return get("pix/payouts/" + seg(id)); }
        public Map<String, Object> fee(Map<String, ?> body) { return send("POST", "pix/payouts/fee", body); }
        public Map<String, Object> lookup(Map<String, ?> body) { return send("POST", "pix/payouts/dict", body); }
        public Map<String, Object> simulate(String id) { return simulate(id, completed()); }
        public Map<String, Object> simulate(String id, Map<String, ?> outcome) { return send("POST", "pix/payouts/" + seg(id) + "/simulate", outcome); }
        public List<Map<String, Object>> listAccounts() { return getList("pix/payout-accounts"); }
    }

    /** Direction is always base_currency to quote_currency: you pay base_amount_minor of base. */
    public final class Conversions {
        public Map<String, Object> quote(Map<String, ?> body) { return send("POST", "rfq/quote", body); }
        /** A quote converts once (409 quote_not_open, 410 quote_expired). Never retried automatically. */
        public Map<String, Object> execute(String quoteId) { return send("POST", "rfq/execute", Map.of("quote_id", quoteId)); }
        public Map<String, Object> prices(List<String> symbols) {
            Map<String, Object> q = new LinkedHashMap<>();
            q.put("symbols", symbols == null ? null : String.join(",", symbols));
            return get("prices", q);
        }
    }

    public final class Crypto {
        public Map<String, Object> send(Map<String, ?> body, String idempotencyKey) { return money("crypto/send", body, idempotencyKey); }
        public Map<String, Object> simulate(String id) { return simulate(id, completed()); }
        public Map<String, Object> simulate(String id, Map<String, ?> outcome) { return ClarianClient.this.send("POST", "crypto/send/" + seg(id) + "/simulate", outcome); }
        public List<Map<String, Object>> networks() { return getList("crypto/networks"); }
    }

    public final class Customers {
        public List<Map<String, Object>> list() { return getList("customers"); }
        public Map<String, Object> create(Map<String, ?> body) { return send("POST", "customers", body); }
        public Map<String, Object> retrieve(String id) { return get("customers/" + seg(id)); }
        public Map<String, Object> update(String id, Map<String, ?> body) { return send("PATCH", "customers/" + seg(id), body); }
    }

    public final class Kyc {
        public List<Map<String, Object>> list() { return getList("kyc"); }
        public Map<String, Object> create(Map<String, ?> body) { return send("POST", "kyc", body); }
        public Map<String, Object> retrieve(String id) { return get("kyc/" + seg(id)); }
        public List<Map<String, Object>> listRfi(String id) { return getList("kyc/" + seg(id) + "/rfi"); }
        public Map<String, Object> answerRfi(String id, Map<String, ?> body) { return send("POST", "kyc/" + seg(id) + "/rfi", body); }
    }

    public final class Whitelist {
        public List<Map<String, Object>> list() { return getList("whitelist"); }
        public Map<String, Object> settings() { return get("whitelist/settings"); }
    }

    public final class BankAccounts {
        public List<Map<String, Object>> list() { return getList("bank-accounts"); }
    }

    public final class WebhookEndpoints {
        public List<Map<String, Object>> list() { return getList("webhooks"); }
        /** The signing {@code secret} is in this reply only. Store it. */
        public Map<String, Object> create(Map<String, ?> body) { return send("POST", "webhooks", body); }
        public Map<String, Object> retrieve(String id) { return get("webhooks/" + seg(id)); }
        public Map<String, Object> update(String id, Map<String, ?> body) { return send("PATCH", "webhooks/" + seg(id), body); }
        public Map<String, Object> delete(String id) { return send("DELETE", "webhooks/" + seg(id), null); }
        /** Sandbox key only: queues a signed {@code test.ping}. */
        public Map<String, Object> test(String id) { return send("POST", "webhooks/" + seg(id) + "/test", null); }
        public List<Map<String, Object>> deliveries() { return getList("webhooks/deliveries"); }
        /** Sandbox key only. */
        public Map<String, Object> resend(String deliveryId) { return send("POST", "webhooks/deliveries/" + seg(deliveryId) + "/resend", null); }
    }

    public final class PaymentLinks {
        public List<Map<String, Object>> list() { return getList("payment-links"); }
        public Map<String, Object> create(Map<String, ?> body) { return send("POST", "payment-links", body); }
        public Map<String, Object> retrieve(String id) { return get("payment-links/" + seg(id)); }
        public Map<String, Object> update(String id, Map<String, ?> body) { return send("PATCH", "payment-links/" + seg(id), body); }
        public Map<String, Object> delete(String id) { return send("DELETE", "payment-links/" + seg(id), null); }
        /** Returns {@code {items, next_cursor}}; pass {@code next_cursor} back as {@code cursor}. */
        public Map<String, Object> payments(Map<String, ?> params) { return get("payment-links/payments", params); }
        public Map<String, Object> stats(Map<String, ?> params) { return get("payment-links/stats", params); }
        public List<Map<String, Object>> splits(String id) { return getList("payment-links/" + seg(id) + "/splits"); }
        public List<Map<String, Object>> replaceSplits(String id, Map<String, ?> body) { return asList(request("PUT", "payment-links/" + seg(id) + "/splits", null, body, null, null)); }
        public Map<String, Object> charge(String id, Map<String, ?> body, String idempotencyKey) { return money("payment-links/" + seg(id) + "/charges", body, idempotencyKey); }
    }

    public final class Checkout {
        public Map<String, Object> retrieve(String slug) { return get("checkout/" + seg(slug)); }
        public Map<String, Object> pay(String slug, Map<String, ?> body, String idempotencyKey) { return money("checkout/" + seg(slug) + "/pay", body, idempotencyKey); }
    }

    public final class SubAccounts {
        public List<Map<String, Object>> list() { return getList("subaccounts"); }
        public Map<String, Object> create(Map<String, ?> body) { return send("POST", "subaccounts", body); }
        public Map<String, Object> retrieve(String id) { return get("subaccounts/" + seg(id)); }
        public List<Map<String, Object>> balances(String id) { return getList("subaccounts/" + seg(id) + "/balances"); }
        public List<Map<String, Object>> transactions(String id, Integer limit) { return getList("subaccounts/" + seg(id) + "/transactions", limit(limit)); }
    }

    public final class ApiKeys {
        public List<Map<String, Object>> list() { return getList("api-keys"); }
        public List<Map<String, Object>> audit(Integer limit) { return getList("api-keys/audit", limit(limit)); }
    }

    public final class Settings {
        public Map<String, Object> retrieve() { return get("settings"); }
    }

    public final class Notifications {
        public List<Map<String, Object>> list() { return getList("notifications"); }
    }

    public final class Team {
        public List<Map<String, Object>> list() { return getList("team"); }
    }

    public final class Workflows {
        public List<Map<String, Object>> list() { return getList("workflows"); }
        public Map<String, Object> create(Map<String, ?> body) { return send("POST", "workflows", body); }
        public List<Map<String, Object>> runs() { return getList("workflows/runs"); }
    }

    public final class Approvals {
        public List<Map<String, Object>> list() { return getList("approvals"); }
        public Map<String, Object> policy() { return get("approval-policy"); }
    }

    public final class Treasury {
        public List<Map<String, Object>> list() { return getList("treasury"); }
        public Map<String, Object> create(Map<String, ?> body) { return send("POST", "treasury", body); }
    }

    // ---- Builder ----------------------------------------------------------------------

    public static final class Builder {
        private String apiKey;
        private String workspaceId;
        private String baseUrl = DEFAULT_BASE_URL;
        private Duration timeout = DEFAULT_TIMEOUT;
        private String subaccountId;
        private int maxRetries = 2;
        private HttpTransport transport;

        /** {@code cl_live_sk_...} (live) or {@code cl_test_sk_...} (sandbox). The key picks the environment. */
        public Builder apiKey(String v) { apiKey = v; return this; }
        public Builder workspaceId(String v) { workspaceId = v; return this; }
        /** API origin. Default {@code https://api.clarian.finance}. */
        public Builder baseUrl(String v) { if (v != null && !v.isEmpty()) baseUrl = v; return this; }
        public Builder timeout(Duration v) { timeout = v; return this; }
        /** Act on one child workspace. Same as {@code client.forSubaccount(id)}. */
        public Builder subaccountId(String v) { subaccountId = v; return this; }
        /** Retries for 429/502/503/504 and network errors on reads, deletes and idempotent creates. Default 2. */
        public Builder maxRetries(int v) { maxRetries = v; return this; }
        /** Custom transport (tests, proxies). */
        public Builder transport(HttpTransport v) { transport = v; return this; }

        public ClarianClient build() { return new ClarianClient(this); }

        private Builder copy() {
            Builder b = new Builder();
            b.apiKey = apiKey; b.workspaceId = workspaceId; b.baseUrl = baseUrl; b.timeout = timeout;
            b.subaccountId = subaccountId; b.maxRetries = maxRetries; b.transport = transport;
            return b;
        }
    }
}
