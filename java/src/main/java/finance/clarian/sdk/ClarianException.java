package finance.clarian.sdk;

import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Map;

/**
 * Thrown for any non-2xx HTTP response from the Clarian API.
 *
 * <p>The exception message truncates the raw body to 500 characters so logs stay
 * safe when bodies echo request fields (PIX keys, documents). Full body remains
 * available via {@link #getBody()}.
 */
public final class ClarianException extends RuntimeException {

    private final int status;
    private final String code;
    private final String message;
    private final Map<String, Object> meta;
    private final String body;

    public ClarianException(int status, String body) {
        super(buildExceptionMessage(status, body));
        String raw = body == null ? "" : body;
        String code = "";
        String detail = "";
        Map<String, Object> meta = new LinkedHashMap<>();

        if (!raw.isEmpty()) {
            try {
                Object parsed = Json.decode(raw);
                if (parsed instanceof Map<?, ?> map) {
                    Object errVal = map.get("error");
                    if (errVal != null) {
                        code = String.valueOf(errVal);
                    }
                    Object detailVal = map.get("detail");
                    if (detailVal != null) {
                        detail = String.valueOf(detailVal);
                    }
                    for (Map.Entry<?, ?> e : map.entrySet()) {
                        String k = String.valueOf(e.getKey());
                        if ("error".equals(k) || "detail".equals(k)) {
                            continue;
                        }
                        meta.put(k, e.getValue());
                    }
                }
            } catch (RuntimeException ignored) {
                // non-JSON body
            }
        }

        String truncated = raw.length() <= 500 ? raw : raw.substring(0, 500);
        String msg;
        if (!detail.isEmpty()) {
            msg = detail;
        } else if (!code.isEmpty()) {
            msg = code;
        } else {
            msg = truncated;
        }

        this.status = status;
        this.code = code;
        this.message = msg;
        this.meta = Collections.unmodifiableMap(meta);
        this.body = raw;
    }

    private static String buildExceptionMessage(int status, String body) {
        String raw = body == null ? "" : body;
        String code = "";
        if (!raw.isEmpty()) {
            try {
                Object parsed = Json.decode(raw);
                if (parsed instanceof Map<?, ?> map && map.get("error") != null) {
                    code = String.valueOf(map.get("error"));
                }
            } catch (RuntimeException ignored) {
                // keep empty code
            }
        }
        String truncated = raw.length() <= 500 ? raw : raw.substring(0, 500);
        if (!code.isEmpty()) {
            return "HTTP " + status + ": " + code;
        }
        if (!truncated.isEmpty()) {
            return "HTTP " + status + ": " + truncated;
        }
        return "HTTP " + status;
    }

    public int getStatus() {
        return status;
    }

    public String getCode() {
        return code;
    }

    /** Human-readable message (detail, code, or truncated body). */
    public String getErrorMessage() {
        return message;
    }

    /** Remaining JSON fields after error/detail are peeled off (includes hint). */
    public Map<String, Object> getMeta() {
        return meta;
    }

    /** Full raw response body (untruncated). */
    public String getBody() {
        return body;
    }
}
