package finance.clarian.sdk;

import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/** Webhook subscription CRUD. */
public final class WebhooksService {

    private final ClarianClient client;

    WebhooksService(ClarianClient client) {
        this.client = client;
    }

    /** GET /webhooks. */
    @SuppressWarnings("unchecked")
    public List<Map<String, Object>> list() {
        Map<String, Object> res = client.request("GET", "/webhooks", null, null);
        Object subs = res.get("subscriptions");
        if (subs instanceof List<?> list) {
            return (List<Map<String, Object>>) list;
        }
        return Collections.emptyList();
    }

    /**
     * POST /webhooks. Secret is a sibling of subscription in the 201 body and is
     * shown only once — store it. Flattened into the returned map as {@code secret}.
     */
    @SuppressWarnings("unchecked")
    public Map<String, Object> create(Map<String, ?> params) {
        Map<String, Object> res = client.request("POST", "/webhooks", params, null);
        Object sub = res.get("subscription");
        Map<String, Object> out =
                sub instanceof Map ? new LinkedHashMap<>((Map<String, Object>) sub) : new LinkedHashMap<>();
        if (res.containsKey("secret")) {
            out.put("secret", res.get("secret"));
        }
        return out;
    }

    /** PATCH /webhooks/{id}. */
    @SuppressWarnings("unchecked")
    public Map<String, Object> update(String webhookId, Map<String, ?> params) {
        Map<String, Object> res =
                client.request("PATCH", "/webhooks/" + Paths.escape(webhookId), params, null);
        Object sub = res.get("subscription");
        if (sub instanceof Map) {
            return (Map<String, Object>) sub;
        }
        return res;
    }

    /** DELETE /webhooks/{id}. */
    public void delete(String webhookId) {
        client.request("DELETE", "/webhooks/" + Paths.escape(webhookId), null, null);
    }
}
