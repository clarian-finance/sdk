import type { HttpClientConfig } from "../http.js";
import { request } from "../http.js";
import { constructWebhookEvent, extractWebhookHeaders, verifyWebhookSignature } from "../webhooks.js";
import type {
  WebhookSubscription, WebhookCreateRequest, WebhookUpdateRequest,
  WebhookCreateResponse, WebhookPayload, Environment,
} from "../types.js";

export class WebhooksAPI {
  constructor(private readonly config: HttpClientConfig) {}

  async verify<T = unknown>(
    rawBody: string,
    headers: Record<string, string | string[] | undefined>,
    secret: string,
    toleranceMs?: number,
  ): Promise<WebhookPayload<T>> {
    return constructWebhookEvent<T>(rawBody, extractWebhookHeaders(headers), secret, toleranceMs);
  }

  extractHeaders(headers: Record<string, string | string[] | undefined>) {
    return extractWebhookHeaders(headers);
  }

  async verifySignature(body: string, signature: string, timestamp: string, secret: string, toleranceMs?: number): Promise<boolean> {
    return verifyWebhookSignature(body, signature, timestamp, secret, toleranceMs);
  }

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
