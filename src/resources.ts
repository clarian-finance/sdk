// One class per API area, in the order of the public reference.
import { request, seg, type HttpClientConfig } from "./http.js";
import type * as S from "./schema.js";
import type { CheckoutListParams, ListParams, PingResponse, Row, StatementParams } from "./types.js";

type Cfg = HttpClientConfig;
type Deleted = Readonly<Record<string, unknown>>;

const requireKey = (idempotencyKey: string) => {
  if (!idempotencyKey) throw new Error("idempotencyKey is required: reuse the same key to retry safely");
  return idempotencyKey;
};

export class Balances {
  constructor(private readonly c: Cfg) {}
  list() {
    return request<S.Balance[]>(this.c, "GET", "balances");
  }
}

export class Wallets {
  constructor(private readonly c: Cfg) {}
  list() {
    return request<S.Wallet[]>(this.c, "GET", "wallets");
  }
  create(body: S.CreateWalletRequest = {}) {
    return request<S.Wallet>(this.c, "POST", "wallets", { body });
  }
  retrieve(id: string) {
    return request<S.Wallet>(this.c, "GET", `wallets/${seg(id)}`);
  }
  balance(id: string) {
    return request<Row>(this.c, "GET", `wallets/${seg(id)}/balance`);
  }
  transactions(id: string, params: { limit?: number } = {}) {
    return request<S.Payment[]>(this.c, "GET", `wallets/${seg(id)}/transactions`, { query: params });
  }
}

export class Transactions {
  constructor(private readonly c: Cfg) {}
  list(params: ListParams = {}) {
    return request<S.Payment[]>(this.c, "GET", "transactions", { query: params });
  }
  retrieve(id: string) {
    return request<S.Payment>(this.c, "GET", `transactions/${seg(id)}`);
  }
  statement(params: StatementParams = {}) {
    return request<S.StatementLine[]>(this.c, "GET", "statement", { query: params });
  }
}

/** PIX cash-in: a charge the payer pays with a QR code / copia-e-cola. */
export class PixPayins {
  constructor(private readonly c: Cfg) {}
  create(body: S.CreatePixPayinRequest, idempotencyKey: string) {
    return request<S.Payment>(this.c, "POST", "pix/payins", { body, idempotencyKey: requireKey(idempotencyKey) });
  }
  retrieve(id: string) {
    return request<S.Payment>(this.c, "GET", `pix/payins/${seg(id)}`);
  }
  /** Sandbox only: settle or fail a pending charge. */
  simulate(id: string, body: S.SimulateOutcomeRequest = { status: "completed" }) {
    return request<S.Payment>(this.c, "POST", `pix/payins/${seg(id)}/simulate`, { body });
  }
}

/**
 * PIX cash-out. Over the API the workspace's registered settlement key receives the money
 * (403 `settlement_key_required` until one is set in the dashboard).
 */
export class PixPayouts {
  constructor(private readonly c: Cfg) {}
  create(body: S.CreatePixPayoutRequest, idempotencyKey: string) {
    return request<S.Payment>(this.c, "POST", "pix/payouts", { body, idempotencyKey: requireKey(idempotencyKey) });
  }
  retrieve(id: string) {
    return request<S.Payment>(this.c, "GET", `pix/payouts/${seg(id)}`);
  }
  /** Fee and total debit before sending. */
  fee(body: S.PixPayoutFeeRequest) {
    return request<S.PixFeeQuote>(this.c, "POST", "pix/payouts/fee", { body });
  }
  /** Resolve a PIX key to its holder (masked) before paying. */
  lookup(body: S.PixDictLookupRequest) {
    return request<S.PixDictResult>(this.c, "POST", "pix/payouts/dict", { body });
  }
  /** Sandbox only. */
  simulate(id: string, body: S.SimulateOutcomeRequest = { status: "completed" }) {
    return request<S.Payment>(this.c, "POST", `pix/payouts/${seg(id)}/simulate`, { body });
  }
  listAccounts() {
    return request<S.PayoutAccount[]>(this.c, "GET", "pix/payout-accounts");
  }
}

export class Pix {
  readonly payins: PixPayins;
  readonly payouts: PixPayouts;
  constructor(c: Cfg) {
    this.payins = new PixPayins(c);
    this.payouts = new PixPayouts(c);
  }
}

/**
 * Quote then convert. Direction is always base_currency → quote_currency: you pay
 * `base_amount_minor` of base and receive quote. To buy USDT with BRL: base BRL, quote USDT.
 */
export class Conversions {
  constructor(private readonly c: Cfg) {}
  quote(body: Omit<S.RfqQuoteRequest, "side">) {
    return request<S.Quote>(this.c, "POST", "rfq/quote", { body });
  }
  /** A quote converts once; a second execute is 409 `quote_not_open`, an old one 410 `quote_expired`. */
  execute(quoteId: string, idempotencyKey?: string) {
    return request<S.RfqExecuteResult>(this.c, "POST", "rfq/execute", { body: { quote_id: quoteId }, idempotencyKey });
  }
  prices(symbols?: readonly string[]) {
    return request<Row>(this.c, "GET", "prices", { query: { symbols: symbols?.join(",") } });
  }
}

export class Crypto {
  constructor(private readonly c: Cfg) {}
  send(body: S.CreateCryptoSendRequest, idempotencyKey: string) {
    return request<S.Payment>(this.c, "POST", "crypto/send", { body, idempotencyKey: requireKey(idempotencyKey) });
  }
  /** Sandbox only. */
  simulate(id: string, body: S.SimulateOutcomeRequest = { status: "completed" }) {
    return request<S.Payment>(this.c, "POST", `crypto/send/${seg(id)}/simulate`, { body });
  }
  networks() {
    return request<S.PayableNetwork[]>(this.c, "GET", "crypto/networks");
  }
}

export class Customers {
  constructor(private readonly c: Cfg) {}
  list() {
    return request<S.Customer[]>(this.c, "GET", "customers");
  }
  create(body: S.CreateCustomerRequest) {
    return request<S.Customer>(this.c, "POST", "customers", { body });
  }
  retrieve(id: string) {
    return request<S.Customer>(this.c, "GET", `customers/${seg(id)}`);
  }
  update(id: string, body: S.UpdateCustomerRequest) {
    return request<S.Customer>(this.c, "PATCH", `customers/${seg(id)}`, { body });
  }
}

export class Kyc {
  constructor(private readonly c: Cfg) {}
  list() {
    return request<S.KycCase[]>(this.c, "GET", "kyc");
  }
  create(body: S.CreateKycCaseRequest) {
    return request<S.KycCase>(this.c, "POST", "kyc", { body });
  }
  retrieve(id: string) {
    return request<S.KycCase>(this.c, "GET", `kyc/${seg(id)}`);
  }
  listRfi(id: string) {
    return request<S.KycRfi[]>(this.c, "GET", `kyc/${seg(id)}/rfi`);
  }
  answerRfi(id: string, body: S.CreateKycRfiRequest) {
    return request<S.KycRfi>(this.c, "POST", `kyc/${seg(id)}/rfi`, { body });
  }
}

export class Whitelist {
  constructor(private readonly c: Cfg) {}
  list() {
    return request<S.WhitelistEntry[]>(this.c, "GET", "whitelist");
  }
  settings() {
    return request<S.WhitelistSettings>(this.c, "GET", "whitelist/settings");
  }
}

export class BankAccounts {
  constructor(private readonly c: Cfg) {}
  list() {
    return request<S.BankAccount[]>(this.c, "GET", "bank-accounts");
  }
}

export class Webhooks {
  constructor(private readonly c: Cfg) {}
  list() {
    return request<S.Webhook[]>(this.c, "GET", "webhooks");
  }
  /** The signing `secret` is in this reply only. Store it. */
  create(body: S.CreateWebhookRequest) {
    return request<S.WebhookCreateResponse>(this.c, "POST", "webhooks", { body });
  }
  retrieve(id: string) {
    return request<S.Webhook>(this.c, "GET", `webhooks/${seg(id)}`);
  }
  update(id: string, body: S.UpdateWebhookRequest) {
    return request<S.Webhook>(this.c, "PATCH", `webhooks/${seg(id)}`, { body });
  }
  delete(id: string) {
    return request<Deleted>(this.c, "DELETE", `webhooks/${seg(id)}`);
  }
  /** Queues a signed `test.ping` to the endpoint. */
  test(id: string) {
    return request<Row>(this.c, "POST", `webhooks/${seg(id)}/test`);
  }
  deliveries() {
    return request<S.WebhookDelivery[]>(this.c, "GET", "webhooks/deliveries");
  }
  resend(deliveryId: string) {
    return request<S.WebhookDelivery>(this.c, "POST", `webhooks/deliveries/${seg(deliveryId)}/resend`);
  }
}

export class PaymentLinks {
  constructor(private readonly c: Cfg) {}
  list() {
    return request<S.PaymentLink[]>(this.c, "GET", "payment-links");
  }
  create(body: S.CreatePaymentLinkRequest) {
    return request<S.PaymentLink>(this.c, "POST", "payment-links", { body });
  }
  retrieve(id: string) {
    return request<S.PaymentLink>(this.c, "GET", `payment-links/${seg(id)}`);
  }
  update(id: string, body: S.UpdatePaymentLinkRequest) {
    return request<S.PaymentLink>(this.c, "PATCH", `payment-links/${seg(id)}`, { body });
  }
  delete(id: string) {
    return request<S.PaymentLink>(this.c, "DELETE", `payment-links/${seg(id)}`);
  }
  /** Paginated: pass `next_cursor` back as `cursor`. */
  payments(params: CheckoutListParams = {}) {
    return request<{ readonly items: readonly S.Payment[]; readonly next_cursor?: string | null }>(
      this.c, "GET", "payment-links/payments", { query: params },
    );
  }
  stats(params: Omit<CheckoutListParams, "limit" | "cursor"> = {}) {
    return request<Row>(this.c, "GET", "payment-links/stats", { query: params });
  }
  splits(id: string) {
    return request<S.PaymentLinkSplit[]>(this.c, "GET", `payment-links/${seg(id)}/splits`);
  }
  replaceSplits(id: string, body: S.ReplaceLinkSplitsRequest) {
    return request<S.PaymentLinkSplit[]>(this.c, "PUT", `payment-links/${seg(id)}/splits`, { body });
  }
  charge(id: string, body: S.CreateCheckoutChargeRequest, idempotencyKey: string) {
    return request<S.CheckoutPaymentResult>(this.c, "POST", `payment-links/${seg(id)}/charges`, {
      body,
      idempotencyKey: requireKey(idempotencyKey),
    });
  }
}

/** The public checkout page of a payment link (no secret needed by the payer). */
export class Checkout {
  constructor(private readonly c: Cfg) {}
  retrieve(slug: string) {
    return request<S.PublicCheckout>(this.c, "GET", `checkout/${seg(slug)}`);
  }
  pay(slug: string, body: S.CheckoutPayRequest, idempotencyKey: string) {
    return request<S.CheckoutPaymentResult>(this.c, "POST", `checkout/${seg(slug)}/pay`, {
      body,
      idempotencyKey: requireKey(idempotencyKey),
    });
  }
}

export class SubAccounts {
  constructor(private readonly c: Cfg) {}
  list() {
    return request<S.SubAccount[]>(this.c, "GET", "subaccounts");
  }
  create(body: S.CreateSubAccountRequest) {
    return request<S.SubAccount>(this.c, "POST", "subaccounts", { body });
  }
  retrieve(id: string) {
    return request<S.SubAccount>(this.c, "GET", `subaccounts/${seg(id)}`);
  }
  balances(id: string) {
    return request<S.Balance[]>(this.c, "GET", `subaccounts/${seg(id)}/balances`);
  }
  transactions(id: string, params: { limit?: number } = {}) {
    return request<S.Payment[]>(this.c, "GET", `subaccounts/${seg(id)}/transactions`, { query: params });
  }
}

export class ApiKeys {
  constructor(private readonly c: Cfg) {}
  list() {
    return request<S.ApiKey[]>(this.c, "GET", "api-keys");
  }
  audit(params: { limit?: number } = {}) {
    return request<S.ApiKeyAuditEntry[]>(this.c, "GET", "api-keys/audit", { query: params });
  }
}

export class Settings {
  constructor(private readonly c: Cfg) {}
  retrieve() {
    return request<S.WorkspaceSettings>(this.c, "GET", "settings");
  }
}

export class Notifications {
  constructor(private readonly c: Cfg) {}
  list() {
    return request<S.Notification[]>(this.c, "GET", "notifications");
  }
}

export class Team {
  constructor(private readonly c: Cfg) {}
  list() {
    return request<Row[]>(this.c, "GET", "team");
  }
}

export class Workflows {
  constructor(private readonly c: Cfg) {}
  list() {
    return request<Row[]>(this.c, "GET", "workflows");
  }
  create(body: { name: string; enabled?: boolean; trigger?: string; steps?: readonly unknown[]; definition?: Row }) {
    return request<Row>(this.c, "POST", "workflows", { body });
  }
  runs() {
    return request<Row[]>(this.c, "GET", "workflows/runs");
  }
}

export class Approvals {
  constructor(private readonly c: Cfg) {}
  list() {
    return request<Row[]>(this.c, "GET", "approvals");
  }
  policy() {
    return request<Row>(this.c, "GET", "approval-policy");
  }
}

export class Treasury {
  constructor(private readonly c: Cfg) {}
  list() {
    return request<Row[]>(this.c, "GET", "treasury");
  }
  create(body: { address: string; network?: string; label?: string }) {
    return request<Row>(this.c, "POST", "treasury", { body });
  }
}

export const ping = (c: Cfg) => request<PingResponse>(c, "GET", "ping");
