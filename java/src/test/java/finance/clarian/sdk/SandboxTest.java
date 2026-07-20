package finance.clarian.sdk;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.Map;
import org.junit.jupiter.api.Test;

class SandboxTest {

    @Test
    void liveKeyRejectedBeforeNetwork() {
        HttpTransport fail = (method, url, headers, body, timeout) -> {
            throw new AssertionError("unexpected HTTP call");
        };
        ClarianClient c = ClarianClient.builder()
                .apiKey("cl_live_sk_x")
                .workspaceId("ws")
                .baseUrl("https://mock")
                .transport(fail)
                .build();

        IllegalStateException e1 = assertThrows(
                IllegalStateException.class,
                () -> c.sandbox.simulateCashIn("ord_1", "completed")
        );
        assertTrue(e1.getMessage().contains("cl_test_sk_"));

        assertThrows(IllegalStateException.class, () -> c.sandbox.simulateCashOut("out_1", "completed"));
        assertThrows(
                IllegalStateException.class,
                () -> c.sandbox.sendWebhookEvent("wh1", SandboxService.EVENT_PIX_PAYIN_COMPLETED)
        );
        assertThrows(IllegalStateException.class, () -> c.sandbox.resendWebhookDelivery("del_1"));
    }

    @Test
    void invalidStatusNoHttp() {
        HttpTransport fail = (method, url, headers, body, timeout) -> {
            throw new AssertionError("unexpected HTTP call");
        };
        ClarianClient c = ClarianClient.builder()
                .apiKey("cl_test_sk_x")
                .workspaceId("ws")
                .baseUrl("https://mock")
                .transport(fail)
                .build();

        IllegalArgumentException e1 = assertThrows(
                IllegalArgumentException.class,
                () -> c.sandbox.simulateCashIn("ord_1", "bogus")
        );
        assertTrue(e1.getMessage().contains("invalid cash-in"));

        IllegalArgumentException e2 = assertThrows(
                IllegalArgumentException.class,
                () -> c.sandbox.simulateCashOut("out_1", "expired")
        );
        assertTrue(e2.getMessage().contains("invalid cash-out"));
    }

    @Test
    void simulateCashIn_happyPath() {
        MockTransport mt = new MockTransport(req -> MockTransport.json(200, Map.of(
                "ok", true,
                "environment", "sandbox",
                "order", Map.of("id", "ord_1", "status", "completed", "amount", 19.50)
        )));
        ClarianClient c = client(mt);
        Map<String, Object> res = c.sandbox.simulateCashIn("ord_1", "completed");
        @SuppressWarnings("unchecked")
        Map<String, Object> order = (Map<String, Object>) res.get("order");
        assertEquals("ord_1", order.get("id"));
        assertEquals("POST", mt.last().method());
        assertEquals("/pix/payins/ord_1/simulate", mt.last().path());
        assertEquals("Bearer cl_test_sk_x", mt.last().headers().get("Authorization"));
        Map<String, Object> body = Json.decodeObject(mt.last().bodyUtf8());
        assertEquals("completed", body.get("status"));
    }

    @Test
    void simulateCashIn_defaultStatus() {
        MockTransport mt = new MockTransport(req -> MockTransport.json(200, Map.of(
                "ok", true,
                "order", Map.of("id", "ord_2", "status", "completed")
        )));
        ClarianClient c = client(mt);
        c.sandbox.simulateCashIn("ord_2");
        Map<String, Object> body = Json.decodeObject(mt.last().bodyUtf8());
        assertEquals("completed", body.get("status"));
    }

    @Test
    void simulateCashOut_happyPath() {
        MockTransport mt = new MockTransport(req -> MockTransport.json(200, Map.of(
                "ok", true,
                "order", Map.of("id", "out_1", "status", "failed")
        )));
        ClarianClient c = client(mt);
        Map<String, Object> res = c.sandbox.simulateCashOut("out_1", "failed");
        @SuppressWarnings("unchecked")
        Map<String, Object> order = (Map<String, Object>) res.get("order");
        assertEquals("failed", order.get("status"));
        assertEquals("/pix/payouts/out_1/simulate", mt.last().path());
    }

    @Test
    void sendWebhookEvent_happyPath() {
        MockTransport mt = new MockTransport(req -> {
            assertEquals("/webhooks/wh1/test", req.path());
            return MockTransport.json(202, Map.of(
                    "ok", true,
                    "environment", "sandbox",
                    "event_type", "pix_payin.completed",
                    "enqueued", Map.of("enqueued", 1, "delivery_id", "del_99")
            ));
        });
        ClarianClient c = client(mt);
        Map<String, Object> res =
                c.sandbox.sendWebhookEvent("wh1", SandboxService.EVENT_PIX_PAYIN_COMPLETED);
        assertEquals(SandboxService.EVENT_PIX_PAYIN_COMPLETED, res.get("event_type"));
        assertEquals(1L, ((Number) res.get("enqueued")).longValue());
        assertEquals("del_99", res.get("delivery_id"));
        Map<String, Object> body = Json.decodeObject(mt.last().bodyUtf8());
        assertEquals("pix_payin.completed", body.get("event_type"));
    }

    @Test
    void resendWebhookDelivery_happyPath() {
        MockTransport mt = new MockTransport(req -> MockTransport.json(202, Map.of(
                "ok", true,
                "delivery_id", "del_new"
        )));
        ClarianClient c = client(mt);
        String newId = c.sandbox.resendWebhookDelivery("del_old");
        assertEquals("del_new", newId);
        assertEquals("/webhooks/deliveries/del_old/resend", mt.last().path());
        assertEquals("POST", mt.last().method());
    }

    @Test
    void magicPixKeyConstants() {
        assertEquals("fail@sandbox.clarian", SandboxService.SANDBOX_FAIL_PIX_KEY);
        assertEquals("pending@sandbox.clarian", SandboxService.SANDBOX_PENDING_PIX_KEY);
    }

    @Test
    void pathEscape() {
        MockTransport mt = new MockTransport(req -> MockTransport.json(200, Map.of(
                "ok", true,
                "order", Map.of("id", "x", "status", "completed")
        )));
        ClarianClient c = client(mt);
        c.sandbox.simulateCashIn("a/b", "completed");
        assertEquals("/pix/payins/a%2Fb/simulate", mt.last().path());
    }

    private static ClarianClient client(MockTransport mt) {
        return ClarianClient.builder()
                .apiKey("cl_test_sk_x")
                .workspaceId("ws")
                .baseUrl("https://mock")
                .transport(mt)
                .build();
    }
}
