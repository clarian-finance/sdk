/** Every non-2xx reply. `code` is the API's stable `error` string, e.g. `insufficient_balance`. */
export class ClarianError extends Error {
  readonly status: number;
  readonly code: string;
  readonly meta: Readonly<Record<string, unknown>>;

  constructor(status: number, body: Readonly<Record<string, unknown>>) {
    const code = typeof body.error === "string" ? body.error : "unknown_error";
    super(typeof body.message === "string" ? body.message : code);
    this.name = "ClarianError";
    this.status = status;
    this.code = code;
    const { error: _e, message: _m, ...rest } = body;
    this.meta = rest;
  }
}
