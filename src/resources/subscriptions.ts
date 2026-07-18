import type { HttpClientConfig } from "../http.js";
import { request } from "../http.js";
import type {
  Environment,
  Subscription,
  SubscriptionCancelParams,
  SubscriptionChangePlanParams,
  SubscriptionCreateRequest,
  SubscriptionCreateResponse,
  SubscriptionInvoice,
  SubscriptionListParams,
} from "../types.js";

export class Subscriptions {
  constructor(private readonly config: HttpClientConfig) {}

  async create(params: SubscriptionCreateRequest, idempotencyKey: string): Promise<SubscriptionCreateResponse> {
    if (!idempotencyKey) throw new Error("idempotencyKey is required for subscription create");
    const res = await request<{
      ok: true;
      environment: Environment;
      subscription: Subscription;
      invoice: SubscriptionCreateResponse["invoice"];
    }>(this.config, "POST", "subscriptions", params, { "Idempotency-Key": idempotencyKey });
    return { subscription: res.subscription, invoice: res.invoice };
  }

  async list(params?: SubscriptionListParams): Promise<Subscription[]> {
    const qs = params?.status ? `?status=${encodeURIComponent(params.status)}` : "";
    const res = await request<{ ok: true; environment: Environment; subscriptions: Subscription[] }>(
      this.config, "GET", `subscriptions${qs}`,
    );
    return res.subscriptions;
  }

  async retrieve(id: string): Promise<Subscription> {
    const res = await request<{ ok: true; environment: Environment; subscription: Subscription }>(
      this.config, "GET", `subscriptions/${id}`,
    );
    return res.subscription;
  }

  async listInvoices(id: string): Promise<SubscriptionInvoice[]> {
    const res = await request<{ ok: true; environment: Environment; invoices: SubscriptionInvoice[] }>(
      this.config, "GET", `subscriptions/${id}/invoices`,
    );
    return res.invoices;
  }

  async cancel(id: string, params?: SubscriptionCancelParams): Promise<Subscription> {
    const res = await request<{ ok: true; environment: Environment; subscription: Subscription }>(
      this.config, "POST", `subscriptions/${id}/cancel`, params || {},
    );
    return res.subscription;
  }

  async changePlan(id: string, params: SubscriptionChangePlanParams): Promise<Subscription> {
    const res = await request<{ ok: true; environment: Environment; subscription: Subscription }>(
      this.config, "POST", `subscriptions/${id}/change-plan`, {
        product_id: params.productId ?? params.product_id,
      },
    );
    return res.subscription;
  }
}
