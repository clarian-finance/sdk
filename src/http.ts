import { ClarianError } from "./errors.js";
import type { Environment } from "./types.js";

const DEFAULT_BASE_URL = "https://api.clarian.finance/functions/v1/api-gateway";
const DEFAULT_TIMEOUT = 30_000;

export interface HttpClientConfig {
  apiKey: string;
  workspaceId: string;
  baseUrl: string;
  timeout: number;
}

export function createHttpConfig(opts: {
  apiKey: string;
  workspaceId: string;
  baseUrl?: string;
  timeout?: number;
}): HttpClientConfig {
  return {
    apiKey: opts.apiKey,
    workspaceId: opts.workspaceId,
    baseUrl: opts.baseUrl ?? DEFAULT_BASE_URL,
    timeout: opts.timeout ?? DEFAULT_TIMEOUT,
  };
}

export function resolveEnvironment(apiKey: string): Environment {
  if (apiKey.startsWith("cl_live_sk_")) return "production";
  if (apiKey.startsWith("cl_test_sk_")) return "sandbox";
  throw new Error("Invalid API key format: must start with cl_live_sk_ or cl_test_sk_");
}

export async function request<T>(
  config: HttpClientConfig,
  method: string,
  path: string,
  body?: unknown,
  headers?: Record<string, string>,
): Promise<T> {
  const env = resolveEnvironment(config.apiKey);
  const envSegment = env === "production" ? "live" : "test";
  const url = `${config.baseUrl}/${envSegment}/${path}`;

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), config.timeout);

  const reqHeaders: Record<string, string> = {
    "Authorization": `Bearer ${config.apiKey}`,
    "X-Workspace-Id": config.workspaceId,
    "Content-Type": "application/json",
    ...headers,
  };

  try {
    const resp = await fetch(url, {
      method,
      headers: reqHeaders,
      body: body ? JSON.stringify(body) : undefined,
      signal: controller.signal,
    });

    const json = await resp.json() as Record<string, unknown>;

    if (!resp.ok) {
      throw new ClarianError(resp.status, {
        error: String(json.error ?? "unknown_error"),
        detail: json.detail as string | undefined,
        hint: json.hint as string | undefined,
      });
    }

    return json as T;
  } finally {
    clearTimeout(timer);
  }
}
