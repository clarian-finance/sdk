package finance.clarian.sdk;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.format.DateTimeParseException;
import java.util.Collection;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/**
 * Webhook signature helpers. Signature is hex(HMAC-SHA256(secret, timestamp + "." + body))
 * using the full {@code whsec_…} secret (prefix included).
 */
public final class Webhooks {

    public static final String HEADER_SIGNATURE = "X-Clarian-Signature";
    public static final String HEADER_TIMESTAMP = "X-Clarian-Timestamp";
    public static final String HEADER_EVENT = "X-Clarian-Event";
    public static final String HEADER_DELIVERY_ID = "X-Clarian-Delivery-Id";
    public static final String HEADER_IDEMPOTENCY_KEY = "X-Clarian-Idempotency-Key";
    public static final String HEADER_ATTEMPT = "X-Clarian-Attempt";

    public static final int DEFAULT_TOLERANCE_SECONDS = 300;

    private Webhooks() {}

    /**
     * Headers extracted from a Clarian webhook delivery (same names as the TypeScript SDK).
     */
    public record Headers(
            String signature,
            String timestamp,
            String event,
            String deliveryId,
            String idempotencyKey,
            String attempt
    ) {}

    /** Sign {@code timestamp + "." + payload} with HMAC-SHA256 and return hex. */
    public static String signPayload(String secret, String timestamp, byte[] payload) {
        String body = payload == null ? "" : new String(payload, StandardCharsets.UTF_8);
        return signPayload(secret, timestamp, body);
    }

    public static String signPayload(String secret, String timestamp, String payload) {
        if (secret == null) {
            secret = "";
        }
        if (timestamp == null) {
            timestamp = "";
        }
        if (payload == null) {
            payload = "";
        }
        try {
            Mac mac = Mac.getInstance("HmacSHA256");
            mac.init(new SecretKeySpec(secret.getBytes(StandardCharsets.UTF_8), "HmacSHA256"));
            byte[] dig = mac.doFinal((timestamp + "." + payload).getBytes(StandardCharsets.UTF_8));
            return toHex(dig);
        } catch (Exception e) {
            throw new IllegalStateException("HMAC-SHA256 failed", e);
        }
    }

    /**
     * Verify HMAC-SHA256 signature and timestamp freshness.
     * Returns false on missing inputs, bad signature, or stale/future timestamp.
     */
    public static boolean verifySignature(
            byte[] payload,
            String timestamp,
            String signature,
            String secret,
            int toleranceSeconds
    ) {
        String body = payload == null ? "" : new String(payload, StandardCharsets.UTF_8);
        return verifySignature(body, timestamp, signature, secret, toleranceSeconds);
    }

    public static boolean verifySignature(
            String payload,
            String timestamp,
            String signature,
            String secret
    ) {
        return verifySignature(payload, timestamp, signature, secret, DEFAULT_TOLERANCE_SECONDS);
    }

    public static boolean verifySignature(
            String payload,
            String timestamp,
            String signature,
            String secret,
            int toleranceSeconds
    ) {
        if (secret == null || secret.isEmpty()
                || signature == null || signature.isEmpty()
                || timestamp == null || timestamp.isEmpty()) {
            return false;
        }
        long eventEpoch;
        try {
            eventEpoch = parseTimestampEpochSeconds(timestamp);
        } catch (IllegalArgumentException e) {
            return false;
        }
        long now = Instant.now().getEpochSecond();
        long age = Math.abs(now - eventEpoch);
        if (age > toleranceSeconds) {
            return false;
        }
        String expected = signPayload(secret, timestamp, payload == null ? "" : payload);
        return MessageDigest.isEqual(
                expected.getBytes(StandardCharsets.UTF_8),
                signature.getBytes(StandardCharsets.UTF_8)
        );
    }

    /**
     * Read Clarian delivery headers (case-insensitive). Missing values become empty strings.
     *
     * @param headers map of header name to value or collection of values
     */
    public static Headers extractHeaders(Map<String, ?> headers) {
        if (headers == null) {
            return new Headers("", "", "", "", "", "");
        }
        Map<String, String> lower = new LinkedHashMap<>();
        for (Map.Entry<String, ?> e : headers.entrySet()) {
            if (e.getKey() == null) {
                continue;
            }
            lower.put(e.getKey().toLowerCase(Locale.ROOT), firstValue(e.getValue()));
        }
        return new Headers(
                get(lower, HEADER_SIGNATURE),
                get(lower, HEADER_TIMESTAMP),
                get(lower, HEADER_EVENT),
                get(lower, HEADER_DELIVERY_ID),
                get(lower, HEADER_IDEMPOTENCY_KEY),
                get(lower, HEADER_ATTEMPT)
        );
    }

    private static String get(Map<String, String> lower, String name) {
        String v = lower.get(name.toLowerCase(Locale.ROOT));
        return v == null ? "" : v;
    }

    private static String firstValue(Object value) {
        if (value == null) {
            return "";
        }
        if (value instanceof Collection<?> col) {
            if (col.isEmpty()) {
                return "";
            }
            Object first = col.iterator().next();
            return first == null ? "" : String.valueOf(first);
        }
        if (value instanceof String[] arr) {
            return arr.length == 0 || arr[0] == null ? "" : arr[0];
        }
        return String.valueOf(value);
    }

    static long parseTimestampEpochSeconds(String timestamp) {
        try {
            return Instant.parse(timestamp).getEpochSecond();
        } catch (DateTimeParseException ignored) {
            // fall through
        }
        try {
            String normalized = timestamp.endsWith("Z")
                    ? timestamp.substring(0, timestamp.length() - 1) + "+00:00"
                    : timestamp;
            return OffsetDateTime.parse(normalized).toInstant().getEpochSecond();
        } catch (DateTimeParseException ignored) {
            // fall through
        }
        try {
            return (long) Double.parseDouble(timestamp);
        } catch (NumberFormatException e) {
            throw new IllegalArgumentException("invalid webhook timestamp: " + timestamp);
        }
    }

    private static String toHex(byte[] bytes) {
        StringBuilder sb = new StringBuilder(bytes.length * 2);
        for (byte b : bytes) {
            sb.append(String.format("%02x", b & 0xff));
        }
        return sb.toString();
    }
}
