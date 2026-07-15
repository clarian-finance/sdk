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

  async execute(_params: ExecuteRequest, _idempotencyKey: string): Promise<ExecuteResult> {
    throw new Error("rfq.execute() is coming soon");
  }
}
