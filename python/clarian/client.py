"""Clarian Finance API client (stdlib urllib only)."""

from __future__ import annotations

import json
import urllib.error
import urllib.request
from typing import Any, Callable, Mapping, Optional
from urllib.parse import quote, urlencode

from .errors import ClarianError
from .sandbox import SandboxService

BASE_URL_LIVE = "https://api.clarian.finance/functions/v1/api-gateway/live"
BASE_URL_TEST = "https://api.clarian.finance/functions/v1/api-gateway/test"

DEFAULT_TIMEOUT = 30.0

# (method, url, headers, body_bytes) -> (status_code, response_body_bytes)
Transport = Callable[
    [str, str, Mapping[str, str], Optional[bytes], float],
    tuple[int, bytes],
]


def _default_transport(
    method: str,
    url: str,
    headers: Mapping[str, str],
    body: Optional[bytes],
    timeout: float,
) -> tuple[int, bytes]:
    req = urllib.request.Request(
        url,
        data=body,
        method=method,
        headers=dict(headers),
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return int(resp.status), resp.read()
    except urllib.error.HTTPError as exc:
        return int(exc.code), exc.read()


def _infer_base_url(api_key: str) -> str:
    if api_key.startswith("cl_test_sk_"):
        return BASE_URL_TEST
    return BASE_URL_LIVE


class CashInService:
    def __init__(self, client: Clarian) -> None:
        self._c = client

    def create(
        self,
        params: Mapping[str, Any],
        idempotency_key: str,
    ) -> dict[str, Any]:
        """POST /cash-in/pix: generate a dynamic PIX charge."""
        return self._c._request(
            "POST",
            "/cash-in/pix",
            body=dict(params),
            idempotency_key=idempotency_key,
        )

    def retrieve(self, order_id: str) -> dict[str, Any]:
        """GET /cash-in/{id}."""
        return self._c._request("GET", f"/cash-in/{quote(order_id, safe='')}")


class CashOutService:
    def __init__(self, client: Clarian) -> None:
        self._c = client

    def dict_check(
        self,
        pix_key: str,
        key_type: Optional[str] = None,
    ) -> dict[str, Any]:
        """POST /pix/payouts/dict: preview PIX key owner before payout."""
        body: dict[str, Any] = {"pix_key": pix_key}
        if key_type:
            body["key_type"] = key_type
        return self._c._request("POST", "/pix/payouts/dict", body=body)

    def create(
        self,
        params: Mapping[str, Any],
        idempotency_key: str,
    ) -> dict[str, Any]:
        """POST /cash-out/pix: send BRL via PIX (idempotency key required)."""
        return self._c._request(
            "POST",
            "/cash-out/pix",
            body=dict(params),
            idempotency_key=idempotency_key,
        )

    def retrieve(self, order_id: str) -> dict[str, Any]:
        """GET /cash-out/{id}."""
        return self._c._request("GET", f"/cash-out/{quote(order_id, safe='')}")


class BalancesService:
    def __init__(self, client: Clarian) -> None:
        self._c = client

    def list(self) -> list[dict[str, Any]]:
        """GET /account/balances."""
        res = self._c._request("GET", "/account/balances")
        balances = res.get("balances")
        return list(balances) if isinstance(balances, list) else []


class TransactionsService:
    def __init__(self, client: Clarian) -> None:
        self._c = client

    def list(
        self,
        *,
        type: Optional[str] = None,
        status: Optional[str] = None,
        limit: Optional[int] = None,
    ) -> list[dict[str, Any]]:
        """GET /transactions with optional type/status/limit filters."""
        q: dict[str, str] = {}
        if type:
            q["type"] = type
        if status:
            q["status"] = status
        if limit is not None and limit > 0:
            q["limit"] = str(limit)
        path = "/transactions"
        if q:
            path = f"{path}?{urlencode(q)}"
        res = self._c._request("GET", path)
        txs = res.get("transactions")
        return list(txs) if isinstance(txs, list) else []

    def retrieve(self, transaction_id: str) -> dict[str, Any]:
        """GET /transactions/{id}."""
        res = self._c._request(
            "GET", f"/transactions/{quote(transaction_id, safe='')}"
        )
        tx = res.get("transaction")
        return dict(tx) if isinstance(tx, Mapping) else res


class WalletsService:
    def __init__(self, client: Clarian) -> None:
        self._c = client

    def list(self, network: Optional[str] = None) -> list[dict[str, Any]]:
        """GET /wallets: on-chain wallets (optional network filter)."""
        path = "/wallets"
        if network:
            path = f"{path}?{urlencode({'network': network})}"
        res = self._c._request("GET", path)
        wallets = res.get("wallets")
        return list(wallets) if isinstance(wallets, list) else []

    def retrieve_balance(self, wallet_id: str) -> dict[str, Any]:
        """GET /wallets/{id}/balance."""
        return self._c._request(
            "GET", f"/wallets/{quote(wallet_id, safe='')}/balance"
        )


class WebhooksService:
    def __init__(self, client: Clarian) -> None:
        self._c = client

    def list(self) -> list[dict[str, Any]]:
        """GET /webhooks."""
        res = self._c._request("GET", "/webhooks")
        subs = res.get("subscriptions")
        return list(subs) if isinstance(subs, list) else []

    def create(self, params: Mapping[str, Any]) -> dict[str, Any]:
        """POST /webhooks: secret is returned once; store it."""
        res = self._c._request("POST", "/webhooks", body=dict(params))
        sub = res.get("subscription")
        out: dict[str, Any] = dict(sub) if isinstance(sub, Mapping) else {}
        if "secret" in res:
            out["secret"] = res["secret"]
        return out

    def update(self, webhook_id: str, params: Mapping[str, Any]) -> dict[str, Any]:
        """PATCH /webhooks/{id}."""
        res = self._c._request(
            "PATCH",
            f"/webhooks/{quote(webhook_id, safe='')}",
            body=dict(params),
        )
        sub = res.get("subscription")
        return dict(sub) if isinstance(sub, Mapping) else res

    def delete(self, webhook_id: str) -> None:
        """DELETE /webhooks/{id}."""
        self._c._request("DELETE", f"/webhooks/{quote(webhook_id, safe='')}")


class Clarian:
    """Clarian Finance API client.

    Environment is inferred from the API key prefix:
    ``cl_test_sk_`` -> sandbox (``/test``), otherwise live (``/live``).
    """

    def __init__(
        self,
        api_key: str,
        workspace_id: str,
        base_url: Optional[str] = None,
        timeout: Optional[float] = None,
        *,
        transport: Optional[Transport] = None,
    ) -> None:
        if not api_key:
            raise ValueError("api_key is required")
        if not workspace_id:
            raise ValueError("workspace_id is required")

        self.api_key = api_key
        self.workspace_id = workspace_id
        self.base_url = (base_url or _infer_base_url(api_key)).rstrip("/")
        self.timeout = float(timeout) if timeout is not None else DEFAULT_TIMEOUT
        self._transport: Transport = transport or _default_transport

        self.cash_in = CashInService(self)
        self.cash_out = CashOutService(self)
        self.balances = BalancesService(self)
        self.transactions = TransactionsService(self)
        self.wallets = WalletsService(self)
        self.webhooks = WebhooksService(self)
        self.sandbox = SandboxService(self)

    def ping(self) -> dict[str, Any]:
        """GET /ping: credential and workspace probe."""
        return self._request("GET", "/ping")

    def _request(
        self,
        method: str,
        path: str,
        body: Optional[Mapping[str, Any]] = None,
        idempotency_key: Optional[str] = None,
    ) -> dict[str, Any]:
        url = f"{self.base_url}{path}"
        headers: dict[str, str] = {
            "Authorization": f"Bearer {self.api_key}",
            "X-Workspace-Id": self.workspace_id,
        }
        raw_body: Optional[bytes] = None
        if body is not None:
            raw_body = json.dumps(body, separators=(",", ":")).encode("utf-8")
            headers["Content-Type"] = "application/json"
        if idempotency_key:
            headers["Idempotency-Key"] = idempotency_key

        status, resp_body = self._transport(
            method, url, headers, raw_body, self.timeout
        )

        if status < 200 or status >= 300:
            raise ClarianError(status, resp_body)

        if not resp_body:
            return {}

        try:
            parsed = json.loads(resp_body.decode("utf-8"))
        except (json.JSONDecodeError, UnicodeDecodeError) as exc:
            raise ClarianError(status, resp_body) from exc

        if isinstance(parsed, dict):
            return parsed
        return {"data": parsed}
