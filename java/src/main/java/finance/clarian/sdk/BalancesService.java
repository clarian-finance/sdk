package finance.clarian.sdk;

import java.util.Collections;
import java.util.List;
import java.util.Map;

/** Workspace ledger balances. */
public final class BalancesService {

    private final ClarianClient client;

    BalancesService(ClarianClient client) {
        this.client = client;
    }

    /** GET /account/balances. */
    @SuppressWarnings("unchecked")
    public List<Map<String, Object>> list() {
        Map<String, Object> res = client.request("GET", "/account/balances", null, null);
        Object bals = res.get("balances");
        if (bals instanceof List<?> list) {
            return (List<Map<String, Object>>) list;
        }
        return Collections.emptyList();
    }
}
