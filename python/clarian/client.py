"""Clarian API v2 client. Stdlib only (urllib). Requests and replies are plain dicts shaped as in openapi.json."""

from __future__ import annotations

import json
import random
import time
import urllib.error
import urllib.request
from typing import Any, Callable, Mapping, Optional, Sequence, Tuple
from urllib.parse import quote, urlencode

from .errors import ClarianError

DEFAULT_BASE_URL = "https://api.clarian.finance"
DEFAULT_TIMEOUT = 30.0
RETRY_STATUS = frozenset({429, 502, 503, 504})

# (method, url, headers, body, timeout) -> (status, response headers, body bytes). Raise OSError on network failure.
Transport = Callable[[str, str, Mapping[str, str], Optional[bytes], float], Tuple[int, Mapping[str, str], bytes]]
Body = Mapping[str, Any]


def _urllib_transport(method: str, url: str, headers: Mapping[str, str], body: Optional[bytes], timeout: float):
    req = urllib.request.Request(url, data=body, headers=dict(headers), method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.status, dict(resp.headers), resp.read()
    except urllib.error.HTTPError as e:
        return e.code, dict(e.headers or {}), e.read()


def resolve_environment(api_key: str) -> str:
    if api_key.startswith("cl_live_sk_"):
        return "live"
    if api_key.startswith("cl_test_sk_"):
        return "sandbox"
    raise ValueError("Invalid API key format: must start with cl_live_sk_ or cl_test_sk_")


def _no_floats(value: Any) -> Any:
    # Amounts are integer minor units: a float would silently lose precision.
    if isinstance(value, float):
        raise TypeError("floats are not allowed in request bodies: send amounts as int or str minor units")
    if isinstance(value, Mapping):
        for v in value.values():
            _no_floats(v)
    elif isinstance(value, (list, tuple)):
        for v in value:
            _no_floats(v)
    return value


def _seg(value: str) -> str:
    return quote(str(value), safe="")


def _backoff(attempt: int, retry_after: Optional[str]) -> float:
    try:
        seconds = float(retry_after) if retry_after is not None else -1
    except ValueError:
        seconds = -1
    if seconds >= 0:
        return min(seconds, 30.0)
    return random.random() * min(8.0, 0.5 * 2**attempt)


def _require_key(idempotency_key: str) -> str:
    if not idempotency_key:
        raise ValueError("idempotency_key is required: reuse the same key to retry safely")
    return idempotency_key


class _Http:
    def __init__(self, api_key: str, workspace_id: str, base_url: Optional[str], timeout: float,
                 subaccount_id: Optional[str], max_retries: int, transport: Optional[Transport],
                 sleep: Callable[[float], None]) -> None:
        self.api_key = api_key
        self.workspace_id = workspace_id
        self.base_url = (base_url or DEFAULT_BASE_URL).rstrip("/")
        self.timeout = timeout
        self.environment = resolve_environment(api_key)
        self.subaccount_id = subaccount_id
        self.max_retries = max_retries
        self.transport = transport or _urllib_transport
        self.sleep = sleep

    def request(self, method: str, path: str, *, query: Optional[Mapping[str, Any]] = None, body: Any = None,
                idempotency_key: Optional[str] = None, otp_code: Optional[str] = None) -> Any:
        env = "live" if self.environment == "live" else "test"
        pairs = {k: v for k, v in (query or {}).items() if isinstance(v, (str, int)) and not isinstance(v, bool)}
        url = f"{self.base_url}/v1/{env}/{path}" + (f"?{urlencode(pairs)}" if pairs else "")
        headers = {"Authorization": f"Bearer {self.api_key}", "X-Workspace-Id": self.workspace_id,
                   "Accept": "application/json"}
        data = None
        if body is not None:
            headers["Content-Type"] = "application/json"
            data = json.dumps(_no_floats(body)).encode()
        if self.subaccount_id:
            headers["X-Subaccount-Id"] = self.subaccount_id
        if idempotency_key:
            headers["Idempotency-Key"] = idempotency_key
        if otp_code:
            headers["X-OTP-Code"] = otp_code
        # Only a request that cannot double-apply is retried: reads, deletes, or a create with an idempotency key.
        retries = self.max_retries if method in ("GET", "DELETE") or idempotency_key else 0
        attempt = 0
        while True:
            try:
                status, resp_headers, raw = self.transport(method, url, headers, data, self.timeout)
            except OSError:
                if attempt >= retries:
                    raise
                self.sleep(_backoff(attempt, None))
                attempt += 1
                continue
            if status in RETRY_STATUS and attempt < retries:
                retry_after = {k.lower(): v for k, v in resp_headers.items()}.get("retry-after")
                self.sleep(_backoff(attempt, retry_after))
                attempt += 1
                continue
            break
        text = raw.decode("utf-8", "replace") if raw else ""
        try:
            parsed = json.loads(text) if text else {}
        except ValueError:
            parsed = {"error": "invalid_json_response", "message": text[:500]}
        obj = parsed if isinstance(parsed, dict) else {}
        if not 200 <= status < 300:
            raise ClarianError(status, obj)
        return obj["data"] if "data" in obj else obj


class _Resource:
    def __init__(self, http: _Http) -> None:
        self._h = http


class Balances(_Resource):
    def list(self):
        return self._h.request("GET", "balances")


class Wallets(_Resource):
    def list(self):
        return self._h.request("GET", "wallets")

    def create(self, body: Optional[Body] = None):
        return self._h.request("POST", "wallets", body=body or {})

    def retrieve(self, id: str):
        return self._h.request("GET", f"wallets/{_seg(id)}")

    def balance(self, id: str):
        return self._h.request("GET", f"wallets/{_seg(id)}/balance")

    def transactions(self, id: str, *, limit: Optional[int] = None):
        return self._h.request("GET", f"wallets/{_seg(id)}/transactions", query={"limit": limit})


class Transactions(_Resource):
    def list(self, **params: Any):
        """Params: limit, before (id of the last row of the previous page)."""
        return self._h.request("GET", "transactions", query=params)

    def retrieve(self, id: str):
        return self._h.request("GET", f"transactions/{_seg(id)}")

    def statement(self, **params: Any):
        """Params: limit, before, from_, to, currency, kind."""
        if "from_" in params:
            params["from"] = params.pop("from_")
        return self._h.request("GET", "statement", query=params)


class PixPayins(_Resource):
    """PIX cash-in: a charge the payer pays with a QR code / copia-e-cola."""

    def create(self, body: Body, idempotency_key: str):
        key = _require_key(idempotency_key)
        return self._h.request("POST", "pix/payins", body=body, idempotency_key=key)

    def retrieve(self, id: str):
        return self._h.request("GET", f"pix/payins/{_seg(id)}")

    def simulate(self, id: str, body: Optional[Body] = None):
        """Sandbox only: settle or fail a pending charge."""
        return self._h.request("POST", f"pix/payins/{_seg(id)}/simulate", body=body or {"status": "completed"})


class PixPayouts(_Resource):
    """PIX cash-out to the workspace's registered settlement key (403 ``settlement_key_required`` until set)."""

    def create(self, body: Body, idempotency_key: str):
        key = _require_key(idempotency_key)
        return self._h.request("POST", "pix/payouts", body=body, idempotency_key=key)

    def retrieve(self, id: str):
        return self._h.request("GET", f"pix/payouts/{_seg(id)}")

    def fee(self, body: Body):
        return self._h.request("POST", "pix/payouts/fee", body=body)

    def lookup(self, body: Body):
        return self._h.request("POST", "pix/payouts/dict", body=body)

    def simulate(self, id: str, body: Optional[Body] = None):
        """Sandbox only."""
        return self._h.request("POST", f"pix/payouts/{_seg(id)}/simulate", body=body or {"status": "completed"})

    def list_accounts(self):
        return self._h.request("GET", "pix/payout-accounts")


class Pix:
    def __init__(self, http: _Http) -> None:
        self.payins = PixPayins(http)
        self.payouts = PixPayouts(http)


class Conversions(_Resource):
    """Direction is always base_currency -> quote_currency. To buy USDT with BRL: base BRL, quote USDT."""

    def quote(self, body: Body):
        return self._h.request("POST", "rfq/quote", body=body)

    def execute(self, quote_id: str):
        """A quote converts once (409 ``quote_not_open``, 410 ``quote_expired``). Never retried automatically."""
        return self._h.request("POST", "rfq/execute", body={"quote_id": quote_id})

    def prices(self, symbols: Optional[Sequence[str]] = None):
        return self._h.request("GET", "prices", query={"symbols": ",".join(symbols) if symbols else None})


class Crypto(_Resource):
    def send(self, body: Body, idempotency_key: str):
        key = _require_key(idempotency_key)
        return self._h.request("POST", "crypto/send", body=body, idempotency_key=key)

    def simulate(self, id: str, body: Optional[Body] = None):
        """Sandbox only."""
        return self._h.request("POST", f"crypto/send/{_seg(id)}/simulate", body=body or {"status": "completed"})

    def networks(self):
        return self._h.request("GET", "crypto/networks")


class Customers(_Resource):
    def list(self):
        return self._h.request("GET", "customers")

    def create(self, body: Body):
        return self._h.request("POST", "customers", body=body)

    def retrieve(self, id: str):
        return self._h.request("GET", f"customers/{_seg(id)}")

    def update(self, id: str, body: Body):
        return self._h.request("PATCH", f"customers/{_seg(id)}", body=body)


class Kyc(_Resource):
    def list(self):
        return self._h.request("GET", "kyc")

    def create(self, body: Body):
        return self._h.request("POST", "kyc", body=body)

    def retrieve(self, id: str):
        return self._h.request("GET", f"kyc/{_seg(id)}")

    def list_rfi(self, id: str):
        return self._h.request("GET", f"kyc/{_seg(id)}/rfi")

    def answer_rfi(self, id: str, body: Body):
        return self._h.request("POST", f"kyc/{_seg(id)}/rfi", body=body)


class Whitelist(_Resource):
    def list(self):
        return self._h.request("GET", "whitelist")

    def settings(self):
        return self._h.request("GET", "whitelist/settings")


class BankAccounts(_Resource):
    def list(self):
        return self._h.request("GET", "bank-accounts")


class Webhooks(_Resource):
    def list(self):
        return self._h.request("GET", "webhooks")

    def create(self, body: Body):
        """The signing ``secret`` is in this reply only. Store it."""
        return self._h.request("POST", "webhooks", body=body)

    def retrieve(self, id: str):
        return self._h.request("GET", f"webhooks/{_seg(id)}")

    def update(self, id: str, body: Body):
        return self._h.request("PATCH", f"webhooks/{_seg(id)}", body=body)

    def delete(self, id: str):
        return self._h.request("DELETE", f"webhooks/{_seg(id)}")

    def test(self, id: str):
        """Sandbox key only: queues a signed ``test.ping``."""
        return self._h.request("POST", f"webhooks/{_seg(id)}/test")

    def deliveries(self):
        return self._h.request("GET", "webhooks/deliveries")

    def resend(self, delivery_id: str):
        """Sandbox key only."""
        return self._h.request("POST", f"webhooks/deliveries/{_seg(delivery_id)}/resend")


class PaymentLinks(_Resource):
    def list(self):
        return self._h.request("GET", "payment-links")

    def create(self, body: Body):
        return self._h.request("POST", "payment-links", body=body)

    def retrieve(self, id: str):
        return self._h.request("GET", f"payment-links/{_seg(id)}")

    def update(self, id: str, body: Body):
        return self._h.request("PATCH", f"payment-links/{_seg(id)}", body=body)

    def delete(self, id: str):
        return self._h.request("DELETE", f"payment-links/{_seg(id)}")

    def payments(self, **params: Any):
        """Paginated ``{items, next_cursor}``: pass ``next_cursor`` back as ``cursor``.
        Params: limit, cursor, from_, to, link_id, status, payer."""
        if "from_" in params:
            params["from"] = params.pop("from_")
        return self._h.request("GET", "payment-links/payments", query=params)

    def stats(self, **params: Any):
        if "from_" in params:
            params["from"] = params.pop("from_")
        return self._h.request("GET", "payment-links/stats", query=params)

    def splits(self, id: str):
        return self._h.request("GET", f"payment-links/{_seg(id)}/splits")

    def replace_splits(self, id: str, body: Body):
        return self._h.request("PUT", f"payment-links/{_seg(id)}/splits", body=body)

    def charge(self, id: str, body: Body, idempotency_key: str):
        key = _require_key(idempotency_key)
        return self._h.request("POST", f"payment-links/{_seg(id)}/charges", body=body, idempotency_key=key)


class Checkout(_Resource):
    """The public checkout page of a payment link."""

    def retrieve(self, slug: str):
        return self._h.request("GET", f"checkout/{_seg(slug)}")

    def pay(self, slug: str, body: Body, idempotency_key: str):
        key = _require_key(idempotency_key)
        return self._h.request("POST", f"checkout/{_seg(slug)}/pay", body=body, idempotency_key=key)


class SubAccounts(_Resource):
    def list(self):
        return self._h.request("GET", "subaccounts")

    def create(self, body: Body):
        return self._h.request("POST", "subaccounts", body=body)

    def retrieve(self, id: str):
        return self._h.request("GET", f"subaccounts/{_seg(id)}")

    def balances(self, id: str):
        return self._h.request("GET", f"subaccounts/{_seg(id)}/balances")

    def transactions(self, id: str, *, limit: Optional[int] = None):
        return self._h.request("GET", f"subaccounts/{_seg(id)}/transactions", query={"limit": limit})


class ApiKeys(_Resource):
    def list(self):
        return self._h.request("GET", "api-keys")

    def audit(self, *, limit: Optional[int] = None):
        return self._h.request("GET", "api-keys/audit", query={"limit": limit})


class Settings(_Resource):
    def retrieve(self):
        return self._h.request("GET", "settings")


class Notifications(_Resource):
    def list(self):
        return self._h.request("GET", "notifications")


class Team(_Resource):
    def list(self):
        return self._h.request("GET", "team")


class Workflows(_Resource):
    def list(self):
        return self._h.request("GET", "workflows")

    def create(self, body: Body):
        return self._h.request("POST", "workflows", body=body)

    def runs(self):
        return self._h.request("GET", "workflows/runs")


class Approvals(_Resource):
    def list(self):
        return self._h.request("GET", "approvals")

    def policy(self):
        return self._h.request("GET", "approval-policy")


class Treasury(_Resource):
    def list(self):
        return self._h.request("GET", "treasury")

    def create(self, body: Body):
        return self._h.request("POST", "treasury", body=body)


class Clarian:
    """Client for one workspace. The key prefix picks the environment (``cl_test_sk_`` sandbox, ``cl_live_sk_`` live)."""

    def __init__(self, api_key: str, workspace_id: str, *, base_url: Optional[str] = None,
                 timeout: float = DEFAULT_TIMEOUT, subaccount_id: Optional[str] = None, max_retries: int = 2,
                 transport: Optional[Transport] = None, sleep: Callable[[float], None] = time.sleep) -> None:
        if not api_key:
            raise ValueError("api_key is required")
        if not workspace_id:
            raise ValueError("workspace_id is required")
        self._opts = dict(api_key=api_key, workspace_id=workspace_id, base_url=base_url, timeout=timeout,
                          subaccount_id=subaccount_id, max_retries=max_retries, transport=transport, sleep=sleep)
        h = _Http(**self._opts)
        self._http = h
        self.environment = h.environment
        self.balances = Balances(h)
        self.wallets = Wallets(h)
        self.transactions = Transactions(h)
        self.pix = Pix(h)
        self.conversions = Conversions(h)
        self.crypto = Crypto(h)
        self.customers = Customers(h)
        self.kyc = Kyc(h)
        self.whitelist = Whitelist(h)
        self.bank_accounts = BankAccounts(h)
        self.webhooks = Webhooks(h)
        self.payment_links = PaymentLinks(h)
        self.checkout = Checkout(h)
        self.subaccounts = SubAccounts(h)
        self.api_keys = ApiKeys(h)
        self.settings = Settings(h)
        self.notifications = Notifications(h)
        self.team = Team(h)
        self.workflows = Workflows(h)
        self.approvals = Approvals(h)
        self.treasury = Treasury(h)

    def ping(self):
        """Checks the key and workspace."""
        return self._http.request("GET", "ping")

    def for_subaccount(self, subaccount_id: str) -> "Clarian":
        """A client that acts on one child workspace (sends ``X-Subaccount-Id``)."""
        return Clarian(**{**self._opts, "subaccount_id": subaccount_id})
