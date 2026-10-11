import { describe, it, expect } from "vitest";
import { Clarian, ClarianError } from "../index.js";

const KEY = "cl_test_sk_unit";
const WS = "00000000-0000-0000-0000-000000000001";
const json = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json", ...headers } });

const recorder = (replies: Response[]) => {
  const calls: { url: string; init: RequestInit }[] = [];
  const fetch = (async (url: string, init: RequestInit) => {
    calls.push({ url, init });
    return replies.shift() ?? json(500, { error: "no_more_replies" });
  }) as unknown as typeof globalThis.fetch;
  return { calls, fetch };
};

describe("request", () => {
  it("targets /v1/test for a sandbox key, sends auth headers and unwraps data", async () => {
    const r = recorder([json(200, { data: [{ currency: "BRL", kind: "available", balance_minor: "100", scale: 2 }] })]);
    const c = new Clarian({ apiKey: KEY, workspaceId: WS, fetch: r.fetch, baseUrl: "https://api.example.com/" });
    const rows = await c.balances.list();
    expect(rows[0].balance_minor).toBe("100");
    expect(r.calls[0].url).toBe("https://api.example.com/v1/test/balances");
    const h = r.calls[0].init.headers as Record<string, string>;
    expect(h.Authorization).toBe(`Bearer ${KEY}`);
    expect(h["X-Workspace-Id"]).toBe(WS);
  });

  it("targets /v1/live for a live key", async () => {
    const r = recorder([json(200, { data: {} })]);
    await new Clarian({ apiKey: "cl_live_sk_x", workspaceId: WS, fetch: r.fetch }).ping();
    expect(r.calls[0].url).toBe("https://api.clarian.finance/v1/live/ping");
  });

  it("serializes bigint amounts as strings and sends the idempotency key", async () => {
    const r = recorder([json(200, { data: { id: "p1" } })]);
    const c = new Clarian({ apiKey: KEY, workspaceId: WS, fetch: r.fetch });
    await c.pix.payins.create({ amount_minor: 12345678901234567890n }, "k-1");
    expect(r.calls[0].init.body).toBe('{"amount_minor":"12345678901234567890"}');
    expect((r.calls[0].init.headers as Record<string, string>)["Idempotency-Key"]).toBe("k-1");
  });

  it("throws ClarianError with the API code", async () => {
    const r = recorder([json(403, { error: "settlement_key_required" })]);
    const c = new Clarian({ apiKey: KEY, workspaceId: WS, fetch: r.fetch });
    const err = await c.pix.payouts.create({ amount_minor: 1 }, "k").catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ClarianError);
    expect((err as ClarianError).status).toBe(403);
    expect((err as ClarianError).code).toBe("settlement_key_required");
  });

  it("retries a read on 429 honoring Retry-After", async () => {
    const r = recorder([json(429, { error: "rate_limited" }, { "retry-after": "0" }), json(200, { data: [] })]);
    const c = new Clarian({ apiKey: KEY, workspaceId: WS, fetch: r.fetch });
    expect(await c.transactions.list()).toEqual([]);
    expect(r.calls).toHaveLength(2);
  });

  it("never retries a create without an idempotency key", async () => {
    const r = recorder([json(503, { error: "unavailable" }), json(200, { data: {} })]);
    const c = new Clarian({ apiKey: KEY, workspaceId: WS, fetch: r.fetch });
    await expect(c.conversions.execute("q1")).rejects.toThrow("unavailable"); // single-use quote, never retried
    expect(r.calls).toHaveLength(1);
  });

  it("stops after maxRetries", async () => {
    const r = recorder([0, 1, 2, 3].map(() => json(503, { error: "unavailable" }, { "retry-after": "0" })));
    const c = new Clarian({ apiKey: KEY, workspaceId: WS, fetch: r.fetch, maxRetries: 2 });
    await expect(c.balances.list()).rejects.toThrow("unavailable");
    expect(r.calls).toHaveLength(3);
  });

  it("sends X-Subaccount-Id from forSubaccount", async () => {
    const r = recorder([json(200, { data: [] })]);
    await new Clarian({ apiKey: KEY, workspaceId: WS, fetch: r.fetch }).forSubaccount("sub-1").wallets.list();
    expect((r.calls[0].init.headers as Record<string, string>)["X-Subaccount-Id"]).toBe("sub-1");
  });

  it("refuses a money create with no idempotency key before any network call", async () => {
    const r = recorder([]);
    const c = new Clarian({ apiKey: KEY, workspaceId: WS, fetch: r.fetch });
    await expect(c.pix.payouts.create({ amount_minor: 1 }, "")).rejects.toThrow("idempotencyKey is required");
    expect(r.calls).toHaveLength(0);
  });

  it("rejects an unknown key prefix", () => {
    expect(() => new Clarian({ apiKey: "sk_x", workspaceId: WS })).toThrow("Invalid API key format");
  });
});

describe("retry eligibility", () => {
  it("does not retry a POST that carries no idempotency key", async () => {
    const r = recorder([json(503, { error: "unavailable" }, { "retry-after": "0" }), json(200, { data: {} })]);
    const c = new Clarian({ apiKey: KEY, workspaceId: WS, fetch: r.fetch });
    await expect(c.wallets.create({})).rejects.toThrow("unavailable");
    expect(r.calls).toHaveLength(1);
  });
});
