package finance.clarian.sdk;

import java.nio.charset.StandardCharsets;

/** Path-segment encoding matching Go's {@code url.PathEscape}. */
final class Paths {

    private Paths() {}

    static String escape(String segment) {
        if (segment == null) {
            return "";
        }
        byte[] bytes = segment.getBytes(StandardCharsets.UTF_8);
        StringBuilder sb = new StringBuilder(bytes.length);
        for (byte b : bytes) {
            int c = b & 0xff;
            if ((c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9')
                    || c == '-'
                    || c == '.'
                    || c == '_'
                    || c == '~') {
                sb.append((char) c);
            } else {
                sb.append(String.format("%%%02X", c));
            }
        }
        return sb.toString();
    }
}
