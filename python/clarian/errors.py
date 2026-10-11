from __future__ import annotations

from typing import Any, Mapping


class ClarianError(Exception):
    """Every non-2xx reply. ``code`` is the API's stable ``error`` string, e.g. ``insufficient_balance``."""

    def __init__(self, status: int, body: Mapping[str, Any]) -> None:
        self.status = status
        self.code = body["error"] if isinstance(body.get("error"), str) else "unknown_error"
        self.message = body["message"] if isinstance(body.get("message"), str) else self.code
        self.meta = {k: v for k, v in body.items() if k not in ("error", "message")}
        super().__init__(self.message)
