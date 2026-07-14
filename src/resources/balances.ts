import type { HttpClientConfig } from "../http.js";
import { request } from "../http.js";
import type { Balance, Environment } from "../types.js";

export class Balances {
  constructor(private readonly config: HttpClientConfig) {}

  async list(): Promise<Balance[]> {
    const res = await request<{ ok: true; environment: Environment; balances: Balance[] }>(
      this.config, "GET", "account/balances",
    );
    return res.balances;
  }
}
