"""Webhook sign/verify tests (stdlib only)."""

from __future__ import annotations

import time
import unittest
from datetime import datetime, timedelta, timezone

from clarian import extract_headers, sign_payload, verify_signature
from clarian.webhooks import HEADER_SIGNATURE, HEADER_TIMESTAMP


class TestWebhooks(unittest.TestCase):
    def setUp(self):
        self.secret = "whsec_test_secret"
        self.body = (
            b'{"id":"evt_1","type":"pix_payin.completed",'
            b'"created_at":"2026-01-01T00:00:00Z","environment":"sandbox",'
            b'"data":{"transaction_id":"ord_abc","status":"completed",'
            b'"amount":19.50,"fee":0.37,"currency":"BRL"}}'
        )
        self.ts = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ")

    def test_sign_verify_roundtrip(self):
        sig = sign_payload(self.secret, self.ts, self.body)
        self.assertTrue(
            verify_signature(self.body, self.ts, sig, self.secret)
        )
        # str payload also works
        sig2 = sign_payload(self.secret, self.ts, self.body.decode())
        self.assertTrue(
            verify_signature(self.body.decode(), self.ts, sig2, self.secret)
        )

    def test_tampered_signature_rejected(self):
        sig = sign_payload(self.secret, self.ts, self.body)
        tampered = self.body.replace(b"ord_abc", b"ord_evil")
        self.assertFalse(
            verify_signature(tampered, self.ts, sig, self.secret)
        )

    def test_wrong_secret_rejected(self):
        sig = sign_payload("whsec_wrong", self.ts, self.body)
        self.assertFalse(
            verify_signature(self.body, self.ts, sig, self.secret)
        )

    def test_stale_timestamp_rejected(self):
        stale = (datetime.now(timezone.utc) - timedelta(minutes=6)).strftime(
            "%Y-%m-%dT%H:%M:%S.%fZ"
        )
        sig = sign_payload(self.secret, stale, self.body)
        self.assertFalse(
            verify_signature(self.body, stale, sig, self.secret)
        )

    def test_future_timestamp_beyond_tolerance_rejected(self):
        future = (datetime.now(timezone.utc) + timedelta(minutes=6)).strftime(
            "%Y-%m-%dT%H:%M:%S.%fZ"
        )
        sig = sign_payload(self.secret, future, self.body)
        self.assertFalse(
            verify_signature(self.body, future, sig, self.secret)
        )

    def test_custom_tolerance(self):
        old = (datetime.now(timezone.utc) - timedelta(seconds=10)).strftime(
            "%Y-%m-%dT%H:%M:%S.%fZ"
        )
        sig = sign_payload(self.secret, old, self.body)
        self.assertFalse(
            verify_signature(
                self.body, old, sig, self.secret, tolerance_seconds=5
            )
        )
        self.assertTrue(
            verify_signature(
                self.body, old, sig, self.secret, tolerance_seconds=60
            )
        )

    def test_missing_inputs_rejected(self):
        sig = sign_payload(self.secret, self.ts, self.body)
        self.assertFalse(verify_signature(self.body, self.ts, sig, ""))
        self.assertFalse(verify_signature(self.body, "", sig, self.secret))
        self.assertFalse(verify_signature(self.body, self.ts, "", self.secret))

    def test_extract_headers(self):
        headers = {
            HEADER_TIMESTAMP: self.ts,
            HEADER_SIGNATURE: "abc123",
            "X-Clarian-Event": "pix_payin.completed",
        }
        ts, sig = extract_headers(headers)
        self.assertEqual(ts, self.ts)
        self.assertEqual(sig, "abc123")

    def test_extract_headers_case_insensitive(self):
        headers = {
            "x-clarian-timestamp": "t1",
            "x-clarian-signature": "s1",
        }
        ts, sig = extract_headers(headers)
        self.assertEqual((ts, sig), ("t1", "s1"))

    def test_extract_headers_list_values(self):
        headers = {
            "X-Clarian-Timestamp": ["t2"],
            "X-Clarian-Signature": ["s2"],
        }
        ts, sig = extract_headers(headers)
        self.assertEqual((ts, sig), ("t2", "s2"))

    def test_unix_timestamp_accepted(self):
        now = str(int(time.time()))
        sig = sign_payload(self.secret, now, self.body)
        self.assertTrue(verify_signature(self.body, now, sig, self.secret))


if __name__ == "__main__":
    unittest.main()
