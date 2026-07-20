package finance.clarian.sdk;

import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Set;

/**
 * Sandbox-only test helpers. Refuse non-{@code cl_test_sk_} keys before any HTTP call.
 * Outside the test environment the gateway returns {@code 404 {error:"sandbox_only"}}.
 */
public final class SandboxService {

    /** Magic PIX key: sandbox payout fails and refunds. */
    public static final String SANDBOX_FAIL_PIX_KEY = "fail@sandbox.clarian";

    /** Magic PIX key: sandbox payout stays pending until simulated. */
    public static final String SANDBOX_PENDING_PIX_KEY = "pending@sandbox.clarian";

    public static final String EVENT_PIX_PAYIN_CREATED = "pix_payin.created";
    public static final String EVENT_PIX_PAYIN_COMPLETED = "pix_payin.completed";
    public static final String EVENT_PIX_PAYIN_EXPIRED = "pix_payin.expired";
    public static final String EVENT_PIX_PAYOUT_CREATED = "pix_payout.created";
    public static final String EVENT_PIX_PAYOUT_COMPLETED = "pix_payout.completed";
    public static final String EVENT_PIX_PAYOUT_FAILED = "pix_payout.failed";
    public static final String EVENT_CHECKOUT_PAID = "checkout.paid";

    private static final Set<String> CASH_IN_STATUSES = Set.of("completed", "expired", "failed");
    private static final Set<String> CASH_OUT_STATUSES = Set.of("completed", "failed");

    private final ClarianClient client;

    SandboxService(ClarianClient client) {
        this.client = client;
    }

    private void requireTestKey() {
        if (!client.apiKey().startsWith("cl_test_sk_")) {
            throw new IllegalStateException("sandbox helpers require a cl_test_sk_ key");
        }
    }

    /**
     * POST /pix/payins/{id}/simulate. Empty/null status defaults to {@code completed}.
     * Allowed: completed | expired | failed.
     */
    public Map<String, Object> simulateCashIn(String orderId, String status) {
        requireTestKey();
        String resolved = (status == null || status.isEmpty()) ? "completed" : status;
        if (!CASH_IN_STATUSES.contains(resolved)) {
            throw new IllegalArgumentException("invalid cash-in simulate status \"" + resolved + "\"");
        }
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("status", resolved);
        return client.request("POST", "/pix/payins/" + Paths.escape(orderId) + "/simulate", body, null);
    }

    public Map<String, Object> simulateCashIn(String orderId) {
        return simulateCashIn(orderId, null);
    }

    /**
     * POST /pix/payouts/{id}/simulate. Empty/null status defaults to {@code completed}.
     * Allowed: completed | failed.
     */
    public Map<String, Object> simulateCashOut(String orderId, String status) {
        requireTestKey();
        String resolved = (status == null || status.isEmpty()) ? "completed" : status;
        if (!CASH_OUT_STATUSES.contains(resolved)) {
            throw new IllegalArgumentException("invalid cash-out simulate status \"" + resolved + "\"");
        }
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("status", resolved);
        return client.request("POST", "/pix/payouts/" + Paths.escape(orderId) + "/simulate", body, null);
    }

    public Map<String, Object> simulateCashOut(String orderId) {
        return simulateCashOut(orderId, null);
    }

    /**
     * POST /webhooks/{id}/test: enqueue a sample delivery.
     * Returns a flattened map with {@code ok}, {@code environment}, {@code event_type},
     * {@code enqueued} (count), and {@code delivery_id}.
     */
    @SuppressWarnings("unchecked")
    public Map<String, Object> sendWebhookEvent(String subscriptionId, String eventType) {
        requireTestKey();
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("event_type", eventType);
        Map<String, Object> res =
                client.request("POST", "/webhooks/" + Paths.escape(subscriptionId) + "/test", body, null);
        Object enqueuedObj = res.get("enqueued");
        Map<String, Object> enqueued =
                enqueuedObj instanceof Map ? (Map<String, Object>) enqueuedObj : Map.of();
        Object deliveryId = enqueued.get("delivery_id");
        Object count = enqueued.get("enqueued");
        Map<String, Object> out = new LinkedHashMap<>();
        out.put("ok", res.get("ok"));
        out.put("environment", res.get("environment"));
        out.put("event_type", res.get("event_type"));
        out.put("enqueued", count == null ? 0L : count);
        out.put("delivery_id", deliveryId == null ? "" : String.valueOf(deliveryId));
        return out;
    }

    /** POST /webhooks/deliveries/{id}/resend; returns the new delivery id. */
    public String resendWebhookDelivery(String deliveryId) {
        requireTestKey();
        Map<String, Object> res = client.request(
                "POST",
                "/webhooks/deliveries/" + Paths.escape(deliveryId) + "/resend",
                null,
                null
        );
        Object id = res.get("delivery_id");
        return id == null ? "" : String.valueOf(id);
    }
}
