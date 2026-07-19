import { describe, it, expect } from "vitest";
import {
  verifyWebhookSignature,
  constructWebhookEvent,
  extractWebhookHeaders,
  signWebhookPayload,
} from "../webhooks.js";

const SECRET = "whsec_test_secret_1234567890";

async function sign(body: string, timestamp: string, secret: string): Promise<string> {
  const enc = new TextEncoder();
  const key = await crypto.subtle.importKey(
    "raw", enc.encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"],
  );
  const sig = await crypto.subtle.sign("HMAC", key, enc.encode(`${timestamp}.${body}`));
  return Array.from(new Uint8Array(sig)).map((b) => b.toString(16).padStart(2, "0")).join("");
}

describe("signWebhookPayload", () => {
  it("roundtrips with verifyWebhookSignature and rejects a tampered body", async () => {
    const body = '{"type":"pix_payin.completed","amount":100}';
    const timestamp = new Date().toISOString();
    const signature = await signWebhookPayload(body, timestamp, SECRET);

    expect(await verifyWebhookSignature(body, signature, timestamp, SECRET)).toBe(true);
    expect(
      await verifyWebhookSignature('{"type":"pix_payin.completed","amount":999}', signature, timestamp, SECRET),
    ).toBe(false);
  });
});

describe("verifyWebhookSignature", () => {
  it("accepts a valid signature", async () => {
    const body = '{"type":"pix_payin.completed"}';
    const timestamp = new Date().toISOString();
    const signature = await sign(body, timestamp, SECRET);

    const valid = await verifyWebhookSignature(body, signature, timestamp, SECRET);
    expect(valid).toBe(true);
  });

  it("rejects a tampered body", async () => {
    const body = '{"type":"pix_payin.completed"}';
    const timestamp = new Date().toISOString();
    const signature = await sign(body, timestamp, SECRET);

    const valid = await verifyWebhookSignature('{"tampered":true}', signature, timestamp, SECRET);
    expect(valid).toBe(false);
  });

  it("rejects a wrong secret", async () => {
    const body = '{"type":"pix_payin.completed"}';
    const timestamp = new Date().toISOString();
    const signature = await sign(body, timestamp, "wrong_secret");

    const valid = await verifyWebhookSignature(body, signature, timestamp, SECRET);
    expect(valid).toBe(false);
  });

  it("rejects an expired timestamp", async () => {
    const body = '{"type":"pix_payin.completed"}';
    const old = new Date(Date.now() - 600_000).toISOString();
    const signature = await sign(body, old, SECRET);

    const valid = await verifyWebhookSignature(body, signature, old, SECRET);
    expect(valid).toBe(false);
  });
});

describe("constructWebhookEvent", () => {
  it("returns parsed payload for valid webhook", async () => {
    const body = '{"amount":1000,"status":"completed"}';
    const timestamp = new Date().toISOString();
    const signature = await sign(body, timestamp, SECRET);

    const event = await constructWebhookEvent(body, {
      signature, timestamp,
      event: "pix_payin.completed",
      deliveryId: "del_123",
      idempotencyKey: "idem_456",
      attempt: "1",
    }, SECRET);

    expect(event.event).toBe("pix_payin.completed");
    expect(event.id).toBe("del_123");
    expect(event.data).toEqual({ amount: 1000, status: "completed" });
  });

  it("throws on invalid signature", async () => {
    const body = '{"amount":1000}';
    const timestamp = new Date().toISOString();

    await expect(constructWebhookEvent(body, {
      signature: "invalid", timestamp,
      event: "pix_payin.completed",
      deliveryId: "del_123",
      idempotencyKey: "idem_456",
      attempt: "1",
    }, SECRET)).rejects.toThrow("Invalid webhook signature");
  });
});

describe("extractWebhookHeaders", () => {
  it("extracts clarian headers", () => {
    const h = extractWebhookHeaders({
      "X-Clarian-Signature": "abc123",
      "X-Clarian-Timestamp": "2026-01-01T00:00:00Z",
      "X-Clarian-Event": "pix_payin.completed",
      "X-Clarian-Delivery-Id": "del_1",
      "X-Clarian-Idempotency-Key": "idem_1",
      "X-Clarian-Attempt": "2",
    });
    expect(h.signature).toBe("abc123");
    expect(h.event).toBe("pix_payin.completed");
    expect(h.attempt).toBe("2");
  });
});
