"""Webhook signature helpers (HMAC-SHA256 hex over timestamp.body)."""

from __future__ import annotations

import hashlib
import hmac
import time
from datetime import datetime, timezone
from email.utils import parsedate_to_datetime
from typing import Mapping, Sequence, Union

HEADER_SIGNATURE = "X-Clarian-Signature"
HEADER_TIMESTAMP = "X-Clarian-Timestamp"
HEADER_EVENT = "X-Clarian-Event"
HEADER_DELIVERY_ID = "X-Clarian-Delivery-Id"
HEADER_IDEMPOTENCY_KEY = "X-Clarian-Idempotency-Key"
HEADER_ATTEMPT = "X-Clarian-Attempt"

DEFAULT_TOLERANCE_SECONDS = 300

Headers = Mapping[str, Union[str, Sequence[str], None]]


def _as_bytes(payload: bytes | str) -> bytes:
    if isinstance(payload, bytes):
        return payload
    return payload.encode("utf-8")


def _as_str(payload: bytes | str) -> str:
    if isinstance(payload, str):
        return payload
    return payload.decode("utf-8")


def sign_payload(secret: str, timestamp: str, payload: bytes | str) -> str:
    """Return hex HMAC-SHA256 of ``f\"{timestamp}.{body}\"`` using secret."""
    body = _as_str(payload)
    mac = hmac.new(
        secret.encode("utf-8"),
        f"{timestamp}.{body}".encode("utf-8"),
        hashlib.sha256,
    )
    return mac.hexdigest()


def _parse_timestamp(timestamp: str) -> float:
    try:
        # ISO 8601 / RFC3339 (with or without Z)
        ts = timestamp.replace("Z", "+00:00")
        dt = datetime.fromisoformat(ts)
        if dt.tzinfo is None:
            dt = dt.replace(tzinfo=timezone.utc)
        return dt.timestamp()
    except ValueError:
        pass
    try:
        return float(timestamp)
    except ValueError:
        pass
    try:
        return parsedate_to_datetime(timestamp).timestamp()
    except (TypeError, ValueError, IndexError):
        raise ValueError(f"invalid webhook timestamp: {timestamp!r}") from None


def verify_signature(
    payload: bytes | str,
    timestamp: str,
    signature: str,
    secret: str,
    tolerance_seconds: int = DEFAULT_TOLERANCE_SECONDS,
) -> bool:
    """Verify HMAC-SHA256 signature and timestamp freshness.

    Signature is hex(HMAC-SHA256(secret, timestamp + "." + body)).
    Returns False on missing inputs, bad signature, or stale/future timestamp.
    """
    if not secret or not signature or not timestamp:
        return False

    try:
        event_ts = _parse_timestamp(timestamp)
    except ValueError:
        return False

    age = abs(time.time() - event_ts)
    if age > tolerance_seconds:
        return False

    expected = sign_payload(secret, timestamp, payload)
    return hmac.compare_digest(expected, signature)


def extract_headers(headers: Headers) -> tuple[str, str]:
    """Return ``(timestamp, signature)`` from Clarian delivery headers.

    Reads ``X-Clarian-Timestamp`` and ``X-Clarian-Signature`` (case-insensitive).
    Missing headers yield empty strings.
    """

    def get(name: str) -> str:
        lower = name.lower()
        for key, value in headers.items():
            if key.lower() != lower:
                continue
            if value is None:
                return ""
            if isinstance(value, (list, tuple)):
                return str(value[0]) if value else ""
            return str(value)
        return ""

    return get(HEADER_TIMESTAMP), get(HEADER_SIGNATURE)
