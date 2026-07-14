import type { ClarianConfig, Environment } from "./types.js";
import { createHttpConfig, resolveEnvironment } from "./http.js";
import { RFQ } from "./resources/rfq.js";
import { CashIn } from "./resources/cash-in.js";
import { CashOut } from "./resources/cash-out.js";
import { Balances } from "./resources/balances.js";
import { Wallets } from "./resources/wallets.js";
import { Transactions } from "./resources/transactions.js";
import { WebhooksAPI } from "./resources/webhooks-api.js";

export class Clarian {
  readonly rfq: RFQ;
  readonly cashIn: CashIn;
  readonly cashOut: CashOut;
  readonly balances: Balances;
  readonly wallets: Wallets;
  readonly transactions: Transactions;
  readonly webhooks: WebhooksAPI;
  readonly environment: Environment;

  constructor(config: ClarianConfig) {
    if (!config.apiKey) throw new Error("apiKey is required");
    if (!config.workspaceId) throw new Error("workspaceId is required");

    const httpConfig = createHttpConfig(config);
    this.environment = resolveEnvironment(config.apiKey);

    this.rfq = new RFQ(httpConfig);
    this.cashIn = new CashIn(httpConfig);
    this.cashOut = new CashOut(httpConfig);
    this.balances = new Balances(httpConfig);
    this.wallets = new Wallets(httpConfig);
    this.transactions = new Transactions(httpConfig);
    this.webhooks = new WebhooksAPI(httpConfig);
  }
}
