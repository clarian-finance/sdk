package finance.clarian.sdk;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Minimal JSON encode/decode for the payload shapes used by this SDK.
 * Supports objects, arrays, strings, numbers, booleans, and null.
 */
final class Json {

    private Json() {}

    static String encode(Object value) {
        StringBuilder sb = new StringBuilder();
        write(sb, value);
        return sb.toString();
    }

    @SuppressWarnings("unchecked")
    static Map<String, Object> decodeObject(String json) {
        Object v = decode(json);
        if (v instanceof Map) {
            return (Map<String, Object>) v;
        }
        throw new IllegalArgumentException("expected JSON object");
    }

    static Object decode(String json) {
        if (json == null) {
            throw new IllegalArgumentException("json is null");
        }
        Parser p = new Parser(json);
        Object v = p.parseValue();
        p.skipWs();
        if (!p.eof()) {
            throw new IllegalArgumentException("trailing data at index " + p.i);
        }
        return v;
    }

    private static void write(StringBuilder sb, Object value) {
        if (value == null) {
            sb.append("null");
            return;
        }
        if (value instanceof String s) {
            writeString(sb, s);
            return;
        }
        if (value instanceof Boolean b) {
            sb.append(b ? "true" : "false");
            return;
        }
        if (value instanceof Number n) {
            writeNumber(sb, n);
            return;
        }
        if (value instanceof Map<?, ?> map) {
            sb.append('{');
            boolean first = true;
            for (Map.Entry<?, ?> e : map.entrySet()) {
                if (e.getKey() == null) {
                    continue;
                }
                if (!first) {
                    sb.append(',');
                }
                first = false;
                writeString(sb, String.valueOf(e.getKey()));
                sb.append(':');
                write(sb, e.getValue());
            }
            sb.append('}');
            return;
        }
        if (value instanceof Iterable<?> it) {
            sb.append('[');
            boolean first = true;
            for (Object item : it) {
                if (!first) {
                    sb.append(',');
                }
                first = false;
                write(sb, item);
            }
            sb.append(']');
            return;
        }
        if (value.getClass().isArray()) {
            int len = java.lang.reflect.Array.getLength(value);
            sb.append('[');
            for (int i = 0; i < len; i++) {
                if (i > 0) {
                    sb.append(',');
                }
                write(sb, java.lang.reflect.Array.get(value, i));
            }
            sb.append(']');
            return;
        }
        writeString(sb, String.valueOf(value));
    }

    private static void writeNumber(StringBuilder sb, Number n) {
        if (n instanceof Double d) {
            if (d.isNaN() || d.isInfinite()) {
                throw new IllegalArgumentException("cannot encode NaN/Infinity");
            }
            long asLong = d.longValue();
            if (asLong == d) {
                sb.append(asLong);
                return;
            }
            sb.append(d);
            return;
        }
        if (n instanceof Float f) {
            if (f.isNaN() || f.isInfinite()) {
                throw new IllegalArgumentException("cannot encode NaN/Infinity");
            }
            long asLong = f.longValue();
            if (asLong == f) {
                sb.append(asLong);
                return;
            }
            sb.append(f);
            return;
        }
        sb.append(n.toString());
    }

    private static void writeString(StringBuilder sb, String s) {
        sb.append('"');
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            switch (c) {
                case '"' -> sb.append("\\\"");
                case '\\' -> sb.append("\\\\");
                case '\b' -> sb.append("\\b");
                case '\f' -> sb.append("\\f");
                case '\n' -> sb.append("\\n");
                case '\r' -> sb.append("\\r");
                case '\t' -> sb.append("\\t");
                default -> {
                    if (c < 0x20) {
                        sb.append(String.format("\\u%04x", (int) c));
                    } else {
                        sb.append(c);
                    }
                }
            }
        }
        sb.append('"');
    }

    private static final class Parser {
        final String s;
        int i;

        Parser(String s) {
            this.s = s;
        }

        boolean eof() {
            return i >= s.length();
        }

        void skipWs() {
            while (i < s.length()) {
                char c = s.charAt(i);
                if (c == ' ' || c == '\n' || c == '\r' || c == '\t') {
                    i++;
                } else {
                    break;
                }
            }
        }

        char peek() {
            if (eof()) {
                throw new IllegalArgumentException("unexpected end of JSON");
            }
            return s.charAt(i);
        }

        char next() {
            char c = peek();
            i++;
            return c;
        }

        Object parseValue() {
            skipWs();
            char c = peek();
            return switch (c) {
                case '{' -> parseObject();
                case '[' -> parseArray();
                case '"' -> parseString();
                case 't' -> parseLiteral("true", Boolean.TRUE);
                case 'f' -> parseLiteral("false", Boolean.FALSE);
                case 'n' -> parseLiteral("null", null);
                case '-', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' -> parseNumber();
                default -> throw new IllegalArgumentException("unexpected char '" + c + "' at " + i);
            };
        }

        Object parseLiteral(String lit, Object value) {
            if (!s.startsWith(lit, i)) {
                throw new IllegalArgumentException("expected " + lit + " at " + i);
            }
            i += lit.length();
            return value;
        }

        Map<String, Object> parseObject() {
            next(); // {
            Map<String, Object> map = new LinkedHashMap<>();
            skipWs();
            if (peek() == '}') {
                next();
                return map;
            }
            while (true) {
                skipWs();
                if (peek() != '"') {
                    throw new IllegalArgumentException("expected string key at " + i);
                }
                String key = parseString();
                skipWs();
                if (next() != ':') {
                    throw new IllegalArgumentException("expected ':' at " + (i - 1));
                }
                Object val = parseValue();
                map.put(key, val);
                skipWs();
                char c = next();
                if (c == '}') {
                    return map;
                }
                if (c != ',') {
                    throw new IllegalArgumentException("expected ',' or '}' at " + (i - 1));
                }
            }
        }

        List<Object> parseArray() {
            next(); // [
            List<Object> list = new ArrayList<>();
            skipWs();
            if (peek() == ']') {
                next();
                return list;
            }
            while (true) {
                list.add(parseValue());
                skipWs();
                char c = next();
                if (c == ']') {
                    return list;
                }
                if (c != ',') {
                    throw new IllegalArgumentException("expected ',' or ']' at " + (i - 1));
                }
            }
        }

        String parseString() {
            next(); // "
            StringBuilder sb = new StringBuilder();
            while (true) {
                if (eof()) {
                    throw new IllegalArgumentException("unterminated string");
                }
                char c = next();
                if (c == '"') {
                    return sb.toString();
                }
                if (c == '\\') {
                    char e = next();
                    switch (e) {
                        case '"' -> sb.append('"');
                        case '\\' -> sb.append('\\');
                        case '/' -> sb.append('/');
                        case 'b' -> sb.append('\b');
                        case 'f' -> sb.append('\f');
                        case 'n' -> sb.append('\n');
                        case 'r' -> sb.append('\r');
                        case 't' -> sb.append('\t');
                        case 'u' -> {
                            if (i + 4 > s.length()) {
                                throw new IllegalArgumentException("bad unicode escape");
                            }
                            int code = Integer.parseInt(s.substring(i, i + 4), 16);
                            i += 4;
                            sb.append((char) code);
                        }
                        default -> throw new IllegalArgumentException("bad escape \\" + e);
                    }
                } else {
                    sb.append(c);
                }
            }
        }

        Number parseNumber() {
            int start = i;
            if (peek() == '-') {
                next();
            }
            if (peek() == '0') {
                next();
            } else {
                if (!Character.isDigit(peek())) {
                    throw new IllegalArgumentException("bad number at " + i);
                }
                while (!eof() && Character.isDigit(peek())) {
                    next();
                }
            }
            boolean isFloat = false;
            if (!eof() && peek() == '.') {
                isFloat = true;
                next();
                if (eof() || !Character.isDigit(peek())) {
                    throw new IllegalArgumentException("bad fraction at " + i);
                }
                while (!eof() && Character.isDigit(peek())) {
                    next();
                }
            }
            if (!eof() && (peek() == 'e' || peek() == 'E')) {
                isFloat = true;
                next();
                if (!eof() && (peek() == '+' || peek() == '-')) {
                    next();
                }
                if (eof() || !Character.isDigit(peek())) {
                    throw new IllegalArgumentException("bad exponent at " + i);
                }
                while (!eof() && Character.isDigit(peek())) {
                    next();
                }
            }
            String raw = s.substring(start, i);
            if (isFloat) {
                return Double.valueOf(raw);
            }
            try {
                return Long.valueOf(raw);
            } catch (NumberFormatException e) {
                return Double.valueOf(raw);
            }
        }
    }
}
