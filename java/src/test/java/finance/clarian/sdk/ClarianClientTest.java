package finance.clarian.sdk;

import static org.junit.jupiter.api.Assertions.*;

import java.io.IOException;
import java.math.BigInteger;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.function.Function;
import org.junit.jupiter.api.Test;

class ClarianClientTest {

    record Call(String method, String url, Map<String, String> headers, String body) {}

    static final class Fake implements HttpTransport {
        final List<Call> calls = new ArrayList<>();
        final Function<Integer, Response> reply;
        Fake(Function<Integer, Response> reply) { this.reply = reply; }
        @Override public Response exchange(String m, String u, Map<String, String> h, byte[] b, java.time.Duration t) throws IOException {
            calls.add(new Call(m, u, Map.copyOf(h), b == null ? null : new String(b, StandardCharsets.UTF_8)));
            Response r = reply.apply(calls.size());
            if (r == null) throw new IOException("connection reset");
            return r;
        }
    }

    static HttpTransport.Response json(int status, String body) {
        return new HttpTransport.Response(status, Map.of("retry-after", "0"), body.getBytes(StandardCharsets.UTF_8));
    }

    static ClarianClient client(Fake f) {
        return ClarianClient.builder().apiKey("cl_test_sk_abc").workspaceId("ws-1").baseUrl("https://x/").transport(f).build();
    }

    @Test void buildsRequestWithHeadersAndUnwrapsData() {
        Fake f = new Fake(n -> json(201, "{\"data\":{\"id\":\"p1\",\"amount_minor\":\"10000\"}}"));
        Map<String, Object> p = client(f).forSubaccount("sub-9").pix.payins.create(Map.of("amount_minor", new BigInteger("10000")), "k-1");
        assertEquals("p1", p.get("id"));
        Call c = f.calls.get(0);
        assertEquals("POST", c.method());
        assertEquals("https://x/v1/test/pix/payins", c.url());
        assertEquals("Bearer cl_test_sk_abc", c.headers().get("Authorization"));
        assertEquals("ws-1", c.headers().get("X-Workspace-Id"));
        assertEquals("sub-9", c.headers().get("X-Subaccount-Id"));
        assertEquals("k-1", c.headers().get("Idempotency-Key"));
        assertEquals("application/json", c.headers().get("Content-Type"));
        assertEquals("{\"amount_minor\":\"10000\"}", c.body());
    }

    @Test void liveKeyAndQueryString() {
        Fake f = new Fake(n -> json(200, "{\"data\":[{\"id\":\"t\"}]}"));
        ClarianClient c = ClarianClient.builder().apiKey("cl_live_sk_x").workspaceId("w").transport(f).build();
        assertEquals(ClarianClient.Environment.LIVE, c.environment);
        assertEquals(1, c.transactions.list(new java.util.TreeMap<>(Map.of("limit", 5, "before", "a b"))).size());
        assertEquals("https://api.clarian.finance/v1/live/transactions?before=a%20b&limit=5", f.calls.get(0).url());
        assertFalse(f.calls.get(0).headers().containsKey("Content-Type"));
        assertThrows(IllegalArgumentException.class, () -> ClarianClient.builder().apiKey("sk_x").workspaceId("w").build());
    }

    @Test void mapsErrors() {
        Fake f = new Fake(n -> json(409, "{\"error\":\"quote_not_open\",\"message\":\"Quote is not open\",\"quote_id\":\"q\"}"));
        ClarianException e = assertThrows(ClarianException.class, () -> client(f).conversions.execute("q"));
        assertEquals(409, e.status());
        assertEquals("quote_not_open", e.code());
        assertEquals("Quote is not open", e.getMessage());
        assertEquals("q", e.meta().get("quote_id"));
        Fake bad = new Fake(n -> json(500, "<html>"));
        assertEquals("invalid_json_response", assertThrows(ClarianException.class, () -> client(bad).balances.list()).code());
    }

    @Test void retriesGetOnRetryableStatusAndNetworkError() {
        Fake f = new Fake(n -> n == 1 ? json(503, "{}") : n == 2 ? null : json(200, "{\"data\":[]}"));
        assertEquals(List.of(), client(f).balances.list());
        assertEquals(3, f.calls.size());
    }

    @Test void stopsAfterMaxRetries() {
        Fake f = new Fake(n -> json(429, "{\"error\":\"rate_limited\"}"));
        assertEquals(429, assertThrows(ClarianException.class, () -> client(f).balances.list()).status());
        assertEquals(3, f.calls.size());
    }

    @Test void postWithoutKeyNotRetried() {
        Fake f = new Fake(n -> json(503, "{\"error\":\"unavailable\"}"));
        assertThrows(ClarianException.class, () -> client(f).customers.create(Map.of("kind", "person")));
        assertEquals(1, f.calls.size());
    }

    @Test void executeNeverRetriedButKeyedCreateIs() {
        Fake f = new Fake(n -> json(502, "{}"));
        assertThrows(ClarianException.class, () -> client(f).conversions.execute("q"));
        assertEquals(1, f.calls.size());
        assertFalse(f.calls.get(0).headers().containsKey("Idempotency-Key"));
        Fake g = new Fake(n -> n < 3 ? json(502, "{}") : json(201, "{\"data\":{\"id\":\"o\"}}"));
        assertEquals("o", client(g).pix.payouts.create(Map.of("amount_minor", 1000), "k").get("id"));
        assertEquals(3, g.calls.size());
    }

    @Test void moneyCreatesRequireIdempotencyKey() {
        Fake f = new Fake(n -> json(200, "{}"));
        ClarianClient c = client(f);
        assertThrows(IllegalArgumentException.class, () -> c.pix.payins.create(Map.of("amount_minor", "1"), ""));
        assertThrows(IllegalArgumentException.class, () -> c.pix.payouts.create(Map.of("amount_minor", "1"), null));
        assertThrows(IllegalArgumentException.class, () -> c.crypto.send(Map.of("amount_minor", "1"), ""));
        assertThrows(IllegalArgumentException.class, () -> c.paymentLinks.charge("l", Map.of(), ""));
        assertThrows(IllegalArgumentException.class, () -> c.checkout.pay("s", Map.of(), ""));
        assertEquals(0, f.calls.size());
    }

    @Test void tradingCryptoWithdrawSendsIdempotencyKey() {
        Fake f = new Fake(n -> json(201, "{\"data\":{\"id\":\"p2\"}}"));
        client(f).trading.cryptoWithdraw(Map.of("amount_minor", "5000000", "currency", "USDC", "network", "polygon"), "k-2");
        Call c = f.calls.get(0);
        assertEquals("POST", c.method());
        assertEquals("https://x/v1/test/trading/crypto-withdrawals", c.url());
        assertEquals("k-2", c.headers().get("Idempotency-Key"));
    }

    @Test void tradingDepositRequiresIdempotencyKey() {
        Fake f = new Fake(n -> json(200, "{}"));
        assertThrows(IllegalArgumentException.class, () -> client(f).trading.deposit(Map.of("amount_minor", "100"), ""));
        assertEquals(0, f.calls.size());
    }

    @Test void rejectsFloatAmounts() {
        Fake f = new Fake(n -> json(200, "{}"));
        assertThrows(IllegalArgumentException.class, () -> client(f).pix.payouts.fee(Map.of("amount_minor", 10.5)));
        assertEquals(0, f.calls.size());
    }

    @Test void dotAndEmptyIdsRefused() {
        Fake f = new Fake(n -> json(200, "{}"));
        for (String id : new String[] {"", ".", ".."}) {
            assertThrows(IllegalArgumentException.class, () -> client(f).wallets.retrieve(id));
        }
        assertTrue(f.calls.isEmpty());
    }

    @Test void blankSubaccountRefused() {
        Fake f = new Fake(n -> json(200, "{}"));
        assertThrows(IllegalArgumentException.class, () -> client(f).forSubaccount(" "));
        assertThrows(IllegalArgumentException.class, () -> client(f).forSubaccount(null));
        assertThrows(IllegalArgumentException.class, () -> ClarianClient.builder().apiKey("cl_test_sk_x").workspaceId("ws").subaccountId("").build());
    }

    @Test void baseUrlMustBeHttps() {
        for (String u : new String[] {"http://api.example.com", "http://localhost.evil.com", "ftp://x"}) {
            assertThrows(IllegalArgumentException.class, () -> ClarianClient.builder().apiKey("cl_test_sk_x").workspaceId("ws").baseUrl(u).build(), u);
        }
        for (String u : new String[] {"https://api.example.com", "http://localhost:3000", "http://127.0.0.1", "http://host.docker.internal:8080"}) {
            assertDoesNotThrow(() -> ClarianClient.builder().apiKey("cl_test_sk_x").workspaceId("ws").baseUrl(u).build(), u);
        }
    }

    @Test void toStringMasksKey() {
        ClarianClient c = ClarianClient.builder().apiKey("cl_test_sk_secret").workspaceId("ws").build();
        assertFalse(c.toString().contains("cl_test_sk_secret"));
        assertFalse(ClarianClient.builder().apiKey("cl_test_sk_secret").toString().contains("cl_test_sk_secret"));
    }
}
