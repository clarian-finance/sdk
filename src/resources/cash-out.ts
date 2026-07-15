import type { HttpClientConfig } from "../http.js";
import { request } from "../http.js";
import type { CashOutCreateRequest, CashOutOrder, Environment } from "../types.js";

export class CashOut {
  constructor(private readonly config: HttpClientConfig) {}

  async create(params: CashOutCreateRequest, idempotencyKey: string): Promise<CashOutOrder> {
    const res = await request<{ ok: true; environment: Environment; order: CashOutOrder }>(
      this.config, "POST", "pix/payouts", params, { "Idempotency-Key": idempotencyKey },
    );
    return res.order;
  }

  async retrieve(id: string): Promise<CashOutOrder> {
    const res = await request<{ ok: true; environment: Environment; order: CashOutOrder }>(
      this.config, "GET", `cash-out/${id}`,
    );
    return res.order;
  }
}
