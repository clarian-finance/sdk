"""Client env detection, headers, resources (mock transport, no network)."""

from __future__ import annotations

import json
import unittest

from clarian import BASE_URL_LIVE, BASE_URL_TEST, Clarian, ClarianError
from tests.mock_transport import MockTransport, RecordedRequest


def _json_handler(
    status: int = 200,
    payload: dict | list | str | None = None,
    assert_fn=None,
):
    def handler(req: RecordedRequest):
        if assert_fn:
            assert_fn(req)
        if payload is None:
            return status, b""
        return status, payload

    return handler


class TestEnvDetection(unittest.TestCase):
    def test_test_prefix(self):
        c = Clarian("cl_test_sk_abc", "ws")
        self.assertEqual(c.base_url, BASE_URL_TEST)

    def test_live_prefix(self):
        c = Clarian("cl_live_sk_abc", "ws")
        self.assertEqual(c.base_url, BASE_URL_LIVE)

    def test_unknown_defaults_live(self):
        c = Clarian("other_key", "ws")
        self.assertEqual(c.base_url, BASE_URL_LIVE)

    def test_base_url_override_strips_trailing_slash(self):
        c = Clarian("cl_test_sk_x", "ws", base_url="http://localhost:9999/v1/")
        self.assertEqual(c.base_url, "http://localhost:9999/v1")


class TestAuthHeaders(unittest.TestCase):
    def test_workspace_and_auth_on_every_request(self):
        mt = MockTransport(
            _json_handler(
                payload={
                    "ok": True,
                    "environment": "sandbox",
                    "master_account_id": "m1",
                }
            )
        )
        c = Clarian("cl_test_sk_key", "ws-uuid", base_url="https://mock", transport=mt)
        res = c.ping()
        self.assertTrue(res["ok"])
        self.assertEqual(mt.last.method, "GET")
        self.assertEqual(mt.last.path, "/ping")
        self.assertEqual(mt.last.headers["Authorization"], "Bearer cl_test_sk_key")
        self.assertEqual(mt.last.headers["X-Workspace-Id"], "ws-uuid")


class TestIdempotencyHeader(unittest.TestCase):
    def test_cash_in_create_sends_idempotency_key(self):
        mt = MockTransport(
            _json_handler(
                status=201,
                payload={
                    "ok": True,
                    "environment": "sandbox",
                    "order": {
                        "id": "ord_1",
                        "status": "pending",
                        "amount": 19.50,
                        "currency": "BRL",
                        "pix": {"copy_paste": "brcode"},
                    },
                },
            )
        )
        c = Clarian("cl_test_sk_x", "ws-uuid", base_url="https://mock", transport=mt)
        res = c.cash_in.create(
            {
                "amount": 19.50,
                "payer": {"name": "Ana", "document_number": "52998224725"},
            },
            "pay-ext-1",
        )
        self.assertEqual(res["order"]["id"], "ord_1")
        self.assertEqual(mt.last.method, "POST")
        self.assertEqual(mt.last.path, "/cash-in/pix")
        self.assertEqual(mt.last.headers["Idempotency-Key"], "pay-ext-1")
        self.assertEqual(mt.last.headers["X-Workspace-Id"], "ws-uuid")
        body = json.loads(mt.last.body.decode())
        self.assertEqual(body["amount"], 19.50)
        self.assertEqual(body["payer"]["name"], "Ana")

    def test_cash_out_create_sends_idempotency_key(self):
        mt = MockTransport(
            _json_handler(
                status=201,
                payload={"ok": True, "order": {"id": "out_9", "status": "processing"}},
            )
        )
        c = Clarian("cl_test_sk_x", "ws", base_url="https://mock", transport=mt)
        c.cash_out.create(
            {"amount": 10.00, "pix_key": "abc", "pix_key_type": "EVP"},
            "repasse-1",
        )
        self.assertEqual(mt.last.headers["Idempotency-Key"], "repasse-1")
        self.assertEqual(mt.last.path, "/cash-out/pix")

    def test_get_does_not_send_idempotency_key(self):
        mt = MockTransport(
            _json_handler(payload={"ok": True, "order": {"id": "x", "status": "pending"}})
        )
        c = Clarian("cl_test_sk_x", "ws", base_url="https://mock", transport=mt)
        c.cash_in.retrieve("x")
        self.assertNotIn("Idempotency-Key", mt.last.headers)


class TestResourceHappyPaths(unittest.TestCase):
    def test_cash_in_retrieve(self):
        mt = MockTransport(
            _json_handler(
                payload={"ok": True, "order": {"id": "ord_paid", "status": "completed"}}
            )
        )
        c = Clarian("cl_live_sk_x", "ws", base_url="https://mock", transport=mt)
        res = c.cash_in.retrieve("ord_paid")
        self.assertEqual(res["order"]["status"], "completed")
        self.assertEqual(mt.last.path, "/cash-in/ord_paid")

    def test_cash_out_retrieve(self):
        mt = MockTransport(
            _json_handler(
                payload={"ok": True, "order": {"id": "out_1", "status": "completed"}}
            )
        )
        c = Clarian("cl_test_sk_x", "ws", base_url="https://mock", transport=mt)
        res = c.cash_out.retrieve("out_1")
        self.assertEqual(res["order"]["id"], "out_1")
        self.assertEqual(mt.last.path, "/cash-out/out_1")

    def test_cash_out_dict_check(self):
        mt = MockTransport(
            _json_handler(
                payload={
                    "ok": True,
                    "dict": {"name": "Maria", "keyType": "EMAIL"},
                }
            )
        )
        c = Clarian("cl_test_sk_x", "ws", base_url="https://mock", transport=mt)
        res = c.cash_out.dict_check("maria@example.com", "EMAIL")
        self.assertEqual(res["dict"]["name"], "Maria")
        self.assertEqual(mt.last.method, "POST")
        self.assertEqual(mt.last.path, "/pix/payouts/dict")
        body = json.loads(mt.last.body.decode())
        self.assertEqual(body["pix_key"], "maria@example.com")
        self.assertEqual(body["key_type"], "EMAIL")

    def test_balances_list(self):
        mt = MockTransport(
            _json_handler(
                payload={
                    "ok": True,
                    "balances": [
                        {
                            "currency": "BRL",
                            "available": 100.50,
                            "pending": 0,
                            "locked": 0,
                        }
                    ],
                }
            )
        )
        c = Clarian("cl_test_sk_x", "ws", base_url="https://mock", transport=mt)
        bals = c.balances.list()
        self.assertEqual(len(bals), 1)
        self.assertEqual(bals[0]["currency"], "BRL")
        self.assertEqual(mt.last.path, "/account/balances")

    def test_transactions_list_and_retrieve(self):
        def handler(req: RecordedRequest):
            if req.path.startswith("/transactions?") or req.path == "/transactions":
                self.assertIn("type=pix_in", req.path)
                self.assertIn("limit=20", req.path)
                return 200, {
                    "transactions": [
                        {
                            "id": "tx1",
                            "type": "pix_in",
                            "status": "completed",
                            "amount": 10,
                            "fee": 0,
                            "currency": "BRL",
                        }
                    ]
                }
            if req.path == "/transactions/tx-99":
                return 200, {
                    "transaction": {
                        "id": "tx-99",
                        "type": "pix_in",
                        "status": "completed",
                        "amount": 10,
                        "fee": 0.1,
                        "currency": "BRL",
                    }
                }
            self.fail(f"unexpected path {req.path}")

        mt = MockTransport(handler)
        c = Clarian("cl_test_sk_x", "ws", base_url="https://mock", transport=mt)
        lst = c.transactions.list(type="pix_in", limit=20)
        self.assertEqual(lst[0]["id"], "tx1")
        tx = c.transactions.retrieve("tx-99")
        self.assertEqual(tx["id"], "tx-99")

    def test_wallets_list_and_balance(self):
        def handler(req: RecordedRequest):
            if req.path == "/wallets" or req.path.startswith("/wallets?"):
                return 200, {
                    "ok": True,
                    "wallets": [
                        {
                            "id": "w1",
                            "network": "polygon",
                            "address": "0xabc",
                        }
                    ],
                }
            if req.path == "/wallets/w1/balance":
                return 200, {
                    "wallet_id": "w1",
                    "network": "polygon",
                    "address": "0xabc",
                    "balances": [{"currency": "USDT", "amount": "1.5"}],
                }
            self.fail(f"unexpected path {req.path}")

        mt = MockTransport(handler)
        c = Clarian("cl_test_sk_x", "ws", base_url="https://mock", transport=mt)
        wallets = c.wallets.list()
        self.assertEqual(wallets[0]["id"], "w1")
        bal = c.wallets.retrieve_balance("w1")
        self.assertEqual(bal["wallet_id"], "w1")
        self.assertEqual(bal["balances"][0]["currency"], "USDT")

    def test_webhooks_crud(self):
        def handler(req: RecordedRequest):
            if req.method == "POST" and req.path == "/webhooks":
                return 201, {
                    "ok": True,
                    "subscription": {
                        "id": "wh1",
                        "url": "https://ex.com",
                        "events": ["pix_payin.completed"],
                        "is_active": True,
                    },
                    "secret": "whsec_abc",
                }
            if req.method == "GET" and req.path == "/webhooks":
                return 200, {
                    "subscriptions": [
                        {
                            "id": "wh1",
                            "url": "https://ex.com",
                            "events": ["pix_payin.completed"],
                            "is_active": True,
                        }
                    ]
                }
            if req.method == "PATCH" and req.path == "/webhooks/wh1":
                return 200, {
                    "subscription": {
                        "id": "wh1",
                        "url": "https://ex.com/v2",
                        "events": ["pix_payin.completed"],
                        "is_active": False,
                    }
                }
            if req.method == "DELETE" and req.path == "/webhooks/wh1":
                return 200, {"ok": True}
            self.fail(f"unexpected {req.method} {req.path}")

        mt = MockTransport(handler)
        c = Clarian("cl_test_sk_x", "ws", base_url="https://mock", transport=mt)
        created = c.webhooks.create(
            {"url": "https://ex.com", "events": ["pix_payin.completed"]}
        )
        self.assertEqual(created["secret"], "whsec_abc")
        self.assertEqual(created["id"], "wh1")
        subs = c.webhooks.list()
        self.assertEqual(len(subs), 1)
        upd = c.webhooks.update(
            "wh1",
            {
                "url": "https://ex.com/v2",
                "events": ["pix_payin.completed"],
                "is_active": False,
            },
        )
        self.assertFalse(upd["is_active"])
        c.webhooks.delete("wh1")
        self.assertEqual(mt.calls[-1].method, "DELETE")


class TestClarianError(unittest.TestCase):
    def test_json_code_and_meta(self):
        mt = MockTransport(
            _json_handler(
                status=402,
                payload={
                    "error": "insufficient_balance",
                    "detail": "saldo insuficiente",
                    "available": 10,
                    "requested": 100,
                },
            )
        )
        c = Clarian("cl_test_sk_x", "ws", base_url="https://mock", transport=mt)
        with self.assertRaises(ClarianError) as cm:
            c.cash_out.create({"amount": 100}, "idem")
        err = cm.exception
        self.assertEqual(err.status, 402)
        self.assertEqual(err.code, "insufficient_balance")
        self.assertEqual(err.message, "saldo insuficiente")
        self.assertEqual(err.meta.get("available"), 10)
        self.assertIn("insufficient_balance", str(err))

    def test_body_truncation_in_message(self):
        long_body = "x" * 600
        mt = MockTransport(lambda req: (429, long_body))
        c = Clarian("cl_test_sk_x", "ws", base_url="https://mock", transport=mt)
        with self.assertRaises(ClarianError) as cm:
            c.ping()
        err = cm.exception
        self.assertEqual(err.status, 429)
        self.assertEqual(err.code, "")
        self.assertEqual(len(err.message), 500)
        # Exception string must not contain the full 600-char body.
        self.assertLessEqual(len(str(err)), 500 + len("HTTP 429: "))
        self.assertNotIn("x" * 600, str(err))
        self.assertEqual(err.body, long_body)


if __name__ == "__main__":
    unittest.main()
