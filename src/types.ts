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

export interface DictCheckRequest {
  pix_key: string;
  key_type?: string;
}

export interface DictCheckResult {
  [key: string]: unknown;
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
  | "off_ramp_payout";

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
  | "rfq.executed"
  | "rfq.failed";

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
