import { describe, it, expect } from "vitest";
import { Clarian } from "../client.js";
import { ClarianError } from "../errors.js";

describe("Clarian client", () => {
  it("detects sandbox from test key", () => {
    const client = new Clarian({
      apiKey: "cl_test_sk_abc123",
      workspaceId: "00000000-0000-0000-0000-000000000000",
    });
    expect(client.environment).toBe("sandbox");
  });

  it("detects production from live key", () => {
    const client = new Clarian({
      apiKey: "cl_live_sk_abc123",
      workspaceId: "00000000-0000-0000-0000-000000000000",
    });
    expect(client.environment).toBe("production");
  });

  it("throws on missing apiKey", () => {
    expect(() => new Clarian({ apiKey: "", workspaceId: "id" })).toThrow("apiKey is required");
  });

  it("throws on missing workspaceId", () => {
    expect(() => new Clarian({ apiKey: "cl_test_sk_x", workspaceId: "" })).toThrow("workspaceId is required");
  });

  it("throws on invalid key format", () => {
    expect(() => new Clarian({ apiKey: "invalid_key", workspaceId: "id" })).toThrow("Invalid API key format");
  });

  it("exposes all resource namespaces", () => {
    const client = new Clarian({
      apiKey: "cl_test_sk_abc123",
      workspaceId: "00000000-0000-0000-0000-000000000000",
    });
    expect(client.rfq).toBeDefined();
    expect(client.cashIn).toBeDefined();
    expect(client.cashOut).toBeDefined();
    expect(client.balances).toBeDefined();
    expect(client.wallets).toBeDefined();
    expect(client.transactions).toBeDefined();
    expect(client.webhooks).toBeDefined();
  });
});

describe("ClarianError", () => {
  it("has structured fields", () => {
    const err = new ClarianError(403, {
      error: "workspace_mismatch",
      detail: "Key belongs to a different workspace",
    });
    expect(err.status).toBe(403);
    expect(err.code).toBe("workspace_mismatch");
    expect(err.message).toBe("Key belongs to a different workspace");
  });

  it("carries extra metadata in .meta", () => {
    const err = new ClarianError(402, {
      error: "insufficient_balance",
      detail: "Your balance is R$ 0.63...",
      available: 0.63,
      requested: 0.63,
      fee: 2.27,
      fee_bearer: "merchant",
      total_required: 2.90,
      max_withdrawable: 0,
    });
    expect(err.code).toBe("insufficient_balance");
    expect(err.meta.available).toBe(0.63);
    expect(err.meta.fee).toBe(2.27);
    expect(err.meta.max_withdrawable).toBe(0);
    expect(err.meta.total_required).toBe(2.90);
  });
});
