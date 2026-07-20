# clarian-sdk (Java)

Official Java SDK for the [Clarian Finance API](https://api.clarian.finance). PIX cash-in/cash-out, balances, wallets, transactions, and webhooks.

Package: `finance.clarian.sdk` · coordinates: `finance.clarian:clarian-sdk:0.4.0` · **stdlib only** (no third-party runtime dependencies). Requires Java 17+.

## Install

From this monorepo:

```bash
cd java
gradle publishToMavenLocal
```

Then depend on the artifact:

**Gradle**

```kotlin
repositories {
    mavenLocal()
    mavenCentral()
}
dependencies {
    implementation("finance.clarian:clarian-sdk:0.4.0")
}
```

**Maven**

```xml
<dependency>
  <groupId>finance.clarian</groupId>
  <artifactId>clarian-sdk</artifactId>
  <version>0.4.0</version>
</dependency>
```

Alternatively, [JitPack](https://jitpack.io) can build from the GitHub monorepo subdirectory if you point it at this repository and the `java/` path (configure the build root / subdirectory per JitPack docs).

## Quickstart

```java
import finance.clarian.sdk.ClarianClient;
import finance.clarian.sdk.ClarianException;

import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

public class Example {
    public static void main(String[] args) {
        ClarianClient client = new ClarianClient(
                System.getenv("CLARIAN_API_KEY"),      // cl_live_sk_... or cl_test_sk_...
                System.getenv("CLARIAN_WORKSPACE_ID")  // workspace UUID
        );
        // cl_test_sk_ -> sandbox; cl_live_sk_ (and any other prefix) -> production/live.
        // Override with ClarianClient.builder().baseUrl(...).timeout(...).build();

        client.ping();

        Map<String, Object> chargeParams = new LinkedHashMap<>();
        chargeParams.put("amount", 250.00);
        chargeParams.put("payer", Map.of(
                "name", "Maria Silva",
                "document_number", "12345678900"
        ));
        chargeParams.put("description", "Order #1234");
        Map<String, Object> charge = client.cashIn.create(chargeParams, "order-1234");
        @SuppressWarnings("unchecked")
        Map<String, Object> order = (Map<String, Object>) charge.get("order");
        @SuppressWarnings("unchecked")
        Map<String, Object> pix = (Map<String, Object>) order.get("pix");
        System.out.println(pix.get("copy_paste"));

        Map<String, Object> payout = client.cashOut.create(
                Map.of("amount", 100.00),
                "withdrawal-2026-07-01-001"
        );
        @SuppressWarnings("unchecked")
        Map<String, Object> outOrder = (Map<String, Object>) payout.get("order");
        System.out.println(outOrder.get("id") + " " + outOrder.get("status"));

        // Optional: preview a PIX key owner before paying out.
        Map<String, Object> info = client.cashOut.dictCheck("maria@example.com", "EMAIL");

        List<Map<String, Object>> balances = client.balances.list();
    }
}
```

Auth headers (set automatically): `Authorization: Bearer <key>` and `X-Workspace-Id: <uuid>`.

## Environments

| Key prefix       | Base URL                                                    |
|------------------|-------------------------------------------------------------|
| `cl_test_sk_…`   | `https://api.clarian.finance/functions/v1/api-gateway/test` |
| `cl_live_sk_…`   | `https://api.clarian.finance/functions/v1/api-gateway/live` |
| any other prefix | **live** (same as `cl_live_sk_…`); not rejected             |

Only the `cl_test_sk_` prefix selects sandbox. Unknown prefixes default to live so a mis-typed key can hit production; pass `baseUrl` when you need an explicit host.

## Error handling

Non-2xx responses throw `ClarianException` with `status`, `code` (JSON `error` when present), `message` (via `getErrorMessage()`), and `meta`. The exception text truncates the raw body to 500 characters.

```java
try {
    client.cashOut.create(Map.of("amount", 100.00), "payout-inv-42");
} catch (ClarianException e) {
    System.err.println(e.getStatus() + " " + e.getCode() + " " + e.getErrorMessage() + " " + e.getMeta());
}
```

## Verifying webhooks

Deliveries are signed with **HMAC-SHA256** (hex) over `` `{X-Clarian-Timestamp}.{rawBody}` `` using the full `whsec_…` secret (prefix included; do not strip it). Reject timestamps older than **5 minutes** (replay protection).

```java
import finance.clarian.sdk.Webhooks;

// In your HTTP handler, use the exact raw body bytes (do not re-serialize JSON).
byte[] rawBody = /* request body bytes */;
Map<String, String> headers = /* request headers */;
String secret = System.getenv("CLARIAN_WEBHOOK_SECRET"); // whsec_...

Webhooks.Headers h = Webhooks.extractHeaders(headers);
if (!Webhooks.verifySignature(rawBody, h.timestamp(), h.signature(), secret, Webhooks.DEFAULT_TOLERANCE_SECONDS)) {
    // reject: 401
    return;
}
// process JSON body...
```

For local handler tests, sign a payload the same way the server does:

```java
String sig = Webhooks.signPayload(secret, timestamp, rawBody);
```

Create a subscription (secret shown **once**):

```java
Map<String, Object> sub = client.webhooks.create(Map.of(
        "url", "https://example.com/webhooks/clarian",
        "events", List.of("pix_payin.completed", "pix_payout.completed")
));
// store sub.get("secret") immediately; it is never returned again
System.out.println(sub.get("id") + " " + sub.get("secret"));
```

## Sandbox testing

Sandbox helpers live on `client.sandbox`. They require a `cl_test_sk_…` key and throw `IllegalStateException` for live keys **before** any HTTP call. Outside the test environment the gateway returns `404 {error:"sandbox_only"}`.

```java
// Advance a pending cash-in (empty/null status -> completed).
Map<String, Object> order = client.sandbox.simulateCashIn(orderId, "completed");
// status: "completed" | "expired" | "failed"

// Advance a cash-out: "completed" | "failed"
client.sandbox.simulateCashOut(payoutOrderId, "failed");

// Enqueue a sample webhook for a subscription.
Map<String, Object> result = client.sandbox.sendWebhookEvent(
        subscriptionId,
        SandboxService.EVENT_PIX_PAYIN_COMPLETED
);
String newId = client.sandbox.resendWebhookDelivery((String) result.get("delivery_id"));
```

### Magic PIX keys

| Constant / key              | Sandbox payout behavior                                      |
|-----------------------------|--------------------------------------------------------------|
| `SANDBOX_FAIL_PIX_KEY`      | `fail@sandbox.clarian` (fails + refund)                      |
| `SANDBOX_PENDING_PIX_KEY`   | `pending@sandbox.clarian` (stays pending until simulated)    |

```java
client.cashOut.create(
        Map.of("amount", 10.00, "pix_key", SandboxService.SANDBOX_FAIL_PIX_KEY),
        "payout-fail-1"
);
```

Sample event type constants: `EVENT_PIX_PAYIN_CREATED`, `EVENT_PIX_PAYIN_COMPLETED`, `EVENT_PIX_PAYIN_EXPIRED`, `EVENT_PIX_PAYOUT_CREATED`, `EVENT_PIX_PAYOUT_COMPLETED`, `EVENT_PIX_PAYOUT_FAILED`, `EVENT_CHECKOUT_PAID`.

## API surface

| Method | Path | Client |
|--------|------|--------|
| `GET` | `/ping` | `client.ping()` |
| `POST` | `/cash-in/pix` | `client.cashIn.create(params, idempotencyKey)` |
| `GET` | `/cash-in/{id}` | `client.cashIn.retrieve(id)` |
| `POST` | `/pix/payouts/dict` | `client.cashOut.dictCheck(pixKey, keyType)` |
| `POST` | `/cash-out/pix` | `client.cashOut.create(params, idempotencyKey)` |
| `GET` | `/cash-out/{id}` | `client.cashOut.retrieve(id)` |
| `GET` | `/account/balances` | `client.balances.list()` |
| `GET` | `/transactions` | `client.transactions.list(type, status, limit)` |
| `GET` | `/transactions/{id}` | `client.transactions.retrieve(id)` |
| `GET` | `/wallets` | `client.wallets.list(network)` |
| `GET` | `/wallets/{id}/balance` | `client.wallets.retrieveBalance(id)` |
| | webhooks CRUD | `client.webhooks.create/list/update/delete` |
| | verify delivery | `Webhooks.verifySignature` / `extractHeaders` / `signPayload` |
| `POST` | `/pix/payins/{id}/simulate` | `client.sandbox.simulateCashIn(id, status)` |
| `POST` | `/pix/payouts/{id}/simulate` | `client.sandbox.simulateCashOut(id, status)` |
| `POST` | `/webhooks/{id}/test` | `client.sandbox.sendWebhookEvent(id, eventType)` |
| `POST` | `/webhooks/deliveries/{id}/resend` | `client.sandbox.resendWebhookDelivery(id)` |

## Development

```bash
cd java
gradle test --console=plain
gradle publishToMavenLocal
```
