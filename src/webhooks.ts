import type { WebhookEvent } from "./types.js";

const encoder = new TextEncoder();
const DEFAULT_TOLERANCE_MS = 300_000;

async function hmacSHA256(message: string, secret: string): Promise<string> {
  const key = await crypto.subtle.importKey("raw", encoder.encode(secret), { name: "HMAC", hash: "SHA-256" }, false, [
    "sign",
  ]);
  const sig = await crypto.subtle.sign("HMAC", key, encoder.encode(message));
  return Array.from(new Uint8Array(sig))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

/** Signs `${timestamp}.${body}` exactly as Clarian does. For tests of your own handler. */
export function signWebhookPayload(body: string, timestamp: string, secret: string): Promise<string> {
  return hmacSHA256(`${timestamp}.${body}`, secret);
}

export async function verifyWebhookSignature(
  body: string,
  signature: string,
  timestamp: string,
  secret: string,
  toleranceMs = DEFAULT_TOLERANCE_MS,
): Promise<boolean> {
  // An empty secret makes the HMAC forgeable by anyone: refuse it outright.
  if (!secret) throw new Error("webhook secret is required");
  const sentAt = new Date(timestamp).getTime();
  // An unparseable timestamp fails closed: NaN would slip past the age check.
  if (!Number.isFinite(sentAt) || Math.abs(Date.now() - sentAt) > toleranceMs) return false;
  const expected = await signWebhookPayload(body, timestamp, secret);
  if (expected.length !== signature.length) return false;
  let mismatch = 0;
  for (let i = 0; i < expected.length; i++) mismatch |= expected.charCodeAt(i) ^ signature.charCodeAt(i);
  return mismatch === 0;
}

type HeaderBag = Headers | Readonly<Record<string, string | readonly string[] | undefined>>;

export interface WebhookHeaders {
  readonly signature: string;
  readonly timestamp: string;
  readonly event: string;
}

/** Reads the `x-clarian-*` headers from a Fetch `Headers` or a Node/Express headers object. */
export function extractWebhookHeaders(headers: HeaderBag): WebhookHeaders {
  const get = (name: string): string => {
    if (typeof Headers !== "undefined" && headers instanceof Headers) return headers.get(name) ?? "";
    const bag = headers as Readonly<Record<string, string | readonly string[] | undefined>>;
    const hit = Object.entries(bag).find(([k]) => k.toLowerCase() === name)?.[1];
    return (Array.isArray(hit) ? hit[0] : (hit as string | undefined)) ?? "";
  };
  return {
    signature: get("x-clarian-signature"),
    timestamp: get("x-clarian-timestamp"),
    event: get("x-clarian-event"),
  };
}

/**
 * Verifies a delivery and returns its parsed body. `rawBody` must be the exact bytes
 * received, never a re-serialized object. Throws when the signature or timestamp is bad.
 * Dedupe on `event.id`: a delivery can arrive more than once.
 */
export async function constructWebhookEvent<T = WebhookEvent["data"]>(
  rawBody: string,
  headers: HeaderBag,
  secret: string,
  toleranceMs = DEFAULT_TOLERANCE_MS,
): Promise<WebhookEvent<T>> {
  const h = extractWebhookHeaders(headers);
  if (!(await verifyWebhookSignature(rawBody, h.signature, h.timestamp, secret, toleranceMs))) {
    throw new Error("Invalid webhook signature or timestamp outside tolerance");
  }
  return JSON.parse(rawBody) as WebhookEvent<T>;
}
