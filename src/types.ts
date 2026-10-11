export type Environment = "live" | "sandbox";

export interface ClarianConfig {
  /** `cl_live_sk_…` (live) or `cl_test_sk_…` (sandbox). The key picks the environment. */
  readonly apiKey: string;
  readonly workspaceId: string;
  /** API origin. Default `https://api.clarian.finance`. */
  readonly baseUrl?: string;
  /** Per-request timeout in ms. Default 30000. */
  readonly timeout?: number;
  /** Act on one child workspace (wallet-as-a-service). Same as `client.forSubaccount(id)`. */
  readonly subaccountId?: string;
  /** Retries for 429/502/503/504 and network errors on reads and idempotent creates. Default 2. */
  readonly maxRetries?: number;
  /** Custom fetch (tests, proxies). Default global fetch. */
  readonly fetch?: typeof fetch;
}

/** Amounts are integer minor units: BRL scale 2 (R$ 10,00 = "1000"), USDT/USDC scale 6. */
export type MinorAmount = string | number | bigint;

export interface PingResponse {
  readonly pong?: boolean;
  readonly environment?: Environment;
  readonly workspace_id?: string;
  readonly [key: string]: unknown;
}

export interface ListParams {
  readonly limit?: number;
  /** ISO timestamp: return items created before it (keyset pagination). */
  readonly before?: string;
}

export interface StatementParams extends ListParams {
  readonly from?: string;
  readonly to?: string;
  readonly currency?: string;
  readonly kind?: string;
}

export interface CheckoutListParams {
  readonly limit?: number;
  readonly cursor?: string;
  readonly from?: string;
  readonly to?: string;
  readonly link_id?: string;
  readonly status?: string;
  readonly payer?: string;
}

/** Rows the spec does not model yet (team, workflows, approvals, treasury). */
export type Row = Readonly<Record<string, unknown>>;

/** Webhook event names the API emits. `test.ping` comes from `webhooks.test()`. */
export type WebhookEventType = "payment.completed" | "payment.failed" | "test.ping";

/** Body of a delivery: `{id, type, created_at, data}`. */
export interface WebhookEvent<T = PaymentEventData> {
  readonly id: string;
  readonly type: WebhookEventType | (string & {});
  readonly created_at: string;
  readonly data: T;
}

export interface PaymentEventData {
  readonly type: string;
  readonly payment_id: string;
  readonly kind: string;
  readonly status: string;
  readonly amount_minor: string;
  readonly currency: string;
  readonly payment_link_id: string | null;
}
