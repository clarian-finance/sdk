import type { HttpClientConfig } from "../http.js";
import { request } from "../http.js";
import type {
  WebhookSubscription, WebhookCreateRequest, WebhookUpdateRequest,
  WebhookCreateResponse, Environment,
} from "../types.js";

export class WebhooksAPI {
  constructor(private readonly config: HttpClientConfig) {}

  async list(): Promise<WebhookSubscription[]> {
    const res = await request<{ ok: true; environment: Environment; subscriptions: WebhookSubscription[] }>(
      this.config, "GET", "webhooks",
    );
    return res.subscriptions;
  }

  async create(params: WebhookCreateRequest): Promise<WebhookCreateResponse> {
    const res = await request<{
      ok: true; environment: Environment; subscription: WebhookSubscription; secret: string;
    }>(this.config, "POST", "webhooks", params);
    return { subscription: res.subscription, secret: res.secret };
  }

  async retrieve(id: string): Promise<WebhookSubscription> {
    const res = await request<{ ok: true; environment: Environment; subscription: WebhookSubscription }>(
      this.config, "GET", `webhooks/${id}`,
    );
    return res.subscription;
  }

  async update(id: string, params: WebhookUpdateRequest): Promise<WebhookSubscription> {
    const res = await request<{ ok: true; environment: Environment; subscription: WebhookSubscription }>(
      this.config, "PATCH", `webhooks/${id}`, params,
    );
    return res.subscription;
  }

  async delete(id: string): Promise<void> {
    await request(this.config, "DELETE", `webhooks/${id}`);
  }
}
