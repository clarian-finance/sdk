package finance.clarian.sdk;

import java.util.LinkedHashMap;
import java.util.Map;

/** Every non-2xx reply. {@code code} is the API's stable {@code error} string, e.g. {@code insufficient_balance}. */
public final class ClarianException extends RuntimeException {

    private final int status;
    private final String code;
    private final Map<String, Object> meta;

    public ClarianException(int status, Map<String, Object> body) {
        super(body.get("message") instanceof String m ? m : codeOf(body));
        this.status = status;
        this.code = codeOf(body);
        Map<String, Object> rest = new LinkedHashMap<>(body);
        rest.remove("error");
        rest.remove("message");
        this.meta = java.util.Collections.unmodifiableMap(rest);
    }

    private static String codeOf(Map<String, Object> body) {
        return body.get("error") instanceof String e ? e : "unknown_error";
    }

    public int status() {
        return status;
    }

    public String code() {
        return code;
    }

    /** The reply's other fields. */
    public Map<String, Object> meta() {
        return meta;
    }
}
