"""Sandbox-only helpers and constants."""

from __future__ import annotations

from typing import TYPE_CHECKING, Any, Optional
from urllib.parse import quote

if TYPE_CHECKING:
    from .client import Clarian

SANDBOX_FAIL_PIX_KEY = "fail@sandbox.clarian"
SANDBOX_PENDING_PIX_KEY = "pending@sandbox.clarian"

EVENT_PIX_PAYIN_CREATED = "pix_payin.created"
EVENT_PIX_PAYIN_COMPLETED = "pix_payin.completed"
EVENT_PIX_PAYIN_EXPIRED = "pix_payin.expired"
EVENT_PIX_PAYOUT_CREATED = "pix_payout.created"
EVENT_PIX_PAYOUT_COMPLETED = "pix_payout.completed"
EVENT_PIX_PAYOUT_FAILED = "pix_payout.failed"
EVENT_CHECKOUT_PAID = "checkout.paid"

_CASH_IN_STATUSES = frozenset({"completed", "expired", "failed"})
_CASH_OUT_STATUSES = frozenset({"completed", "failed"})


class SandboxService:
    """Sandbox-only test helpers. Refuse live keys before any HTTP call."""

    def __init__(self, client: Clarian) -> None:
        self._c = client

    def _require_test_key(self) -> None:
        if not self._c.api_key.startswith("cl_test_sk_"):
            raise ValueError("sandbox helpers require a cl_test_sk_ key")

    def simulate_cash_in(
        self, order_id: str, status: Optional[str] = None
    ) -> dict[str, Any]:
        """POST /pix/payins/{id}/simulate; default status is completed."""
        self._require_test_key()
        resolved = status if status else "completed"
        if resolved not in _CASH_IN_STATUSES:
            raise ValueError(f"invalid cash-in simulate status {resolved!r}")
        path = f"/pix/payins/{quote(order_id, safe='')}/simulate"
        return self._c._request("POST", path, body={"status": resolved})

    def simulate_cash_out(
        self, order_id: str, status: Optional[str] = None
    ) -> dict[str, Any]:
        """POST /pix/payouts/{id}/simulate; default status is completed."""
        self._require_test_key()
        resolved = status if status else "completed"
        if resolved not in _CASH_OUT_STATUSES:
            raise ValueError(f"invalid cash-out simulate status {resolved!r}")
        path = f"/pix/payouts/{quote(order_id, safe='')}/simulate"
        return self._c._request("POST", path, body={"status": resolved})

    def send_webhook_event(
        self, subscription_id: str, event_type: str
    ) -> dict[str, Any]:
        """POST /webhooks/{id}/test: enqueue a sample delivery."""
        self._require_test_key()
        path = f"/webhooks/{quote(subscription_id, safe='')}/test"
        res = self._c._request("POST", path, body={"event_type": event_type})
        enqueued = res.get("enqueued") or {}
        delivery_id = enqueued.get("delivery_id")
        return {
            "ok": res.get("ok"),
            "environment": res.get("environment"),
            "event_type": res.get("event_type"),
            "enqueued": enqueued.get("enqueued", 0),
            "delivery_id": delivery_id if delivery_id is not None else "",
        }

    def resend_webhook_delivery(self, delivery_id: str) -> str:
        """POST /webhooks/deliveries/{id}/resend; returns new delivery id."""
        self._require_test_key()
        path = f"/webhooks/deliveries/{quote(delivery_id, safe='')}/resend"
        res = self._c._request("POST", path)
        return str(res.get("delivery_id") or "")
