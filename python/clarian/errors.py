"""API error types."""

from __future__ import annotations

import json
from typing import Any, Mapping


class ClarianError(Exception):
    """Raised for any non-2xx HTTP response from the Clarian API.

    Attributes:
        status: HTTP status code.
        code: Value of the JSON ``error`` field when present.
        message: Human-readable message (detail, code, or truncated body).
        meta: Remaining JSON fields after error/detail/hint are peeled off.
        body: Full raw response body (untruncated).
    """

    def __init__(self, status: int, body: str | bytes | None = None) -> None:
        if body is None:
            raw = ""
        elif isinstance(body, bytes):
            raw = body.decode("utf-8", errors="replace")
        else:
            raw = body

        code = ""
        detail = ""
        meta: dict[str, Any] = {}
        if raw:
            try:
                parsed = json.loads(raw)
            except (json.JSONDecodeError, TypeError):
                parsed = None
            if isinstance(parsed, Mapping):
                err_val = parsed.get("error")
                if err_val is not None:
                    code = str(err_val)
                detail_val = parsed.get("detail")
                if detail_val is not None:
                    detail = str(detail_val)
                meta = {
                    k: v
                    for k, v in parsed.items()
                    if k not in ("error", "detail", "hint")
                }
                hint = parsed.get("hint")
                if hint is not None:
                    meta = {**meta, "hint": hint}

        # Keep exception text log-safe: raw bodies may echo PII.
        truncated = raw if len(raw) <= 500 else raw[:500]
        if detail:
            message = detail
        elif code:
            message = code
        else:
            message = truncated

        if code:
            exc_msg = f"HTTP {status}: {code}"
        elif truncated:
            exc_msg = f"HTTP {status}: {truncated}"
        else:
            exc_msg = f"HTTP {status}"

        super().__init__(exc_msg)
        self.status = status
        self.code = code
        self.message = message
        self.meta = meta
        self.body = raw
