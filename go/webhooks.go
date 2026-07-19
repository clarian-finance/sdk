package clarian

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"net/http"
	"time"
)

// Webhook replay window (matches server dispatch).
const webhookTolerance = 5 * time.Minute

// Header names on every Clarian webhook delivery.
const (
	HeaderSignature      = "X-Clarian-Signature"
	HeaderTimestamp      = "X-Clarian-Timestamp"
	HeaderEvent          = "X-Clarian-Event"
	HeaderDeliveryID     = "X-Clarian-Delivery-Id"
	HeaderIdempotencyKey = "X-Clarian-Idempotency-Key"
	HeaderAttempt        = "X-Clarian-Attempt"
)

// SignPayload returns the hex HMAC-SHA256 of timestamp+"."+payload using secret.
// Use with VerifyWebhook in local handler tests (inverse of the verify step).
func SignPayload(secret, timestamp string, payload []byte) string {
	mac := hmac.New(sha256.New, []byte(secret))
	_, _ = mac.Write([]byte(timestamp + "." + string(payload)))
	return hex.EncodeToString(mac.Sum(nil))
}

// VerifyWebhook checks HMAC-SHA256 signature and timestamp freshness, then
// parses the envelope. Signature = hex(HMAC-SHA256(secret, timestamp+"."+rawBody))
// where secret is the full whsec_… string (prefix included). Fail-closed on
// missing secret, signature, or timestamp.
func VerifyWebhook(payload []byte, headers http.Header, secret string) (*Event, error) {
	sig := headers.Get(HeaderSignature)
	tsRaw := headers.Get(HeaderTimestamp)
	if secret == "" || sig == "" || tsRaw == "" {
		return nil, fmt.Errorf("clarian: missing webhook signature, timestamp, or secret")
	}

	ts, err := time.Parse(time.RFC3339Nano, tsRaw)
	if err != nil {
		return nil, fmt.Errorf("clarian: invalid webhook timestamp: %w", err)
	}
	diff := time.Since(ts)
	if diff < 0 {
		diff = -diff
	}
	if diff > webhookTolerance {
		return nil, fmt.Errorf("clarian: webhook timestamp outside tolerance")
	}

	mac := hmac.New(sha256.New, []byte(secret))
	_, _ = mac.Write([]byte(tsRaw + "." + string(payload)))
	expected := hex.EncodeToString(mac.Sum(nil))
	if !hmac.Equal([]byte(expected), []byte(sig)) {
		return nil, fmt.Errorf("clarian: invalid webhook signature")
	}

	var envelope struct {
		ID          string          `json:"id"`
		Type        string          `json:"type"`
		CreatedAt   string          `json:"created_at"`
		Environment string          `json:"environment"`
		Data        json.RawMessage `json:"data"`
	}
	if err := json.Unmarshal(payload, &envelope); err != nil {
		return nil, fmt.Errorf("clarian: decode webhook: %w", err)
	}

	evt := &Event{
		ID:          envelope.ID,
		Type:        envelope.Type,
		CreatedAt:   envelope.CreatedAt,
		Environment: envelope.Environment,
		RawData:     envelope.Data,
	}
	if len(envelope.Data) > 0 {
		if err := json.Unmarshal(envelope.Data, &evt.Data); err != nil {
			return nil, fmt.Errorf("clarian: decode webhook data: %w", err)
		}
	}
	return evt, nil
}
