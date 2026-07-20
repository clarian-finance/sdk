package finance.clarian.sdk;

import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Objects;

/**
 * Clarian Finance API client.
 *
 * <p>Environment is inferred from the API key prefix:
 * {@code cl_test_sk_} → sandbox ({@link #BASE_URL_TEST}), otherwise live ({@link #BASE_URL_LIVE}).
 */
public final class ClarianClient {

    public static final String BASE_URL_LIVE =
            "https://api.clarian.finance/functions/v1/api-gateway/live";
    public static final String BASE_URL_TEST =
            "https://api.clarian.finance/functions/v1/api-gateway/test";

    public static final Duration DEFAULT_TIMEOUT = Duration.ofSeconds(30);

    private final String apiKey;
    private final String workspaceId;
    private final String baseUrl;
    private final Duration timeout;
    private final HttpTransport transport;

    public final CashInService cashIn;
    public final CashOutService cashOut;
    public final BalancesService balances;
    public final TransactionsService transactions;
    public final WalletsService wallets;
    public final WebhooksService webhooks;
    public final SandboxService sandbox;

    public ClarianClient(String apiKey, String workspaceId) {
        this(builder().apiKey(apiKey).workspaceId(workspaceId));
    }

    private ClarianClient(Builder b) {
        if (b.apiKey == null || b.apiKey.isEmpty()) {
            throw new IllegalArgumentException("apiKey is required");
        }
        if (b.workspaceId == null || b.workspaceId.isEmpty()) {
            throw new IllegalArgumentException("workspaceId is required");
        }
        this.apiKey = b.apiKey;
        this.workspaceId = b.workspaceId;
        String inferred = b.apiKey.startsWith("cl_test_sk_") ? BASE_URL_TEST : BASE_URL_LIVE;
        String base = b.baseUrl != null ? b.baseUrl : inferred;
        while (base.endsWith("/")) {
            base = base.substring(0, base.length() - 1);
        }
        this.baseUrl = base;
        this.timeout = b.timeout != null ? b.timeout : DEFAULT_TIMEOUT;
        this.transport = b.transport != null ? b.transport : defaultTransport();

        this.cashIn = new CashInService(this);
        this.cashOut = new CashOutService(this);
        this.balances = new BalancesService(this);
        this.transactions = new TransactionsService(this);
        this.wallets = new WalletsService(this);
        this.webhooks = new WebhooksService(this);
        this.sandbox = new SandboxService(this);
    }

    public static Builder builder() {
        return new Builder();
    }

    public String apiKey() {
        return apiKey;
    }

    public String workspaceId() {
        return workspaceId;
    }

    public String baseUrl() {
        return baseUrl;
    }

    public Duration timeout() {
        return timeout;
    }

    /** GET /ping: credential and workspace probe. */
    public Map<String, Object> ping() {
        return request("GET", "/ping", null, null);
    }

    Map<String, Object> request(
            String method,
            String path,
            Map<String, ?> body,
            String idempotencyKey
    ) {
        String url = baseUrl + path;
        Map<String, String> headers = new LinkedHashMap<>();
        headers.put("Authorization", "Bearer " + apiKey);
        headers.put("X-Workspace-Id", workspaceId);

        byte[] rawBody = null;
        if (body != null) {
            rawBody = Json.encode(body).getBytes(StandardCharsets.UTF_8);
            headers.put("Content-Type", "application/json");
        }
        if (idempotencyKey != null && !idempotencyKey.isEmpty()) {
            headers.put("Idempotency-Key", idempotencyKey);
        }

        HttpTransport.Response resp;
        try {
            resp = transport.exchange(method, url, headers, rawBody, timeout);
        } catch (IOException e) {
            throw new RuntimeException("clarian: request failed: " + e.getMessage(), e);
        }

        int status = resp.status();
        String respBody = new String(resp.body(), StandardCharsets.UTF_8);

        if (status < 200 || status >= 300) {
            throw new ClarianException(status, respBody);
        }
        if (respBody.isEmpty()) {
            return Map.of();
        }
        try {
            Object parsed = Json.decode(respBody);
            if (parsed instanceof Map<?, ?> map) {
                @SuppressWarnings("unchecked")
                Map<String, Object> cast = (Map<String, Object>) map;
                return cast;
            }
            Map<String, Object> wrap = new LinkedHashMap<>();
            wrap.put("data", parsed);
            return wrap;
        } catch (RuntimeException e) {
            throw new ClarianException(status, respBody);
        }
    }

    private static HttpTransport defaultTransport() {
        HttpClient client = HttpClient.newBuilder()
                .followRedirects(HttpClient.Redirect.NORMAL)
                .build();
        return (method, url, headers, body, timeout) -> {
            HttpRequest.Builder rb = HttpRequest.newBuilder()
                    .uri(URI.create(url))
                    .timeout(timeout);
            for (Map.Entry<String, String> h : headers.entrySet()) {
                rb.header(h.getKey(), h.getValue());
            }
            HttpRequest.BodyPublisher publisher = body == null
                    ? HttpRequest.BodyPublishers.noBody()
                    : HttpRequest.BodyPublishers.ofByteArray(body);
            rb.method(method, publisher);
            try {
                HttpResponse<byte[]> response =
                        client.send(rb.build(), HttpResponse.BodyHandlers.ofByteArray());
                return new HttpTransport.Response(response.statusCode(), response.body());
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new IOException("request interrupted", e);
            }
        };
    }

    public static final class Builder {
        private String apiKey;
        private String workspaceId;
        private String baseUrl;
        private Duration timeout;
        private HttpTransport transport;

        public Builder apiKey(String apiKey) {
            this.apiKey = apiKey;
            return this;
        }

        public Builder workspaceId(String workspaceId) {
            this.workspaceId = workspaceId;
            return this;
        }

        public Builder baseUrl(String baseUrl) {
            this.baseUrl = baseUrl;
            return this;
        }

        public Builder timeout(Duration timeout) {
            this.timeout = timeout;
            return this;
        }

        /** For tests: inject a fake transport (no network). */
        public Builder transport(HttpTransport transport) {
            this.transport = transport;
            return this;
        }

        public ClarianClient build() {
            Objects.requireNonNull(apiKey, "apiKey");
            Objects.requireNonNull(workspaceId, "workspaceId");
            return new ClarianClient(this);
        }
    }
}
