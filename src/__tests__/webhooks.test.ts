import { describe, it, expect } from "vitest";
import { constructWebhookEvent, signWebhookPayload, verifyWebhookSignature } from "../webhooks.js";

const SECRET = "whsec_test_secret_1234567890";
const body = '{"id":"evt_1","type":"payment.completed","created_at":"2026-10-10T00:00:00Z","data":{"payment_id":"p1"}}';

// Literal computed once with: printf '%s' "2026-10-10T00:00:00.000Z.{}" | openssl dgst -sha256 -hmac "k"
const KNOWN_SIG = "28f9264ffafd605af427e67efcdb3b4ae3e39a75b1f83f1e8cb6584f2b0cfc01";

describe("verifyWebhookSignature", () => {
  it("accepts a fresh valid signature", async () => {
    const ts = new Date().toISOString();
    expect(await verifyWebhookSignature(body, await signWebhookPayload(body, ts, SECRET), ts, SECRET)).toBe(true);
  });

  it("rejects a tampered body", async () => {
    const ts = new Date().toISOString();
    const sig = await signWebhookPayload(body, ts, SECRET);
    expect(await verifyWebhookSignature(body.replace("p1", "p2"), sig, ts, SECRET)).toBe(false);
  });

  it("rejects a wrong secret", async () => {
    const ts = new Date().toISOString();
    expect(await verifyWebhookSignature(body, await signWebhookPayload(body, ts, "other"), ts, SECRET)).toBe(false);
  });

  it("rejects a timestamp older than the tolerance", async () => {
    const ts = new Date(Date.now() - 600_000).toISOString();
    expect(await verifyWebhookSignature(body, await signWebhookPayload(body, ts, SECRET), ts, SECRET)).toBe(false);
  });

  it("rejects an unparseable timestamp even with a matching signature", async () => {
    const ts = "1791685933";
    expect(await verifyWebhookSignature(body, await signWebhookPayload(body, ts, SECRET), ts, SECRET)).toBe(false);
  });

  it("matches an HMAC computed outside the SDK", async () => {
    const sig = await signWebhookPayload("{}", "2026-10-10T00:00:00.000Z", "k");
    expect(sig).toBe(KNOWN_SIG);
  });
});

describe("constructWebhookEvent", () => {
  it("verifies from a Fetch Headers object and returns the body", async () => {
    const ts = new Date().toISOString();
    const headers = new Headers({
      "x-clarian-signature": await signWebhookPayload(body, ts, SECRET),
      "x-clarian-timestamp": ts,
      "x-clarian-event": "payment.completed",
    });
    const event = await constructWebhookEvent(body, headers, SECRET);
    expect(event.type).toBe("payment.completed");
    expect(event.id).toBe("evt_1");
  });

  it("verifies from a Node-style headers object with mixed case", async () => {
    const ts = new Date().toISOString();
    const event = await constructWebhookEvent(
      body,
      { "X-Clarian-Signature": await signWebhookPayload(body, ts, SECRET), "x-clarian-timestamp": [ts] },
      SECRET,
    );
    expect(event.data).toEqual({ payment_id: "p1" });
  });

  it("throws when the signature header is missing", async () => {
    await expect(constructWebhookEvent(body, {}, SECRET)).rejects.toThrow("Invalid webhook signature");
  });
});
