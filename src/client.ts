import type { ClarianConfig, Environment, PingResponse } from "./types.js";
import { createHttpConfig, resolveEnvironment, type HttpClientConfig } from "./http.js";
import { request } from "./http.js";
import { RFQ } from "./resources/rfq.js";
import { CashIn } from "./resources/cash-in.js";
import { CashOut } from "./resources/cash-out.js";
import { Balances } from "./resources/balances.js";
import { Wallets } from "./resources/wallets.js";
import { Transactions } from "./resources/transactions.js";
import { WebhooksAPI } from "./resources/webhooks-api.js";
import { Products } from "./resources/products.js";
import { Cards } from "./resources/cards.js";
import { Subscriptions } from "./resources/subscriptions.js";

export class Clarian {
  readonly rfq: RFQ;
  readonly cashIn: CashIn;
  readonly cashOut: CashOut;
  readonly balances: Balances;
  readonly wallets: Wallets;
  readonly transactions: Transactions;
  readonly webhooks: WebhooksAPI;
  readonly products: Products;
  readonly cards: Cards;
  readonly subscriptions: Subscriptions;
  readonly environment: Environment;
  private readonly config: HttpClientConfig;

  constructor(config: ClarianConfig) {
    if (!config.apiKey) throw new Error("apiKey is required");
    if (!config.workspaceId) throw new Error("workspaceId is required");

    this.config = createHttpConfig(config);
    this.environment = resolveEnvironment(config.apiKey);

    this.rfq = new RFQ(this.config);
    this.cashIn = new CashIn(this.config);
    this.cashOut = new CashOut(this.config);
    this.balances = new Balances(this.config);
    this.wallets = new Wallets(this.config);
    this.transactions = new Transactions(this.config);
    this.webhooks = new WebhooksAPI(this.config);
    this.products = new Products(this.config);
    this.cards = new Cards(this.config);
    this.subscriptions = new Subscriptions(this.config);
  }

  async ping(): Promise<PingResponse> {
    return request<PingResponse>(this.config, "GET", "ping");
  }
}
