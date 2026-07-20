"""Sandbox guard and happy-path tests (no network)."""

from __future__ import annotations

import json
import unittest

from clarian import (
    EVENT_PIX_PAYIN_COMPLETED,
    SANDBOX_FAIL_PIX_KEY,
    SANDBOX_PENDING_PIX_KEY,
    Clarian,
)
from tests.mock_transport import MockTransport, RecordedRequest


class FailTransport:
    """Fails the test if any HTTP call is attempted."""

    def __call__(self, *args, **kwargs):
        raise AssertionError("unexpected HTTP call")


class TestSandboxGuard(unittest.TestCase):
    def test_live_key_rejected_before_network(self):
        c = Clarian(
            "cl_live_sk_x",
            "ws",
            base_url="https://mock",
            transport=FailTransport(),
        )
        with self.assertRaises(ValueError) as cm:
            c.sandbox.simulate_cash_in("ord_1", "completed")
        self.assertIn("cl_test_sk_", str(cm.exception))

        with self.assertRaises(ValueError):
            c.sandbox.simulate_cash_out("out_1", "completed")
        with self.assertRaises(ValueError):
            c.sandbox.send_webhook_event("wh1", EVENT_PIX_PAYIN_COMPLETED)
        with self.assertRaises(ValueError):
            c.sandbox.resend_webhook_delivery("del_1")

    def test_invalid_status_no_http(self):
        c = Clarian(
            "cl_test_sk_x",
            "ws",
            base_url="https://mock",
            transport=FailTransport(),
        )
        with self.assertRaises(ValueError) as cm:
            c.sandbox.simulate_cash_in("ord_1", "bogus")
        self.assertIn("invalid cash-in", str(cm.exception))

        with self.assertRaises(ValueError) as cm:
            c.sandbox.simulate_cash_out("out_1", "expired")
        self.assertIn("invalid cash-out", str(cm.exception))


class TestSandboxHappyPaths(unittest.TestCase):
    def test_simulate_cash_in(self):
        mt = MockTransport(
            lambda req: (
                200,
                {
                    "ok": True,
                    "environment": "sandbox",
                    "order": {
                        "id": "ord_1",
                        "status": "completed",
                        "amount": 19.50,
                    },
                },
            )
        )
        c = Clarian("cl_test_sk_x", "ws-uuid", base_url="https://mock", transport=mt)
        res = c.sandbox.simulate_cash_in("ord_1", "completed")
        self.assertEqual(res["order"]["id"], "ord_1")
        self.assertEqual(mt.last.method, "POST")
        self.assertEqual(mt.last.path, "/pix/payins/ord_1/simulate")
        self.assertEqual(mt.last.headers["Authorization"], "Bearer cl_test_sk_x")
        body = json.loads(mt.last.body.decode())
        self.assertEqual(body["status"], "completed")

    def test_simulate_cash_in_default_status(self):
        mt = MockTransport(
            lambda req: (200, {"ok": True, "order": {"id": "ord_2", "status": "completed"}})
        )
        c = Clarian("cl_test_sk_x", "ws", base_url="https://mock", transport=mt)
        c.sandbox.simulate_cash_in("ord_2")
        body = json.loads(mt.last.body.decode())
        self.assertEqual(body["status"], "completed")

    def test_simulate_cash_out(self):
        mt = MockTransport(
            lambda req: (200, {"ok": True, "order": {"id": "out_1", "status": "failed"}})
        )
        c = Clarian("cl_test_sk_x", "ws", base_url="https://mock", transport=mt)
        res = c.sandbox.simulate_cash_out("out_1", "failed")
        self.assertEqual(res["order"]["status"], "failed")
        self.assertEqual(mt.last.path, "/pix/payouts/out_1/simulate")

    def test_send_webhook_event(self):
        def handler(req: RecordedRequest):
            self.assertEqual(req.path, "/webhooks/wh1/test")
            return 202, {
                "ok": True,
                "environment": "sandbox",
                "event_type": "pix_payin.completed",
                "enqueued": {"enqueued": 1, "delivery_id": "del_99"},
            }

        mt = MockTransport(handler)
        c = Clarian("cl_test_sk_x", "ws", base_url="https://mock", transport=mt)
        res = c.sandbox.send_webhook_event("wh1", EVENT_PIX_PAYIN_COMPLETED)
        self.assertEqual(res["event_type"], EVENT_PIX_PAYIN_COMPLETED)
        self.assertEqual(res["enqueued"], 1)
        self.assertEqual(res["delivery_id"], "del_99")
        body = json.loads(mt.last.body.decode())
        self.assertEqual(body["event_type"], "pix_payin.completed")

    def test_resend_webhook_delivery(self):
        mt = MockTransport(
            lambda req: (202, {"ok": True, "delivery_id": "del_new"})
        )
        c = Clarian("cl_test_sk_x", "ws", base_url="https://mock", transport=mt)
        new_id = c.sandbox.resend_webhook_delivery("del_old")
        self.assertEqual(new_id, "del_new")
        self.assertEqual(mt.last.path, "/webhooks/deliveries/del_old/resend")
        self.assertEqual(mt.last.method, "POST")

    def test_magic_pix_key_constants(self):
        self.assertEqual(SANDBOX_FAIL_PIX_KEY, "fail@sandbox.clarian")
        self.assertEqual(SANDBOX_PENDING_PIX_KEY, "pending@sandbox.clarian")

    def test_path_escape(self):
        mt = MockTransport(
            lambda req: (200, {"ok": True, "order": {"id": "x", "status": "completed"}})
        )
        c = Clarian("cl_test_sk_x", "ws", base_url="https://mock", transport=mt)
        c.sandbox.simulate_cash_in("a/b", "completed")
        self.assertEqual(mt.last.path, "/pix/payins/a%2Fb/simulate")


if __name__ == "__main__":
    unittest.main()
