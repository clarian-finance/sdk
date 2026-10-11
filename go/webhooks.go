package clarian

import (
	"bytes"
	"crypto/hmac"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"net/http"
	"time"
)

// DefaultWebhookTolerance is the maximum age of a delivery.
const DefaultWebhookTolerance = 5 * time.Minute

// ErrInvalidWebhook is returned when the signature or timestamp is bad.
var ErrInvalidWebhook = errors.New("invalid webhook signature or timestamp outside tolerance")

// ErrWebhookSecretRequired is returned for an empty secret: its HMAC would be forgeable by anyone.
var ErrWebhookSecretRequired = errors.New("webhook secret is required")

// WebhookEvent is the body of a delivery. Data is a Row with payment_id, kind, status, amount_minor, currency, ...
type WebhookEvent struct {
	ID        string `json:"id"`
	Type      string `json:"type"`
	CreatedAt string `json:"created_at"`
	Data      Row    `json:"data"`
}

// SignWebhookPayload signs "<timestamp>.<body>" exactly as Clarian does. For tests of your own handler.
func SignWebhookPayload(body []byte, timestamp, secret string) string {
	m := hmac.New(sha256.New, []byte(secret))
	m.Write([]byte(timestamp + "."))
	m.Write(body)
	return hex.EncodeToString(m.Sum(nil))
}

// VerifyWebhookSignature checks the HMAC and the timestamp age. An unparseable timestamp or an empty secret fails closed.
func VerifyWebhookSignature(body []byte, signature, timestamp, secret string, tolerance time.Duration) bool {
	if secret == "" {
		return false
	}
	sentAt, err := time.Parse(time.RFC3339Nano, timestamp)
	if err != nil {
		return false
	}
	if age := time.Since(sentAt); age > tolerance || age < -tolerance {
		return false
	}
	return hmac.Equal([]byte(SignWebhookPayload(body, timestamp, secret)), []byte(signature))
}

// ConstructWebhookEvent verifies a delivery and returns its parsed body. rawBody must be the exact
// bytes received. Dedupe on event.ID: a delivery can arrive more than once.
func ConstructWebhookEvent(rawBody []byte, h http.Header, secret string) (*WebhookEvent, error) {
	if secret == "" {
		return nil, ErrWebhookSecretRequired
	}
	if !VerifyWebhookSignature(rawBody, h.Get("X-Clarian-Signature"), h.Get("X-Clarian-Timestamp"), secret, DefaultWebhookTolerance) {
		return nil, ErrInvalidWebhook
	}
	var ev WebhookEvent
	if err := decode(bytes.TrimSpace(rawBody), &ev); err != nil {
		return nil, err
	}
	return &ev, nil
}
