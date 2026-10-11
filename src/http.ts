import { ClarianError } from "./errors.js";
import type { Environment } from "./types.js";

const DEFAULT_BASE_URL = "https://api.clarian.finance";
const DEFAULT_TIMEOUT = 30_000;

export interface HttpClientConfig {
  readonly apiKey: string;
  readonly workspaceId: string;
  readonly baseUrl: string;
  readonly timeout: number;
  readonly environment: Environment;
  readonly subaccountId?: string;
  readonly fetch: typeof fetch;
  readonly maxRetries: number;
}

export function resolveEnvironment(apiKey: string): Environment {
  if (apiKey.startsWith("cl_live_sk_")) return "live";
  if (apiKey.startsWith("cl_test_sk_")) return "sandbox";
  throw new Error("Invalid API key format: must start with cl_live_sk_ or cl_test_sk_");
}

export function createHttpConfig(opts: {
  apiKey: string;
  workspaceId: string;
  baseUrl?: string;
  timeout?: number;
  subaccountId?: string;
  fetch?: typeof fetch;
  maxRetries?: number;
}): HttpClientConfig {
  return {
    apiKey: opts.apiKey,
    workspaceId: opts.workspaceId,
    baseUrl: (opts.baseUrl ?? DEFAULT_BASE_URL).replace(/\/+$/, ""),
    timeout: opts.timeout ?? DEFAULT_TIMEOUT,
    environment: resolveEnvironment(opts.apiKey),
    subaccountId: opts.subaccountId,
    fetch: opts.fetch ?? globalThis.fetch.bind(globalThis),
    maxRetries: opts.maxRetries ?? 2,
  };
}

// An object of string/number fields; undefined fields are dropped.
export type Query = object;

export interface RequestOptions {
  readonly query?: Query;
  readonly body?: unknown;
  /** Required by the API on money-moving creates; retries with the same key never double-send. */
  readonly idempotencyKey?: string;
  /** Fresh TOTP code, for the few owner/admin actions that ask for it. */
  readonly otpCode?: string;
}

// bigint amounts go on the wire as strings: JSON.stringify throws on bigint otherwise.
const toJson = (body: unknown) =>
  JSON.stringify(body, (_k, v: unknown) => (typeof v === "bigint" ? v.toString() : v));

const queryString = (query: Query | undefined) => {
  const pairs = Object.entries(query ?? {}).filter(
    (e): e is [string, string | number] => typeof e[1] === "string" || typeof e[1] === "number",
  );
  return pairs.length === 0 ? "" : `?${new URLSearchParams(pairs.map(([k, v]) => [k, String(v)])).toString()}`;
};

const parseBody = async (resp: Response): Promise<unknown> => {
  const text = await resp.text();
  if (text === "") return {};
  try {
    return JSON.parse(text);
  } catch {
    return { error: "invalid_json_response", message: text.slice(0, 500) };
  }
};

const RETRY_STATUS = new Set([429, 502, 503, 504]);
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

const backoffMs = (attempt: number, retryAfter: string | null) => {
  const seconds = Number(retryAfter);
  if (retryAfter !== null && Number.isFinite(seconds) && seconds >= 0) return Math.min(seconds, 30) * 1000;
  return Math.random() * Math.min(8000, 500 * 2 ** attempt);
};

async function send(config: HttpClientConfig, url: string, init: RequestInit, retriesLeft: number, attempt = 0): Promise<Response> {
  const result = await config
    .fetch(url, { ...init, signal: AbortSignal.timeout(config.timeout) })
    .then((resp) => ({ resp }), (error: unknown) => ({ error }));
  const again = "resp" in result ? RETRY_STATUS.has(result.resp.status) : true;
  if (!again || retriesLeft <= 0) {
    if ("error" in result) throw result.error;
    return result.resp;
  }
  await sleep(backoffMs(attempt, "resp" in result ? result.resp.headers.get("retry-after") : null));
  return send(config, url, init, retriesLeft - 1, attempt + 1);
}

/** Calls `/v1/{live|test}/<path>` and returns the `data` member of the reply. */
export async function request<T>(
  config: HttpClientConfig,
  method: "GET" | "POST" | "PUT" | "PATCH" | "DELETE",
  path: string,
  opts: RequestOptions = {},
): Promise<T> {
  const envSegment = config.environment === "live" ? "live" : "test";
  const url = `${config.baseUrl}/v1/${envSegment}/${path}${queryString(opts.query)}`;
  const headers: Record<string, string> = {
    Authorization: `Bearer ${config.apiKey}`,
    "X-Workspace-Id": config.workspaceId,
    ...(opts.body !== undefined ? { "Content-Type": "application/json" } : {}),
    ...(config.subaccountId ? { "X-Subaccount-Id": config.subaccountId } : {}),
    ...(opts.idempotencyKey ? { "Idempotency-Key": opts.idempotencyKey } : {}),
    ...(opts.otpCode ? { "X-OTP-Code": opts.otpCode } : {}),
  };
  const init = { method, headers, body: opts.body !== undefined ? toJson(opts.body) : undefined };
  // Only a request that cannot double-apply is retried: reads, deletes, or a create with an idempotency key.
  const retryable = method === "GET" || method === "DELETE" || opts.idempotencyKey !== undefined;
  const resp = await send(config, url, init, retryable ? config.maxRetries : 0);
  const json = await parseBody(resp);
  const obj = typeof json === "object" && json !== null ? (json as Record<string, unknown>) : {};
  if (!resp.ok) throw new ClarianError(resp.status, obj);
  return ("data" in obj ? obj.data : obj) as T;
}

export const seg = (id: string) => encodeURIComponent(id);
