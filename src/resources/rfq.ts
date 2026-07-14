import type { HttpClientConfig } from "../http.js";
import { request } from "../http.js";
import type { Quote, QuoteRequest, ExecuteRequest, ExecuteResult, Environment } from "../types.js";

export class RFQ {
  constructor(private readonly config: HttpClientConfig) {}

  async quote(params: QuoteRequest): Promise<Quote> {
    const res = await request<{ ok: true; environment: Environment } & Quote>(
      this.config, "POST", "rfq/quote", params,
    );
    return res;
  }

  async execute(params: ExecuteRequest, idempotencyKey?: string): Promise<ExecuteResult> {
    const headers: Record<string, string> = {};
    if (idempotencyKey) headers["Idempotency-Key"] = idempotencyKey;
    const res = await request<{ ok: true; environment: Environment } & ExecuteResult>(
      this.config, "POST", "rfq/execute", params, headers,
    );
    return res;
  }
}
