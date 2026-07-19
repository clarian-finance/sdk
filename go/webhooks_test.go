package clarian

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/hex"
	"net/http"
	"testing"
	"time"
)

func signWebhook(t *testing.T, secret, ts string, body []byte) string {
	t.Helper()
	mac := hmac.New(sha256.New, []byte(secret))
	_, _ = mac.Write([]byte(ts + "." + string(body)))
	return hex.EncodeToString(mac.Sum(nil))
}

func TestVerifyWebhook(t *testing.T) {
	t.Parallel()
	const secret = "whsec_test_secret"
	body := []byte(`{"id":"evt_1","type":"pix_payin.completed","created_at":"2026-01-01T00:00:00Z","environment":"sandbox","data":{"transaction_id":"ord_abc","external_id":"unreliable","status":"completed","amount":19.50,"fee":0.37,"currency":"BRL"}}`)
	ts := time.Now().UTC().Format(time.RFC3339Nano)

	t.Run("accepts valid signature", func(t *testing.T) {
		t.Parallel()
		h := http.Header{}
		h.Set(HeaderTimestamp, ts)
		h.Set(HeaderSignature, signWebhook(t, secret, ts, body))
		evt, err := VerifyWebhook(body, h, secret)
		if err != nil {
			t.Fatalf("VerifyWebhook: %v", err)
		}
		if evt.Type != "pix_payin.completed" {
			t.Errorf("type = %q", evt.Type)
		}
		if evt.Data.TransactionID != "ord_abc" {
			t.Errorf("TransactionID = %q", evt.Data.TransactionID)
		}
		if evt.Data.ExternalID != "unreliable" {
			t.Errorf("ExternalID = %q", evt.Data.ExternalID)
		}
		if evt.Data.Status != "completed" {
			t.Errorf("status = %q", evt.Data.Status)
		}
		if string(evt.Data.Amount) != "19.50" {
			t.Errorf("amount = %q", evt.Data.Amount)
		}
		if string(evt.Data.Fee) != "0.37" {
			t.Errorf("fee = %q", evt.Data.Fee)
		}
		if evt.Data.Currency != "BRL" {
			t.Errorf("currency = %q", evt.Data.Currency)
		}
		if len(evt.RawData) == 0 {
			t.Error("RawData empty")
		}
	})

	t.Run("rejects tampered body", func(t *testing.T) {
		t.Parallel()
		h := http.Header{}
		h.Set(HeaderTimestamp, ts)
		h.Set(HeaderSignature, signWebhook(t, secret, ts, body))
		tampered := []byte(`{"id":"evt_1","type":"pix_payin.completed","data":{"transaction_id":"ord_evil","status":"completed","amount":19.50}}`)
		if _, err := VerifyWebhook(tampered, h, secret); err == nil {
			t.Fatal("expected error for tampered body")
		}
	})

	t.Run("rejects wrong secret", func(t *testing.T) {
		t.Parallel()
		h := http.Header{}
		h.Set(HeaderTimestamp, ts)
		h.Set(HeaderSignature, signWebhook(t, "whsec_wrong", ts, body))
		if _, err := VerifyWebhook(body, h, secret); err == nil {
			t.Fatal("expected error for wrong secret")
		}
	})

	t.Run("rejects stale timestamp", func(t *testing.T) {
		t.Parallel()
		stale := time.Now().UTC().Add(-6 * time.Minute).Format(time.RFC3339Nano)
		h := http.Header{}
		h.Set(HeaderTimestamp, stale)
		h.Set(HeaderSignature, signWebhook(t, secret, stale, body))
		if _, err := VerifyWebhook(body, h, secret); err == nil {
			t.Fatal("expected error for stale timestamp")
		}
	})

	t.Run("rejects missing signature header", func(t *testing.T) {
		t.Parallel()
		h := http.Header{}
		h.Set(HeaderTimestamp, ts)
		if _, err := VerifyWebhook(body, h, secret); err == nil {
			t.Fatal("expected error for missing signature")
		}
	})

	t.Run("rejects missing timestamp header", func(t *testing.T) {
		t.Parallel()
		h := http.Header{}
		h.Set(HeaderSignature, signWebhook(t, secret, ts, body))
		if _, err := VerifyWebhook(body, h, secret); err == nil {
			t.Fatal("expected error for missing timestamp")
		}
	})

	t.Run("rejects empty secret", func(t *testing.T) {
		t.Parallel()
		h := http.Header{}
		h.Set(HeaderTimestamp, ts)
		h.Set(HeaderSignature, signWebhook(t, secret, ts, body))
		if _, err := VerifyWebhook(body, h, ""); err == nil {
			t.Fatal("expected error for empty secret")
		}
	})

	t.Run("rejects future timestamp beyond tolerance", func(t *testing.T) {
		t.Parallel()
		future := time.Now().UTC().Add(6 * time.Minute).Format(time.RFC3339Nano)
		h := http.Header{}
		h.Set(HeaderTimestamp, future)
		h.Set(HeaderSignature, signWebhook(t, secret, future, body))
		if _, err := VerifyWebhook(body, h, secret); err == nil {
			t.Fatal("expected error for future timestamp")
		}
	})
}

func TestSignPayload_roundtrip(t *testing.T) {
	t.Parallel()
	const secret = "whsec_test_secret"
	body := []byte(`{"id":"evt_1","type":"pix_payin.completed","created_at":"2026-01-01T00:00:00Z","environment":"sandbox","data":{"transaction_id":"ord_abc","status":"completed","amount":19.50,"fee":0.37,"currency":"BRL"}}`)
	ts := time.Now().UTC().Format(time.RFC3339Nano)

	t.Run("valid passes VerifyWebhook", func(t *testing.T) {
		t.Parallel()
		h := http.Header{}
		h.Set(HeaderTimestamp, ts)
		h.Set(HeaderSignature, SignPayload(secret, ts, body))
		evt, err := VerifyWebhook(body, h, secret)
		if err != nil {
			t.Fatalf("VerifyWebhook: %v", err)
		}
		if evt.Type != "pix_payin.completed" {
			t.Errorf("type = %q", evt.Type)
		}
	})

	t.Run("tampered payload fails", func(t *testing.T) {
		t.Parallel()
		h := http.Header{}
		h.Set(HeaderTimestamp, ts)
		h.Set(HeaderSignature, SignPayload(secret, ts, body))
		tampered := []byte(`{"id":"evt_1","type":"pix_payin.completed","data":{"transaction_id":"ord_evil","status":"completed","amount":19.50}}`)
		if _, err := VerifyWebhook(tampered, h, secret); err == nil {
			t.Fatal("expected error for tampered body")
		}
	})
}
