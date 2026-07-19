import { describe, it, expect, vi, afterEach } from "vitest";
import { Clarian } from "../client.js";

describe("Sandbox helpers", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("throws with a live key and does not call fetch", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const client = new Clarian({
      apiKey: "cl_live_sk_abc123",
      workspaceId: "00000000-0000-0000-0000-000000000000",
    });

    await expect(client.sandbox.simulateCashIn("order-id")).rejects.toThrow(
      "Sandbox helpers require a cl_test_sk_ key",
    );
    await expect(client.sandbox.simulateCashOut("order-id")).rejects.toThrow(
      "Sandbox helpers require a cl_test_sk_ key",
    );
    await expect(
      client.sandbox.sendWebhookEvent("sub-id", "pix_payin.completed"),
    ).rejects.toThrow("Sandbox helpers require a cl_test_sk_ key");
    await expect(client.sandbox.resendWebhookDelivery("del-id")).rejects.toThrow(
      "Sandbox helpers require a cl_test_sk_ key",
    );

    expect(fetchMock).not.toHaveBeenCalled();
  });
});
