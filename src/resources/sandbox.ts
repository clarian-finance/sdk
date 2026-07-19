import type { HttpClientConfig } from "../http.js";
import { request, resolveEnvironment } from "../http.js";
import type {
  CashInOrder,
  CashOutOrder,
  Environment,
  SampleEventType,
  SimulateCashInStatus,
  SimulateCashOutStatus,
} from "../types.js";

export class Sandbox {
  constructor(private readonly config: HttpClientConfig) {}

  private assertSandbox(): void {
    if (resolveEnvironment(this.config.apiKey) !== "sandbox") {
      throw new Error("Sandbox helpers require a cl_test_sk_ key");
    }
  }

  async simulateCashIn(id: string, status?: SimulateCashInStatus): Promise<CashInOrder> {
    this.assertSandbox();
    const res = await request<{ ok: true; environment: Environment; order: CashInOrder }>(
      this.config,
      "POST",
      `pix/payins/${encodeURIComponent(id)}/simulate`,
      status !== undefined ? { status } : {},
    );
    return res.order;
  }

  async simulateCashOut(id: string, status?: SimulateCashOutStatus): Promise<CashOutOrder> {
    this.assertSandbox();
    const res = await request<{ ok: true; environment: Environment; order: CashOutOrder }>(
      this.config,
      "POST",
      `pix/payouts/${encodeURIComponent(id)}/simulate`,
      status !== undefined ? { status } : {},
    );
    return res.order;
  }

  async sendWebhookEvent(
    subscriptionId: string,
    eventType: SampleEventType,
  ): Promise<{ enqueued: number; delivery_id: string | null }> {
    this.assertSandbox();
    const res = await request<{
      ok: true;
      environment: Environment;
      event_type: string;
      enqueued: { enqueued: number; delivery_id: string | null };
    }>(
      this.config,
      "POST",
      `webhooks/${encodeURIComponent(subscriptionId)}/test`,
      { event_type: eventType },
    );
    return res.enqueued;
  }

  async resendWebhookDelivery(deliveryId: string): Promise<{ deliveryId: string }> {
    this.assertSandbox();
    const res = await request<{ ok: true; delivery_id: string }>(
      this.config,
      "POST",
      `webhooks/deliveries/${encodeURIComponent(deliveryId)}/resend`,
    );
    return { deliveryId: res.delivery_id };
  }
}
