package finance.clarian.sdk;

import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;

/** Ledger transactions. */
public final class TransactionsService {

    private final ClarianClient client;

    TransactionsService(ClarianClient client) {
        this.client = client;
    }

    /**
     * GET /transactions with optional filters.
     *
     * @param type   pix_in | pix_out | … (null omitted)
     * @param status pending | processing | completed | failed | cancelled (null omitted)
     * @param limit  positive limit (null or &le;0 omitted)
     */
    @SuppressWarnings("unchecked")
    public List<Map<String, Object>> list(String type, String status, Integer limit) {
        Map<String, String> q = new LinkedHashMap<>();
        if (type != null && !type.isEmpty()) {
            q.put("type", type);
        }
        if (status != null && !status.isEmpty()) {
            q.put("status", status);
        }
        if (limit != null && limit > 0) {
            q.put("limit", String.valueOf(limit));
        }
        String path = "/transactions";
        if (!q.isEmpty()) {
            path = path + "?" + q.entrySet().stream()
                    .map(e -> urlEncode(e.getKey()) + "=" + urlEncode(e.getValue()))
                    .collect(Collectors.joining("&"));
        }
        Map<String, Object> res = client.request("GET", path, null, null);
        Object txs = res.get("transactions");
        if (txs instanceof List<?> list) {
            return (List<Map<String, Object>>) list;
        }
        return Collections.emptyList();
    }

    public List<Map<String, Object>> list() {
        return list(null, null, null);
    }

    /** GET /transactions/{id}; returns the nested transaction object when present. */
    @SuppressWarnings("unchecked")
    public Map<String, Object> retrieve(String transactionId) {
        Map<String, Object> res =
                client.request("GET", "/transactions/" + Paths.escape(transactionId), null, null);
        Object tx = res.get("transaction");
        if (tx instanceof Map) {
            return (Map<String, Object>) tx;
        }
        return res;
    }

    private static String urlEncode(String s) {
        return java.net.URLEncoder.encode(s, java.nio.charset.StandardCharsets.UTF_8);
    }
}
