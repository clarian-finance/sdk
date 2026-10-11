package finance.clarian.sdk;

import java.io.IOException;
import java.time.Duration;
import java.util.Map;

/**
 * Pluggable HTTP transport. The default uses {@link java.net.http.HttpClient};
 * tests inject a fake so no network is needed.
 */
@FunctionalInterface
public interface HttpTransport {

    /** {@code headers} keys are lower-case. */
    record Response(int status, Map<String, String> headers, byte[] body) {
        public Response {
            headers = headers == null ? Map.of() : headers;
            body = body == null ? new byte[0] : body;
        }
    }

    Response exchange(String method, String url, Map<String, String> headers, byte[] body, Duration timeout)
            throws IOException;
}
