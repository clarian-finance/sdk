export class ClarianError extends Error {
  readonly status: number;
  readonly code: string;
  readonly detail: string | undefined;
  readonly hint: string | undefined;
  readonly meta: Record<string, unknown>;

  constructor(status: number, body: Record<string, unknown>) {
    const error = String(body.error ?? "unknown_error");
    const detail = body.detail as string | undefined;
    super(detail ?? error);
    this.name = "ClarianError";
    this.status = status;
    this.code = error;
    this.detail = detail;
    this.hint = body.hint as string | undefined;
    const { error: _e, detail: _d, hint: _h, ...rest } = body;
    this.meta = rest;
  }
}
