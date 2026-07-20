package finance.clarian.sdk;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.time.Instant;
import java.time.temporal.ChronoUnit;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

class WebhooksTest {

    private String secret;
    private byte[] body;
    private String ts;

    @BeforeEach
    void setUp() {
        secret = "whsec_test_secret";
        body = (
                "{\"id\":\"evt_1\",\"type\":\"pix_payin.completed\","
                        + "\"created_at\":\"2026-01-01T00:00:00Z\",\"environment\":\"sandbox\","
                        + "\"data\":{\"transaction_id\":\"ord_abc\",\"status\":\"completed\","
                        + "\"amount\":19.50,\"fee\":0.37,\"currency\":\"BRL\"}}"
        ).getBytes(java.nio.charset.StandardCharsets.UTF_8);
        ts = Instant.now().toString();
    }

    @Test
    void signVerifyRoundtrip() {
        String sig = Webhooks.signPayload(secret, ts, body);
        assertTrue(Webhooks.verifySignature(body, ts, sig, secret, Webhooks.DEFAULT_TOLERANCE_SECONDS));
        String sig2 = Webhooks.signPayload(secret, ts, new String(body));
        assertTrue(Webhooks.verifySignature(new String(body), ts, sig2, secret));
    }

    @Test
    void tamperedBodyRejected() {
        String sig = Webhooks.signPayload(secret, ts, body);
        byte[] tampered = new String(body).replace("ord_abc", "ord_evil")
                .getBytes(java.nio.charset.StandardCharsets.UTF_8);
        assertFalse(Webhooks.verifySignature(tampered, ts, sig, secret, Webhooks.DEFAULT_TOLERANCE_SECONDS));
    }

    @Test
    void wrongSecretRejected() {
        String sig = Webhooks.signPayload("whsec_wrong", ts, body);
        assertFalse(Webhooks.verifySignature(body, ts, sig, secret, Webhooks.DEFAULT_TOLERANCE_SECONDS));
    }

    @Test
    void staleTimestampRejected() {
        String stale = Instant.now().minus(6, ChronoUnit.MINUTES).toString();
        String sig = Webhooks.signPayload(secret, stale, body);
        assertFalse(Webhooks.verifySignature(body, stale, sig, secret, Webhooks.DEFAULT_TOLERANCE_SECONDS));
    }

    @Test
    void futureTimestampBeyondToleranceRejected() {
        String future = Instant.now().plus(6, ChronoUnit.MINUTES).toString();
        String sig = Webhooks.signPayload(secret, future, body);
        assertFalse(Webhooks.verifySignature(body, future, sig, secret, Webhooks.DEFAULT_TOLERANCE_SECONDS));
    }

    @Test
    void customTolerance() {
        String old = Instant.now().minus(10, ChronoUnit.SECONDS).toString();
        String sig = Webhooks.signPayload(secret, old, body);
        assertFalse(Webhooks.verifySignature(body, old, sig, secret, 5));
        assertTrue(Webhooks.verifySignature(body, old, sig, secret, 60));
    }

    @Test
    void missingInputsRejected() {
        String sig = Webhooks.signPayload(secret, ts, body);
        assertFalse(Webhooks.verifySignature(body, ts, sig, "", Webhooks.DEFAULT_TOLERANCE_SECONDS));
        assertFalse(Webhooks.verifySignature(body, "", sig, secret, Webhooks.DEFAULT_TOLERANCE_SECONDS));
        assertFalse(Webhooks.verifySignature(body, ts, "", secret, Webhooks.DEFAULT_TOLERANCE_SECONDS));
    }

    @Test
    void extractHeaders() {
        Map<String, String> headers = Map.of(
                Webhooks.HEADER_TIMESTAMP, ts,
                Webhooks.HEADER_SIGNATURE, "abc123",
                Webhooks.HEADER_EVENT, "pix_payin.completed"
        );
        Webhooks.Headers h = Webhooks.extractHeaders(headers);
        assertEquals(ts, h.timestamp());
        assertEquals("abc123", h.signature());
        assertEquals("pix_payin.completed", h.event());
    }

    @Test
    void extractHeadersCaseInsensitive() {
        Webhooks.Headers h = Webhooks.extractHeaders(Map.of(
                "x-clarian-timestamp", "t1",
                "x-clarian-signature", "s1"
        ));
        assertEquals("t1", h.timestamp());
        assertEquals("s1", h.signature());
    }

    @Test
    void extractHeadersListValues() {
        Webhooks.Headers h = Webhooks.extractHeaders(Map.of(
                "X-Clarian-Timestamp", List.of("t2"),
                "X-Clarian-Signature", List.of("s2")
        ));
        assertEquals("t2", h.timestamp());
        assertEquals("s2", h.signature());
    }

    @Test
    void unixTimestampAccepted() {
        String now = String.valueOf(Instant.now().getEpochSecond());
        String sig = Webhooks.signPayload(secret, now, body);
        assertTrue(Webhooks.verifySignature(body, now, sig, secret, Webhooks.DEFAULT_TOLERANCE_SECONDS));
    }
}
