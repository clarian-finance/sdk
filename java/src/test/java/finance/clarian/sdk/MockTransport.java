package finance.clarian.sdk;

import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.function.Function;

/** Fake HTTP transport for unit tests (no network). */
final class MockTransport implements HttpTransport {

    record Recorded(
            String method,
            String url,
            String path,
            Map<String, String> headers,
            byte[] body
    ) {
        String bodyUtf8() {
            return body == null ? "" : new String(body, StandardCharsets.UTF_8);
        }
    }

    private final Function<Recorded, Response> handler;
    final List<Recorded> calls = new ArrayList<>();

    MockTransport(Function<Recorded, Response> handler) {
        this.handler = handler;
    }

    static Response json(int status, Object payload) {
        if (payload == null) {
            return new Response(status, new byte[0]);
        }
        if (payload instanceof byte[] bytes) {
            return new Response(status, bytes);
        }
        if (payload instanceof String s) {
            return new Response(status, s.getBytes(StandardCharsets.UTF_8));
        }
        return new Response(status, Json.encode(payload).getBytes(StandardCharsets.UTF_8));
    }

    Recorded last() {
        if (calls.isEmpty()) {
            throw new AssertionError("no requests recorded");
        }
        return calls.get(calls.size() - 1);
    }

    @Override
    public Response exchange(
            String method,
            String url,
            Map<String, String> headers,
            byte[] body,
            Duration timeout
    ) {
        String path = URI.create(url).getRawPath();
        String query = URI.create(url).getRawQuery();
        if (query != null && !query.isEmpty()) {
            path = path + "?" + query;
        }
        Recorded rec = new Recorded(
                method,
                url,
                path,
                new LinkedHashMap<>(headers),
                body == null ? new byte[0] : body
        );
        calls.add(rec);
        return handler.apply(rec);
    }
}
