import type { HttpClientConfig } from "../http.js";
import { request } from "../http.js";
import type { CashInCreateRequest, CashInOrder, Environment } from "../types.js";

export class CashIn {
  constructor(private readonly config: HttpClientConfig) {}

  async create(params: CashInCreateRequest, idempotencyKey?: string): Promise<CashInOrder> {
    const headers: Record<string, string> = {};
    if (idempotencyKey) headers["Idempotency-Key"] = idempotencyKey;

    const body = {
      amount: params.amount,
      payer: {
        name: params.payer.name,
        document: params.payer.document,
      },
      description: params.description,
      expiration_seconds: params.expiration_seconds,
      external_id: params.external_id,
      customer_id: params.customer_id,
    };

    const res = await request<{ ok: true; environment: Environment; order: CashInOrder }>(
      this.config, "POST", "pix/payins", body, headers,
    );
    return res.order;
  }

  async retrieve(id: string): Promise<CashInOrder> {
    const res = await request<{ ok: true; environment: Environment; order: CashInOrder }>(
      this.config, "GET", `cash-in/${id}`,
    );
    return res.order;
  }
}
