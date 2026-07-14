export class ClarianError extends Error {
  readonly status: number;
  readonly code: string;
  readonly detail: string | undefined;
  readonly hint: string | undefined;

  constructor(status: number, body: { error: string; detail?: string; hint?: string }) {
    super(body.detail ?? body.error);
    this.name = "ClarianError";
    this.status = status;
    this.code = body.error;
    this.detail = body.detail;
    this.hint = body.hint;
  }
}
