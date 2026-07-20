package finance.clarian.sdk;

import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.Collections;
import java.util.List;
import java.util.Map;

/** On-chain (Turnkey) wallets — crypto, separate from the BRL ledger. */
public final class WalletsService {

    private final ClarianClient client;

    WalletsService(ClarianClient client) {
        this.client = client;
    }

    /** GET /wallets with optional network filter. */
    @SuppressWarnings("unchecked")
    public List<Map<String, Object>> list(String network) {
        String path = "/wallets";
        if (network != null && !network.isEmpty()) {
            path = path + "?network=" + URLEncoder.encode(network, StandardCharsets.UTF_8);
        }
        Map<String, Object> res = client.request("GET", path, null, null);
        Object wallets = res.get("wallets");
        if (wallets instanceof List<?> list) {
            return (List<Map<String, Object>>) list;
        }
        return Collections.emptyList();
    }

    public List<Map<String, Object>> list() {
        return list(null);
    }

    /** GET /wallets/{id}/balance. */
    public Map<String, Object> retrieveBalance(String walletId) {
        return client.request("GET", "/wallets/" + Paths.escape(walletId) + "/balance", null, null);
    }
}
