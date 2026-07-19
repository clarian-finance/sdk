import type { HttpClientConfig } from "../http.js";
import { request } from "../http.js";
import type {
  CardCharge,
  CardChargeCreateRequest,
  CardToken,
  Environment,
} from "../types.js";

export class Cards {
  constructor(private readonly config: HttpClientConfig) {}

  async createCaptureSession(params?: Record<string, unknown>): Promise<unknown> {
    const res = await request<{ ok: true; environment: Environment } & Record<string, unknown>>(
      this.config, "POST", "cards/capture-sessions", params || {},
    );
    return res;
  }

  async listTokens(): Promise<CardToken[]> {
    const res = await request<{ ok: true; environment: Environment; tokens: CardToken[] }>(
      this.config, "GET", "cards/tokens",
    );
    return res.tokens;
  }

  async revokeToken(id: string): Promise<void> {
    await request(this.config, "DELETE", `cards/tokens/${id}`);
  }

  async charge(params: CardChargeCreateRequest, idempotencyKey: string): Promise<CardCharge> {
    if (!idempotencyKey) throw new Error("idempotencyKey is required for card charges");
    const res = await request<{ ok: true; environment: Environment; charge: CardCharge }>(
      this.config, "POST", "cards/charges", params, { "Idempotency-Key": idempotencyKey },
    );
    return res.charge;
  }

  async retrieveCharge(id: string): Promise<CardCharge> {
    const res = await request<{ ok: true; environment: Environment; charge: CardCharge }>(
      this.config, "GET", `cards/charges/${id}`,
    );
    return res.charge;
  }

  async refund(id: string, idempotencyKey: string): Promise<CardCharge | unknown> {
    if (!idempotencyKey) throw new Error("idempotencyKey is required for refunds");
    const res = await request<{ ok: true; environment: Environment; charge?: CardCharge }>(
      this.config, "POST", `cards/charges/${id}/refund`, {}, { "Idempotency-Key": idempotencyKey },
    );
    return res.charge ?? res;
  }

  /** Sandbox only: mark a pending charge paid and settle ledger / subscription invoice. */
  async simulatePayment(id: string): Promise<CardCharge> {
    const res = await request<{ ok: true; environment: Environment; charge: CardCharge }>(
      this.config, "POST", `cards/charges/${id}/simulate`, {},
    );
    return res.charge;
  }
}
