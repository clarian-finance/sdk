from __future__ import annotations

import hashlib
import hmac
import json
import time
from datetime import datetime
from typing import Any, Mapping

DEFAULT_TOLERANCE_SECONDS = 300


def sign_webhook_payload(body: str, timestamp: str, secret: str) -> str:
    """Signs ``f"{timestamp}.{body}"`` exactly as Clarian does. For tests of your own handler."""
    return hmac.new(secret.encode(), f"{timestamp}.{body}".encode(), hashlib.sha256).hexdigest()


def _parse_ts(timestamp: str) -> float | None:
    try:
        # fromisoformat before 3.11 rejects a trailing Z.
        dt = datetime.fromisoformat(timestamp.replace("Z", "+00:00"))
    except (ValueError, AttributeError):
        return None
    return dt.timestamp() if dt.tzinfo else None


def verify_webhook_signature(
    body: str, signature: str, timestamp: str, secret: str, tolerance: float = DEFAULT_TOLERANCE_SECONDS
) -> bool:
    sent_at = _parse_ts(timestamp)
    # An unparseable or naive timestamp fails closed.
    if sent_at is None or abs(time.time() - sent_at) > tolerance:
        return False
    return hmac.compare_digest(sign_webhook_payload(body, timestamp, secret), signature or "")


def extract_webhook_headers(headers: Mapping[str, Any]) -> dict[str, str]:
    """Reads the ``x-clarian-*`` headers from any case-insensitive or plain mapping."""
    lower = {str(k).lower(): v for k, v in headers.items()}

    def get(name: str) -> str:
        v = lower.get(name, "")
        return (v[0] if v else "") if isinstance(v, (list, tuple)) else (v or "")

    return {k: get(f"x-clarian-{k}") for k in ("signature", "timestamp", "event")}


def construct_webhook_event(
    raw_body: str | bytes, headers: Mapping[str, Any], secret: str, tolerance: float = DEFAULT_TOLERANCE_SECONDS
) -> dict[str, Any]:
    """Verifies a delivery and returns its body ``{id, type, created_at, data}``.

    ``raw_body`` must be the exact bytes received. Raises ``ValueError`` on a bad signature or timestamp.
    Dedupe on ``event["id"]``: a delivery can arrive more than once.
    """
    body = raw_body.decode() if isinstance(raw_body, bytes) else raw_body
    h = extract_webhook_headers(headers)
    if not verify_webhook_signature(body, h["signature"], h["timestamp"], secret, tolerance):
        raise ValueError("Invalid webhook signature or timestamp outside tolerance")
    return json.loads(body)
