import { createHttpConfig, type HttpClientConfig } from "./http.js";
import * as R from "./resources.js";
import type { ClarianConfig, Environment } from "./types.js";

export class Clarian {
  readonly environment: Environment;
  readonly balances: R.Balances;
  readonly wallets: R.Wallets;
  readonly transactions: R.Transactions;
  readonly pix: R.Pix;
  readonly conversions: R.Conversions;
  readonly crypto: R.Crypto;
  readonly customers: R.Customers;
  readonly kyc: R.Kyc;
  readonly whitelist: R.Whitelist;
  readonly bankAccounts: R.BankAccounts;
  readonly webhooks: R.Webhooks;
  readonly paymentLinks: R.PaymentLinks;
  readonly checkout: R.Checkout;
  readonly subaccounts: R.SubAccounts;
  readonly apiKeys: R.ApiKeys;
  readonly settings: R.Settings;
  readonly notifications: R.Notifications;
  readonly team: R.Team;
  readonly workflows: R.Workflows;
  readonly approvals: R.Approvals;
  readonly treasury: R.Treasury;
  private readonly config: HttpClientConfig;
  private readonly options: ClarianConfig;

  constructor(options: ClarianConfig) {
    if (!options.apiKey) throw new Error("apiKey is required");
    if (!options.workspaceId) throw new Error("workspaceId is required");
    const c = createHttpConfig(options);
    this.options = options;
    this.config = c;
    this.environment = c.environment;
    this.balances = new R.Balances(c);
    this.wallets = new R.Wallets(c);
    this.transactions = new R.Transactions(c);
    this.pix = new R.Pix(c);
    this.conversions = new R.Conversions(c);
    this.crypto = new R.Crypto(c);
    this.customers = new R.Customers(c);
    this.kyc = new R.Kyc(c);
    this.whitelist = new R.Whitelist(c);
    this.bankAccounts = new R.BankAccounts(c);
    this.webhooks = new R.Webhooks(c);
    this.paymentLinks = new R.PaymentLinks(c);
    this.checkout = new R.Checkout(c);
    this.subaccounts = new R.SubAccounts(c);
    this.apiKeys = new R.ApiKeys(c);
    this.settings = new R.Settings(c);
    this.notifications = new R.Notifications(c);
    this.team = new R.Team(c);
    this.workflows = new R.Workflows(c);
    this.approvals = new R.Approvals(c);
    this.treasury = new R.Treasury(c);
  }

  /** Checks the key and workspace. */
  ping() {
    return R.ping(this.config);
  }

  /** A client that acts on one child workspace (sends `X-Subaccount-Id`). */
  forSubaccount(subaccountId: string): Clarian {
    return new Clarian({ ...this.options, subaccountId });
  }
}
