import type { HttpClientConfig } from "../http.js";
import { request } from "../http.js";
import type { Wallet, WalletNetwork, OnChainBalance, Environment } from "../types.js";

export class Wallets {
  constructor(private readonly config: HttpClientConfig) {}

  async list(network?: WalletNetwork): Promise<Wallet[]> {
    const path = network ? `wallets?network=${network}` : "wallets";
    const res = await request<{ ok: true; environment: Environment; wallets: Wallet[] }>(
      this.config, "GET", path,
    );
    return res.wallets;
  }

  async retrieveBalance(walletId: string): Promise<{ wallet_id: string; network: string; address: string; balances: OnChainBalance[] }> {
    const res = await request<{
      ok: true; environment: Environment;
      wallet_id: string; network: string; address: string; balances: OnChainBalance[];
    }>(this.config, "GET", `wallets/${walletId}/balance`);
    return { wallet_id: res.wallet_id, network: res.network, address: res.address, balances: res.balances };
  }
}
