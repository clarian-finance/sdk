package finance.clarian.sdk;

import java.io.IOException;
import java.time.Duration;
import java.util.Map;

/**
 * Pluggable HTTP transport. Default implementation uses {@link java.net.http.HttpClient}.
 * Tests inject a fake transport so no external network is required.
 */
@FunctionalInterface
public interface HttpTransport {

    record Response(int status, byte[] body) {
        public Response {
            if (body == null) {
                body = new byte[0];
            }
        }
    }

    Response exchange(
            String method,
            String url,
            Map<String, String> headers,
            byte[] body,
            Duration timeout
    ) throws IOException;
}
