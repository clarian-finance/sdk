package finance.clarian.sdk;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.Duration;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.format.DateTimeParseException;
import java.util.HexFormat;
import java.util.Map;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/** Verifies Clarian webhook deliveries. */
public final class Webhooks {

    public static final Duration DEFAULT_TOLERANCE = Duration.ofMinutes(5);

    private Webhooks() {}

    /** Body of a delivery: {@code {id, type, created_at, data}}. */
    public record Event(String id, String type, String createdAt, Map<String, Object> data) {}

    /** Signs {@code timestamp + "." + body} exactly as Clarian does. For tests of your own handler. */
    public static String sign(String body, String timestamp, String secret) {
        try {
            Mac mac = Mac.getInstance("HmacSHA256");
            mac.init(new SecretKeySpec(secret.getBytes(StandardCharsets.UTF_8), "HmacSHA256"));
            return HexFormat.of().formatHex(mac.doFinal((timestamp + "." + body).getBytes(StandardCharsets.UTF_8)));
        } catch (java.security.GeneralSecurityException e) {
            throw new IllegalStateException(e);
        }
    }

    public static boolean verifySignature(String body, String signature, String timestamp, String secret) {
        return verifySignature(body, signature, timestamp, secret, DEFAULT_TOLERANCE);
    }

    public static boolean verifySignature(String body, String signature, String timestamp, String secret, Duration tolerance) {
        if (body == null || signature == null || timestamp == null || secret == null) return false;
        Instant sentAt;
        try {
            sentAt = OffsetDateTime.parse(timestamp).toInstant();
        } catch (DateTimeParseException e) {
            return false; // unparseable timestamp fails closed
        }
        if (Duration.between(sentAt, Instant.now()).abs().compareTo(tolerance) > 0) return false;
        return MessageDigest.isEqual(
                sign(body, timestamp, secret).getBytes(StandardCharsets.UTF_8),
                signature.getBytes(StandardCharsets.UTF_8));
    }

    /**
     * Verifies a delivery and returns its parsed body. {@code rawBody} must be the exact bytes received.
     * Header names are matched case-insensitively. Throws when the signature or timestamp is bad.
     * Dedupe on {@code event.id()}: a delivery can arrive more than once.
     */
    @SuppressWarnings("unchecked")
    public static Event constructEvent(String rawBody, Map<String, String> headers, String secret) {
        if (!verifySignature(rawBody, header(headers, "x-clarian-signature"), header(headers, "x-clarian-timestamp"), secret)) {
            throw new IllegalArgumentException("Invalid webhook signature or timestamp outside tolerance");
        }
        Map<String, Object> m = Json.decodeObject(rawBody);
        Object data = m.get("data");
        return new Event((String) m.get("id"), (String) m.get("type"), (String) m.get("created_at"),
                data instanceof Map ? (Map<String, Object>) data : Map.of());
    }

    private static String header(Map<String, String> headers, String name) {
        for (Map.Entry<String, String> e : headers.entrySet()) {
            if (e.getKey() != null && e.getKey().equalsIgnoreCase(name)) return e.getValue();
        }
        return null;
    }
}
