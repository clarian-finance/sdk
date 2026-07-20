package finance.clarian.sdk;

import java.util.Map;

/** PIX cash-in: generate and fetch dynamic charges. */
public final class CashInService {

    private final ClarianClient client;

    CashInService(ClarianClient client) {
        this.client = client;
    }

    /**
     * POST /cash-in/pix. {@code idempotencyKey} is required by the API —
     * retries with the same key return the original order.
     */
    public Map<String, Object> create(Map<String, ?> params, String idempotencyKey) {
        return client.request("POST", "/cash-in/pix", params, idempotencyKey);
    }

    /** GET /cash-in/{id}. */
    public Map<String, Object> retrieve(String orderId) {
        return client.request("GET", "/cash-in/" + Paths.escape(orderId), null, null);
    }
}
