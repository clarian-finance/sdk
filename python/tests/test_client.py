import json
import time
from datetime import datetime, timedelta, timezone

import pytest

from clarian import Clarian, ClarianError, construct_webhook_event, sign_webhook_payload, verify_webhook_signature

KEY = "cl_test_sk_abc"
WS = "ws-1"


class Fake:
    def __init__(self, *replies):
        self.replies = list(replies)
        self.calls = []

    def __call__(self, method, url, headers, body, timeout):
        self.calls.append((method, url, dict(headers), json.loads(body) if body else None))
        r = self.replies.pop(0) if len(self.replies) > 1 else self.replies[0]
        if isinstance(r, Exception):
            raise r
        status, payload, *hdrs = r
        return status, (hdrs[0] if hdrs else {}), json.dumps(payload).encode()


def client(fake, **kw):
    return Clarian(KEY, WS, transport=fake, sleep=lambda s: None, **kw)


def test_headers_url_and_unwrap():
    fake = Fake((200, {"data": {"id": "p1"}}))
    out = client(fake).for_subaccount("sub-9").pix.payins.create({"amount_minor": 10**30}, "k-1")
    method, url, headers, body = fake.calls[0]
    assert out == {"id": "p1"}
    assert (method, url) == ("POST", "https://api.clarian.finance/v1/test/pix/payins")
    assert headers["Authorization"] == f"Bearer {KEY}"
    assert headers["X-Workspace-Id"] == WS and headers["X-Subaccount-Id"] == "sub-9"
    assert headers["Idempotency-Key"] == "k-1" and headers["Content-Type"] == "application/json"
    assert body == {"amount_minor": 10**30}


def test_live_env_base_url_query_and_segment():
    fake = Fake((200, {"data": []}))
    c = Clarian("cl_live_sk_x", WS, base_url="http://localhost:3000/", transport=fake)
    c.wallets.transactions("a/b", limit=5)
    assert fake.calls[0][1] == "http://localhost:3000/v1/live/wallets/a%2Fb/transactions?limit=5"
    assert c.environment == "live"


def test_bad_key_prefix():
    with pytest.raises(ValueError):
        Clarian("sk_nope", WS)


def test_error_mapping():
    fake = Fake((402, {"error": "insufficient_balance", "message": "no funds", "needed": "10"}))
    with pytest.raises(ClarianError) as e:
        client(fake).balances.list()
    assert (e.value.status, e.value.code, str(e.value), e.value.meta) == (402, "insufficient_balance", "no funds", {"needed": "10"})


def test_get_retried_with_retry_after():
    slept = []
    fake = Fake((503, {}, {"Retry-After": "2"}), (200, {"data": [1]}))
    assert Clarian(KEY, WS, transport=fake, sleep=slept.append).balances.list() == [1]
    assert len(fake.calls) == 2 and slept == [2.0]


def test_network_error_retried_then_raised():
    fake = Fake(OSError("down"))
    with pytest.raises(OSError):
        client(fake).balances.list()
    assert len(fake.calls) == 3


def test_post_without_key_not_retried():
    fake = Fake((503, {"error": "unavailable"}), (200, {"data": {}}))
    with pytest.raises(ClarianError):
        client(fake).customers.create({"kind": "person"})
    assert len(fake.calls) == 1


def test_execute_not_retried_and_no_key():
    fake = Fake((502, {"error": "bad_gateway"}), (200, {"data": {}}))
    with pytest.raises(ClarianError):
        client(fake).conversions.execute("q1")
    assert len(fake.calls) == 1 and "Idempotency-Key" not in fake.calls[0][2]


def test_post_with_key_retried():
    fake = Fake((429, {}), (200, {"data": {"id": "x"}}))
    assert client(fake).crypto.send({"amount_minor": "1"}, "k")["id"] == "x"
    assert len(fake.calls) == 2


@pytest.mark.parametrize("call", [
    lambda c: c.pix.payins.create({}, ""),
    lambda c: c.pix.payouts.create({}, ""),
    lambda c: c.crypto.send({}, ""),
    lambda c: c.payment_links.charge("l", {}, ""),
    lambda c: c.checkout.pay("s", {}, ""),
])
def test_idempotency_key_required(call):
    fake = Fake((200, {"data": {}}))
    with pytest.raises(ValueError, match="idempotency_key is required"):
        call(client(fake))
    assert fake.calls == []


def test_float_amount_rejected():
    fake = Fake((200, {"data": {}}))
    with pytest.raises(TypeError):
        client(fake).pix.payouts.fee({"amount_minor": 10.5})
    assert fake.calls == []


SECRET = "whsec_test"
BODY = json.dumps({"id": "evt_1", "type": "test.ping", "created_at": "2026-10-11T00:00:00Z", "data": {}})


def headers_for(ts, sig=None):
    return {"X-Clarian-Signature": sig or sign_webhook_payload(BODY, ts, SECRET), "X-Clarian-Timestamp": ts}


def iso(delta=0):
    return (datetime.now(timezone.utc) + timedelta(seconds=delta)).isoformat().replace("+00:00", "Z")


def test_webhook_valid():
    assert construct_webhook_event(BODY.encode(), headers_for(iso()), SECRET)["id"] == "evt_1"


def test_webhook_bad_signature():
    with pytest.raises(ValueError):
        construct_webhook_event(BODY, headers_for(iso(), "0" * 64), SECRET)


def test_webhook_stale():
    ts = iso(-301)
    assert not verify_webhook_signature(BODY, sign_webhook_payload(BODY, ts, SECRET), ts, SECRET)


def test_webhook_unparseable_timestamp():
    ts = "not-a-date"
    assert not verify_webhook_signature(BODY, sign_webhook_payload(BODY, ts, SECRET), ts, SECRET)


def test_seg_encodes_dot_segments_and_refuses_empty():
    from clarian.client import _seg

    assert _seg("../api-keys") == "%2E%2E%2Fapi-keys"
    with pytest.raises(ValueError):
        _seg("")


def test_empty_webhook_secret_raises():
    ts = datetime.now(timezone.utc).isoformat()
    with pytest.raises(ValueError, match="secret"):
        verify_webhook_signature("{}", sign_webhook_payload("{}", ts, ""), ts, "")
    with pytest.raises(ValueError, match="secret"):
        construct_webhook_event("{}", {"x-clarian-signature": "x", "x-clarian-timestamp": ts}, "")


@pytest.mark.parametrize("bad", ["", ".", ".."])
def test_dot_and_empty_ids_refused(bad):
    fake = Fake((200, {}))
    with pytest.raises(ValueError):
        client(fake).wallets.retrieve(bad)
    assert fake.calls == []


def test_blank_subaccount_refused():
    with pytest.raises(ValueError):
        client(Fake((200, {}))).for_subaccount("  ")
    with pytest.raises(ValueError):
        client(Fake((200, {})), subaccount_id="")


def test_base_url_must_be_https():
    for bad in ("http://api.example.com", "http://localhost.evil.com", "ftp://x"):
        with pytest.raises(ValueError, match="https"):
            client(Fake((200, {})), base_url=bad)
    for ok in ("https://api.example.com", "http://localhost:3000", "http://127.0.0.1", "http://host.docker.internal:8080"):
        client(Fake((200, {})), base_url=ok)


def test_repr_masks_key():
    c = client(Fake((200, {})))
    assert KEY not in repr(c) and KEY not in repr(c._http)


def test_redirects_are_not_followed():
    import threading
    from http.server import BaseHTTPRequestHandler, HTTPServer

    from clarian.client import _urllib_transport

    seen = []

    class H(BaseHTTPRequestHandler):
        def do_GET(self):
            seen.append((self.path, self.headers.get("Authorization")))
            self.send_response(302)
            self.send_header("Location", "/elsewhere")
            self.end_headers()

        def log_message(self, *a):
            pass

    srv = HTTPServer(("127.0.0.1", 0), H)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    try:
        status, _, _ = _urllib_transport("GET", f"http://127.0.0.1:{srv.server_port}/x", {"Authorization": "Bearer k"}, None, 5)
    finally:
        srv.shutdown()
    assert status == 302
    assert seen == [("/x", "Bearer k")]
