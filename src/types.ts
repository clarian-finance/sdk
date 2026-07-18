// ── Configuration ───────────────────────────────────────────

export interface ClarianConfig {
  apiKey: string;
  workspaceId: string;
  baseUrl?: string;
  timeout?: number;
}

export type Environment = "production" | "sandbox";

// ── Ping ────────────────────────────────────────────────────

export interface PingResponse {
  ok: true;
  environment: Environment;
  master_account_id: string;
  scope: string;
  customer_id: string | null;
}

// ── Shared ──────────────────────────────────────────────────

export interface ApiResponse<T> {
  ok: true;
  environment: Environment;
  data: T;
}

export interface ApiError {
  error: string;
  detail?: string;
  hint?: string;
}

// ── Structured Error Metadata ───────────────────────────────

export interface InsufficientBalanceError {
  error: "insufficient_balance";
  detail: string;
  available: number;
  requested: number;
  fee: number;
  fee_bearer: string;
  total_required: number;
  max_withdrawable: number;
}

// ── RFQ ─────────────────────────────────────────────────────

export interface QuoteRequest {
  base_currency: string;
  quote_currency: string;
  amount: number;
  amount_currency: string;
  spread_fee_id?: string;
}

export interface Quote {
  quote_id: string;
  base_currency: string;
  quote_currency: string;
  side: string;
  base_size: string;
  quote_size: string;
  rate: string;
  expires_at: string;
}

export interface ExecuteRequest {
  quote_id: string;
}

export interface ExecuteResult {
  [key: string]: unknown;
}

// ── PIX Cash-in ─────────────────────────────────────────────

export interface PayerDocument {
  number: string;
  type?: "CPF" | "CNPJ";
}

export interface Payer {
  name: string;
  document: PayerDocument;
}

export interface CashInCreateRequest {
  amount: number;
  payer: Payer;
  description?: string;
  expiration_seconds?: number;
  external_id?: string;
  customer_id?: string;
}

export interface CashInOrder {
  id: string;
  status: string;
  amount: number;
  currency: string;
  pix: {
    qr_code: string | null;
    copy_paste: string | null;
    end_to_end_id: string | null;
  };
  expires_at: string | null;
  fee?: number;
  description?: string;
  created_at?: string;
  updated_at?: string;
}

// ── PIX Cash-out ────────────────────────────────────────────

export interface CashOutCreateRequest {
  amount: number;
  pix_key?: string;
  pix_key_type?: string;
  bank_account_id?: string;
  dict?: Record<string, unknown>;
  description?: string;
  external_id?: string;
  source_customer_id?: string;
  initiation_type?: string;
}

export interface CashOutOrder {
  id: string;
  status: string;
  end_to_end_id: string | null;
  amount: number;
  pix_amount?: number;
  currency: string;
  fee?: number;
  fee_bearer?: string;
  destination?: string;
  description?: string;
  created_at?: string;
  updated_at?: string;
}

// ── Balances ────────────────────────────────────────────────

export interface Balance {
  currency: string;
  available: number;
  pending: number;
  locked: number;
}

// ── Wallets ─────────────────────────────────────────────────

export type WalletNetwork =
  | "ethereum"
  | "polygon"
  | "solana"
  | "tron"
  | "base";

export interface Wallet {
  id: string;
  network: WalletNetwork;
  address: string;
  is_auto_provisioned: boolean;
  created_at: string;
}

export interface OnChainBalance {
  currency: string;
  amount: string;
}

// ── Transactions ────────────────────────────────────────────

export type TransactionType =
  | "pix_in"
  | "pix_out"
  | "on_ramp_deposit"
  | "off_ramp_payout"
  | "card_in";

export type TransactionStatus =
  | "pending"
  | "processing"
  | "completed"
  | "failed"
  | "cancelled";

export interface Transaction {
  id: string;
  type: TransactionType;
  status: TransactionStatus;
  amount: number;
  fee: number;
  currency: string;
  description: string | null;
  external_id: string | null;
  entity_id: string | null;
  entity_type: string | null;
  environment: Environment;
  created_at: string;
  updated_at: string;
}

export interface TransactionListParams {
  limit?: number;
  type?: TransactionType;
  status?: TransactionStatus;
}

// ── Webhooks ────────────────────────────────────────────────

export type WebhookEvent =
  | "pix_payin.created"
  | "pix_payin.completed"
  | "pix_payin.failed"
  | "pix_payin.expired"
  | "pix_payout.created"
  | "pix_payout.completed"
  | "pix_payout.failed"
  | "checkout.completed"
  | "checkout.expired"
  | "checkout.failed"
  | "checkout.paid"
  | "rfq.executed"
  | "rfq.failed"
  | "card.charge.paid"
  | "card.charge.failed"
  | "card.charge.refunded"
  | "card.charge.chargeback"
  | "subscription.created"
  | "subscription.activated"
  | "subscription.invoice.created"
  | "subscription.invoice.paid"
  | "subscription.invoice.failed"
  | "subscription.past_due"
  | "subscription.cancelled"
  | "subscription.plan_changed";

// ── Products / Plans ────────────────────────────────────────

export type BillingCycle = "weekly" | "monthly" | "semiannually" | "annually";

export interface ProductCreateRequest {
  name: string;
  price_cents: number;
  currency?: string;
  description?: string;
  image_url?: string;
  sku?: string;
  cycle?: BillingCycle;
  external_id?: string;
}

export interface Product {
  id: string;
  name: string;
  description: string | null;
  price_cents: number;
  currency: string;
  image_url: string | null;
  sku: string | null;
  active: boolean;
  cycle: BillingCycle | null;
  external_id: string | null;
  environment: Environment;
  created_at: string;
  updated_at?: string;
}

export interface ProductListParams {
  cycle?: BillingCycle;
}

// ── Cards ───────────────────────────────────────────────────

export interface CardToken {
  id: string;
  brand: string | null;
  last4: string | null;
  exp_month: number | null;
  exp_year: number | null;
  holder_name: string | null;
  status: string;
  customer_id: string | null;
  created_at: string;
}

export interface CardChargeCreateRequest {
  amount_cents: number;
  card_token_id: string;
  installments?: number;
  customer_id?: string;
  external_id?: string;
  metadata?: Record<string, unknown>;
}

export interface CardCharge {
  id: string;
  amount_cents: number;
  currency: string;
  installments: number;
  status: string;
  fee_cents: number;
  card_token_id: string | null;
  external_id: string | null;
  failure_code?: string | null;
  failure_message?: string | null;
  created_at: string;
  updated_at?: string;
}

// ── Subscriptions ───────────────────────────────────────────

export interface SubscriptionCreateRequest {
  product_id?: string;
  external_product_id?: string;
  payment_method: "pix" | "card";
  customer_id?: string;
  payer?: { name?: string; document?: string | { number: string }; email?: string };
  payer_name?: string;
  payer_document?: string;
  payer_email?: string;
  card_token_id?: string;
  external_id?: string;
  metadata?: Record<string, unknown>;
}

export interface Subscription {
  id: string;
  product_id: string;
  customer_id: string | null;
  payment_method: "pix" | "card";
  cycle: BillingCycle;
  amount_cents: number;
  status: string;
  current_period_start: string | null;
  current_period_end: string | null;
  next_billing_at: string | null;
  cancel_at_period_end: boolean;
  external_id: string | null;
  created_at: string;
  updated_at?: string;
}

export interface SubscriptionInvoice {
  id: string;
  sequence: number;
  amount_cents: number;
  fee_cents: number;
  payment_method: "pix" | "card";
  status: string;
  period_start: string;
  period_end: string;
  due_at: string;
  paid_at: string | null;
  pix: { emv: string | null; qr_base64: string | null; expires_at: string | null } | null;
  created_at?: string;
}

export interface SubscriptionCreateResponse {
  subscription: Subscription;
  invoice: {
    id: string;
    sequence: number;
    amount_cents: number;
    fee_cents: number;
    status: string;
    due_at: string;
    pix: { emv: string | null; qr_base64: string | null; expires_at: string | null; end_to_end_id?: string | null } | null;
    charge: CardCharge | Record<string, unknown> | null;
  };
}

export interface SubscriptionListParams {
  status?: string;
}

export interface SubscriptionCancelParams {
  at_period_end?: boolean;
}

export interface SubscriptionChangePlanParams {
  productId?: string;
  product_id?: string;
}

export interface WebhookSubscription {
  id: string;
  url: string;
  events: WebhookEvent[];
  is_active: boolean;
  description: string | null;
  environment: Environment;
  created_at: string;
  updated_at?: string;
}

export interface WebhookCreateRequest {
  url: string;
  events: WebhookEvent[];
  description?: string;
}

export interface WebhookUpdateRequest {
  url?: string;
  events?: WebhookEvent[];
  is_active?: boolean;
  description?: string;
}

export interface WebhookCreateResponse {
  subscription: WebhookSubscription;
  secret: string;
}

// ── Webhook Payload ─────────────────────────────────────────

export interface WebhookPayload<T = unknown> {
  id: string;
  event: WebhookEvent;
  timestamp: string;
  idempotency_key: string;
  attempt: number;
  data: T;
}
