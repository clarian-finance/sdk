import type { HttpClientConfig } from "../http.js";
import { request } from "../http.js";
import type { Transaction, TransactionListParams, Environment } from "../types.js";

export class Transactions {
  constructor(private readonly config: HttpClientConfig) {}

  async list(params?: TransactionListParams): Promise<{ count: number; transactions: Transaction[] }> {
    const qs = new URLSearchParams();
    if (params?.limit) qs.set("limit", String(params.limit));
    if (params?.type) qs.set("type", params.type);
    if (params?.status) qs.set("status", params.status);
    const query = qs.toString();
    const path = query ? `transactions?${query}` : "transactions";

    const res = await request<{
      ok: true; environment: Environment; count: number; transactions: Transaction[];
    }>(this.config, "GET", path);
    return { count: res.count, transactions: res.transactions };
  }

  async retrieve(id: string): Promise<Transaction> {
    const res = await request<{ ok: true; environment: Environment; transaction: Transaction }>(
      this.config, "GET", `transactions/${id}`,
    );
    return res.transaction;
  }
}
