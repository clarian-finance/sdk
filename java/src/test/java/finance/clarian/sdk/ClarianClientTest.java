package finance.clarian.sdk;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

class ClarianClientTest {

    @Test
    void envDetection_testPrefix() {
        ClarianClient c = new ClarianClient("cl_test_sk_abc", "ws");
        assertEquals(ClarianClient.BASE_URL_TEST, c.baseUrl());
    }

    @Test
    void envDetection_livePrefix() {
        ClarianClient c = new ClarianClient("cl_live_sk_abc", "ws");
        assertEquals(ClarianClient.BASE_URL_LIVE, c.baseUrl());
    }

    @Test
    void envDetection_unknownDefaultsLive() {
        ClarianClient c = new ClarianClient("other_key", "ws");
        assertEquals(ClarianClient.BASE_URL_LIVE, c.baseUrl());
    }

    @Test
    void baseUrlOverride_stripsTrailingSlash() {
        ClarianClient c = ClarianClient.builder()
                .apiKey("cl_test_sk_x")
                .workspaceId("ws")
                .baseUrl("http://localhost:9999/v1/")
                .build();
        assertEquals("http://localhost:9999/v1", c.baseUrl());
    }

    @Test
    void authHeadersOnEveryCall() {
        MockTransport mt = new MockTransport(req -> MockTransport.json(200, Map.of(
                "ok", true,
                "environment", "sandbox",
                "master_account_id", "m1"
        )));
        ClarianClient c = client("cl_test_sk_key", "ws-uuid", mt);
        Map<String, Object> res = c.ping();
        assertEquals(true, res.get("ok"));
        assertEquals("GET", mt.last().method());
        assertEquals("/ping", mt.last().path());
        assertEquals("Bearer cl_test_sk_key", mt.last().headers().get("Authorization"));
        assertEquals("ws-uuid", mt.last().headers().get("X-Workspace-Id"));
    }

    @Test
    void cashInCreate_sendsIdempotencyAndWorkspace() {
        MockTransport mt = new MockTransport(req -> MockTransport.json(201, Map.of(
                "ok", true,
                "environment", "sandbox",
                "order", Map.of(
                        "id", "ord_1",
                        "status", "pending",
                        "amount", 19.50,
                        "currency", "BRL",
                        "pix", Map.of("copy_paste", "brcode")
                )
        )));
        ClarianClient c = client("cl_test_sk_x", "ws-uuid", mt);
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("amount", 19.50);
        body.put("payer", Map.of("name", "Ana", "document_number", "52998224725"));
        Map<String, Object> res = c.cashIn.create(body, "pay-ext-1");
        @SuppressWarnings("unchecked")
        Map<String, Object> order = (Map<String, Object>) res.get("order");
        assertEquals("ord_1", order.get("id"));
        assertEquals("POST", mt.last().method());
        assertEquals("/cash-in/pix", mt.last().path());
        assertEquals("pay-ext-1", mt.last().headers().get("Idempotency-Key"));
        assertEquals("ws-uuid", mt.last().headers().get("X-Workspace-Id"));
        Map<String, Object> sent = Json.decodeObject(mt.last().bodyUtf8());
        assertEquals(19.50, ((Number) sent.get("amount")).doubleValue(), 0.001);
    }

    @Test
    void cashOutCreate_sendsIdempotency() {
        MockTransport mt = new MockTransport(req -> MockTransport.json(201, Map.of(
                "ok", true,
                "order", Map.of("id", "out_9", "status", "processing")
        )));
        ClarianClient c = client("cl_test_sk_x", "ws", mt);
        c.cashOut.create(Map.of("amount", 10.00, "pix_key", "abc", "pix_key_type", "EVP"), "repasse-1");
        assertEquals("repasse-1", mt.last().headers().get("Idempotency-Key"));
        assertEquals("/cash-out/pix", mt.last().path());
    }

    @Test
    void getDoesNotSendIdempotencyKey() {
        MockTransport mt = new MockTransport(req -> MockTransport.json(200, Map.of(
                "ok", true,
                "order", Map.of("id", "x", "status", "pending")
        )));
        ClarianClient c = client("cl_test_sk_x", "ws", mt);
        c.cashIn.retrieve("x");
        assertFalse(mt.last().headers().containsKey("Idempotency-Key"));
    }

    @Test
    void cashInRetrieve_happyPath() {
        MockTransport mt = new MockTransport(req -> MockTransport.json(200, Map.of(
                "ok", true,
                "order", Map.of("id", "ord_paid", "status", "completed")
        )));
        ClarianClient c = client("cl_live_sk_x", "ws", mt);
        Map<String, Object> res = c.cashIn.retrieve("ord_paid");
        @SuppressWarnings("unchecked")
        Map<String, Object> order = (Map<String, Object>) res.get("order");
        assertEquals("completed", order.get("status"));
        assertEquals("/cash-in/ord_paid", mt.last().path());
    }

    @Test
    void cashOutRetrieve_happyPath() {
        MockTransport mt = new MockTransport(req -> MockTransport.json(200, Map.of(
                "ok", true,
                "order", Map.of("id", "out_1", "status", "completed")
        )));
        ClarianClient c = client("cl_test_sk_x", "ws", mt);
        Map<String, Object> res = c.cashOut.retrieve("out_1");
        @SuppressWarnings("unchecked")
        Map<String, Object> order = (Map<String, Object>) res.get("order");
        assertEquals("out_1", order.get("id"));
        assertEquals("/cash-out/out_1", mt.last().path());
    }

    @Test
    void cashOutDictCheck_happyPath() {
        MockTransport mt = new MockTransport(req -> MockTransport.json(200, Map.of(
                "ok", true,
                "dict", Map.of("name", "Maria", "keyType", "EMAIL")
        )));
        ClarianClient c = client("cl_test_sk_x", "ws", mt);
        Map<String, Object> res = c.cashOut.dictCheck("maria@example.com", "EMAIL");
        @SuppressWarnings("unchecked")
        Map<String, Object> dict = (Map<String, Object>) res.get("dict");
        assertEquals("Maria", dict.get("name"));
        assertEquals("POST", mt.last().method());
        assertEquals("/pix/payouts/dict", mt.last().path());
        Map<String, Object> sent = Json.decodeObject(mt.last().bodyUtf8());
        assertEquals("maria@example.com", sent.get("pix_key"));
        assertEquals("EMAIL", sent.get("key_type"));
    }

    @Test
    void balancesList_happyPath() {
        MockTransport mt = new MockTransport(req -> MockTransport.json(200, Map.of(
                "ok", true,
                "balances", List.of(Map.of(
                        "currency", "BRL",
                        "available", 100.50,
                        "pending", 0,
                        "locked", 0
                ))
        )));
        ClarianClient c = client("cl_test_sk_x", "ws", mt);
        List<Map<String, Object>> bals = c.balances.list();
        assertEquals(1, bals.size());
        assertEquals("BRL", bals.get(0).get("currency"));
        assertEquals("/account/balances", mt.last().path());
    }

    @Test
    void transactionsListAndRetrieve_happyPath() {
        MockTransport mt = new MockTransport(req -> {
            if (req.path().startsWith("/transactions?") || req.path().equals("/transactions")) {
                assertTrue(req.path().contains("type=pix_in"));
                assertTrue(req.path().contains("limit=20"));
                return MockTransport.json(200, Map.of(
                        "transactions", List.of(Map.of(
                                "id", "tx1",
                                "type", "pix_in",
                                "status", "completed",
                                "amount", 10,
                                "fee", 0,
                                "currency", "BRL"
                        ))
                ));
            }
            if (req.path().equals("/transactions/tx-99")) {
                return MockTransport.json(200, Map.of(
                        "transaction", Map.of(
                                "id", "tx-99",
                                "type", "pix_in",
                                "status", "completed",
                                "amount", 10,
                                "fee", 0.1,
                                "currency", "BRL"
                        )
                ));
            }
            throw new AssertionError("unexpected path " + req.path());
        });
        ClarianClient c = client("cl_test_sk_x", "ws", mt);
        List<Map<String, Object>> list = c.transactions.list("pix_in", null, 20);
        assertEquals("tx1", list.get(0).get("id"));
        Map<String, Object> tx = c.transactions.retrieve("tx-99");
        assertEquals("tx-99", tx.get("id"));
    }

    @Test
    void walletsListAndBalance_happyPath() {
        MockTransport mt = new MockTransport(req -> {
            if (req.path().equals("/wallets") || req.path().startsWith("/wallets?")) {
                return MockTransport.json(200, Map.of(
                        "ok", true,
                        "wallets", List.of(Map.of(
                                "id", "w1",
                                "network", "polygon",
                                "address", "0xabc"
                        ))
                ));
            }
            if (req.path().equals("/wallets/w1/balance")) {
                return MockTransport.json(200, Map.of(
                        "wallet_id", "w1",
                        "network", "polygon",
                        "address", "0xabc",
                        "balances", List.of(Map.of("currency", "USDT", "amount", "1.5"))
                ));
            }
            throw new AssertionError("unexpected path " + req.path());
        });
        ClarianClient c = client("cl_test_sk_x", "ws", mt);
        List<Map<String, Object>> wallets = c.wallets.list();
        assertEquals("w1", wallets.get(0).get("id"));
        Map<String, Object> bal = c.wallets.retrieveBalance("w1");
        assertEquals("w1", bal.get("wallet_id"));
        @SuppressWarnings("unchecked")
        List<Map<String, Object>> bals = (List<Map<String, Object>>) bal.get("balances");
        assertEquals("USDT", bals.get(0).get("currency"));
    }

    @Test
    void webhooksCrud_happyPath() {
        MockTransport mt = new MockTransport(req -> {
            if ("POST".equals(req.method()) && "/webhooks".equals(req.path())) {
                return MockTransport.json(201, Map.of(
                        "ok", true,
                        "subscription", Map.of(
                                "id", "wh1",
                                "url", "https://ex.com",
                                "events", List.of("pix_payin.completed"),
                                "is_active", true
                        ),
                        "secret", "whsec_abc"
                ));
            }
            if ("GET".equals(req.method()) && "/webhooks".equals(req.path())) {
                return MockTransport.json(200, Map.of(
                        "subscriptions", List.of(Map.of(
                                "id", "wh1",
                                "url", "https://ex.com",
                                "events", List.of("pix_payin.completed"),
                                "is_active", true
                        ))
                ));
            }
            if ("PATCH".equals(req.method()) && "/webhooks/wh1".equals(req.path())) {
                return MockTransport.json(200, Map.of(
                        "subscription", Map.of(
                                "id", "wh1",
                                "url", "https://ex.com/v2",
                                "events", List.of("pix_payin.completed"),
                                "is_active", false
                        )
                ));
            }
            if ("DELETE".equals(req.method()) && "/webhooks/wh1".equals(req.path())) {
                return MockTransport.json(200, Map.of("ok", true));
            }
            throw new AssertionError("unexpected " + req.method() + " " + req.path());
        });
        ClarianClient c = client("cl_test_sk_x", "ws", mt);
        Map<String, Object> created = c.webhooks.create(Map.of(
                "url", "https://ex.com",
                "events", List.of("pix_payin.completed")
        ));
        assertEquals("whsec_abc", created.get("secret"));
        assertEquals("wh1", created.get("id"));
        assertEquals(1, c.webhooks.list().size());
        Map<String, Object> upd = c.webhooks.update("wh1", Map.of(
                "url", "https://ex.com/v2",
                "events", List.of("pix_payin.completed"),
                "is_active", false
        ));
        assertEquals(false, upd.get("is_active"));
        c.webhooks.delete("wh1");
        assertEquals("DELETE", mt.last().method());
    }

    @Test
    void errorMapping_jsonCodeAndMeta() {
        MockTransport mt = new MockTransport(req -> MockTransport.json(402, Map.of(
                "error", "insufficient_balance",
                "detail", "saldo insuficiente",
                "available", 10,
                "requested", 100
        )));
        ClarianClient c = client("cl_test_sk_x", "ws", mt);
        ClarianException err = assertThrows(
                ClarianException.class,
                () -> c.cashOut.create(Map.of("amount", 100), "idem")
        );
        assertEquals(402, err.getStatus());
        assertEquals("insufficient_balance", err.getCode());
        assertEquals("saldo insuficiente", err.getErrorMessage());
        assertEquals(10L, ((Number) err.getMeta().get("available")).longValue());
        assertTrue(err.getMessage().contains("insufficient_balance"));
    }

    @Test
    void errorMapping_bodyTruncatedTo500() {
        String longBody = "x".repeat(600);
        MockTransport mt = new MockTransport(req -> MockTransport.json(429, longBody));
        ClarianClient c = client("cl_test_sk_x", "ws", mt);
        ClarianException err = assertThrows(ClarianException.class, c::ping);
        assertEquals(429, err.getStatus());
        assertEquals("", err.getCode());
        assertEquals(500, err.getErrorMessage().length());
        assertTrue(err.getMessage().length() <= 500 + "HTTP 429: ".length());
        assertFalse(err.getMessage().contains("x".repeat(600)));
        assertEquals(longBody, err.getBody());
        assertNotNull(err.getMeta());
    }

    private static ClarianClient client(String key, String ws, MockTransport mt) {
        return ClarianClient.builder()
                .apiKey(key)
                .workspaceId(ws)
                .baseUrl("https://mock")
                .transport(mt)
                .build();
    }
}
