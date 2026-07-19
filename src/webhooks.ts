import type { WebhookPayload, WebhookEvent } from "./types.js";

const encoder = new TextEncoder();

async function hmacSHA256(message: string, secret: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    "raw",
    encoder.encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const sig = await crypto.subtle.sign("HMAC", key, encoder.encode(message));
  return Array.from(new Uint8Array(sig))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

const DEFAULT_TOLERANCE_MS = 300_000; // 5 minutes

/** Sign a webhook body for local handler tests (inverse of verifyWebhookSignature). */
export async function signWebhookPayload(
  body: string,
  timestamp: string,
  secret: string,
): Promise<string> {
  return hmacSHA256(`${timestamp}.${body}`, secret);
}

export async function verifyWebhookSignature(
  body: string,
  signature: string,
  timestamp: string,
  secret: string,
  toleranceMs = DEFAULT_TOLERANCE_MS,
): Promise<boolean> {
  const age = Math.abs(Date.now() - new Date(timestamp).getTime());
  if (age > toleranceMs) return false;

  const expected = await signWebhookPayload(body, timestamp, secret);
  if (expected.length !== signature.length) return false;

  let mismatch = 0;
  for (let i = 0; i < expected.length; i++) {
    mismatch |= expected.charCodeAt(i) ^ signature.charCodeAt(i);
  }
  return mismatch === 0;
}

export async function constructWebhookEvent<T = unknown>(
  body: string,
  headers: { signature: string; timestamp: string; event: string; deliveryId: string; idempotencyKey: string; attempt: string },
  secret: string,
  toleranceMs = DEFAULT_TOLERANCE_MS,
): Promise<WebhookPayload<T>> {
  const valid = await verifyWebhookSignature(body, headers.signature, headers.timestamp, secret, toleranceMs);
  if (!valid) throw new Error("Invalid webhook signature or timestamp outside tolerance");

  const data = JSON.parse(body) as T;
  return {
    id: headers.deliveryId,
    event: headers.event as WebhookEvent,
    timestamp: headers.timestamp,
    idempotency_key: headers.idempotencyKey,
    attempt: Number(headers.attempt) || 1,
    data,
  };
}

export function extractWebhookHeaders(headers: Record<string, string | string[] | undefined>) {
  const get = (key: string): string => {
    const v = headers[key] ?? headers[key.toLowerCase()];
    return Array.isArray(v) ? v[0] : (v ?? "");
  };
  return {
    signature: get("X-Clarian-Signature"),
    timestamp: get("X-Clarian-Timestamp"),
    event: get("X-Clarian-Event"),
    deliveryId: get("X-Clarian-Delivery-Id"),
    idempotencyKey: get("X-Clarian-Idempotency-Key"),
    attempt: get("X-Clarian-Attempt"),
  };
}
