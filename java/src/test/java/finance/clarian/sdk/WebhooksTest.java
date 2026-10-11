package finance.clarian.sdk;

import static org.junit.jupiter.api.Assertions.*;

import java.time.Instant;
import java.util.Map;
import org.junit.jupiter.api.Test;

class WebhooksTest {
    static final String SECRET = "whsec_test";
    static final String BODY = "{\"id\":\"evt_1\",\"type\":\"payment.completed\",\"created_at\":\"2026-10-10T00:00:00Z\",\"data\":{\"payment_id\":\"p1\",\"amount_minor\":\"1000\"}}";

    @Test void validDelivery() {
        String ts = Instant.now().toString();
        Webhooks.Event e = Webhooks.constructEvent(BODY, Map.of("X-Clarian-Signature", Webhooks.sign(BODY, ts, SECRET), "x-clarian-timestamp", ts), SECRET);
        assertEquals("evt_1", e.id());
        assertEquals("payment.completed", e.type());
        assertEquals("2026-10-10T00:00:00Z", e.createdAt());
        assertEquals("p1", e.data().get("payment_id"));
    }

    @Test void badSignature() {
        String ts = Instant.now().toString();
        String sig = Webhooks.sign(BODY, ts, "other");
        assertFalse(Webhooks.verifySignature(BODY, sig, ts, SECRET));
        assertThrows(IllegalArgumentException.class, () -> Webhooks.constructEvent(BODY, Map.of("x-clarian-signature", sig, "x-clarian-timestamp", ts), SECRET));
        assertFalse(Webhooks.verifySignature(BODY + " ", Webhooks.sign(BODY, ts, SECRET), ts, SECRET));
    }

    @Test void staleTimestamp() {
        String ts = Instant.now().minusSeconds(301).toString();
        assertFalse(Webhooks.verifySignature(BODY, Webhooks.sign(BODY, ts, SECRET), ts, SECRET));
    }

    @Test void unparseableTimestampFailsClosed() {
        for (String ts : new String[] {"not-a-date", "", "1728518400"}) {
            assertFalse(Webhooks.verifySignature(BODY, Webhooks.sign(BODY, ts, SECRET), ts, SECRET), ts);
        }
        assertThrows(IllegalArgumentException.class, () -> Webhooks.constructEvent(BODY, Map.of(), SECRET));
    }
}
