package finance.clarian.sdk;

import java.util.LinkedHashMap;
import java.util.Map;

/** PIX cash-out: DICT lookup, create payout, fetch order. */
public final class CashOutService {

    private final ClarianClient client;

    CashOutService(ClarianClient client) {
        this.client = client;
    }

    /**
     * POST /pix/payouts/dict: preview a PIX key owner before paying out.
     *
     * @param keyType optional: CPF | CNPJ | EMAIL | PHONE | EVP
     */
    public Map<String, Object> dictCheck(String pixKey, String keyType) {
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("pix_key", pixKey);
        if (keyType != null && !keyType.isEmpty()) {
            body.put("key_type", keyType);
        }
        return client.request("POST", "/pix/payouts/dict", body, null);
    }

    public Map<String, Object> dictCheck(String pixKey) {
        return dictCheck(pixKey, null);
    }

    /**
     * POST /cash-out/pix. {@code idempotencyKey} is required —
     * retries never double-send.
     */
    public Map<String, Object> create(Map<String, ?> params, String idempotencyKey) {
        return client.request("POST", "/cash-out/pix", params, idempotencyKey);
    }

    /** GET /cash-out/{id}. */
    public Map<String, Object> retrieve(String orderId) {
        return client.request("GET", "/cash-out/" + Paths.escape(orderId), null, null);
    }
}
