package clarian

import (
	"context"
	"fmt"
	"net/http"
	"net/url"
	"strings"
)

// Magic PIX keys honored by the sandbox payout rail.
const (
	SandboxFailPixKey    = "fail@sandbox.clarian"
	SandboxPendingPixKey = "pending@sandbox.clarian"
)

// Sample webhook event types for sandbox testing (exact gateway strings).
const (
	EventPixPayinCreated    = "pix_payin.created"
	EventPixPayinCompleted  = "pix_payin.completed"
	EventPixPayinExpired    = "pix_payin.expired"
	EventPixPayoutCreated   = "pix_payout.created"
	EventPixPayoutCompleted = "pix_payout.completed"
	EventPixPayoutFailed    = "pix_payout.failed"
	EventCheckoutPaid       = "checkout.paid"
)

// SandboxService exposes sandbox-only test helpers. Server-side these endpoints
// return 404 {error:"sandbox_only"} outside the test environment; the client
// also refuses non-cl_test_sk_ keys before any HTTP call.
type SandboxService struct{ c *Client }

func (s *SandboxService) requireTestKey() error {
	if !strings.HasPrefix(s.c.apiKey, "cl_test_sk_") {
		return fmt.Errorf("clarian: sandbox helpers require a cl_test_sk_ key")
	}
	return nil
}

// SimulateCashIn advances a sandbox PIX pay-in to status (completed, expired,
// or failed). Empty status defaults to completed.
// POST /pix/payins/{id}/simulate
func (s *SandboxService) SimulateCashIn(ctx context.Context, id, status string) (*CashInOrderResponse, error) {
	if err := s.requireTestKey(); err != nil {
		return nil, err
	}
	if status == "" {
		status = "completed"
	}
	switch status {
	case "completed", "expired", "failed":
	default:
		return nil, fmt.Errorf("clarian: invalid cash-in simulate status %q", status)
	}
	var out CashInOrderResponse
	body := map[string]string{"status": status}
	if err := s.c.do(ctx, http.MethodPost, "/pix/payins/"+url.PathEscape(id)+"/simulate", "", body, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// SimulateCashOut advances a sandbox PIX pay-out to status (completed or failed).
// Empty status defaults to completed.
// POST /pix/payouts/{id}/simulate
func (s *SandboxService) SimulateCashOut(ctx context.Context, id, status string) (*CashOutOrderResponse, error) {
	if err := s.requireTestKey(); err != nil {
		return nil, err
	}
	if status == "" {
		status = "completed"
	}
	switch status {
	case "completed", "failed":
	default:
		return nil, fmt.Errorf("clarian: invalid cash-out simulate status %q", status)
	}
	var out CashOutOrderResponse
	body := map[string]string{"status": status}
	if err := s.c.do(ctx, http.MethodPost, "/pix/payouts/"+url.PathEscape(id)+"/simulate", "", body, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// SandboxWebhookEventResult is returned after enqueueing a sandbox test delivery.
type SandboxWebhookEventResult struct {
	OK          bool
	Environment Environment
	EventType   string
	Enqueued    int
	DeliveryID  string // empty when the gateway returns null
}

// SendWebhookEvent enqueues a sample webhook delivery for a subscription.
// POST /webhooks/{id}/test
func (s *SandboxService) SendWebhookEvent(ctx context.Context, subscriptionID, eventType string) (*SandboxWebhookEventResult, error) {
	if err := s.requireTestKey(); err != nil {
		return nil, err
	}
	var res struct {
		OK          bool        `json:"ok"`
		Environment Environment `json:"environment"`
		EventType   string      `json:"event_type"`
		Enqueued    struct {
			Enqueued   int     `json:"enqueued"`
			DeliveryID *string `json:"delivery_id"`
		} `json:"enqueued"`
	}
	body := map[string]string{"event_type": eventType}
	if err := s.c.do(ctx, http.MethodPost, "/webhooks/"+url.PathEscape(subscriptionID)+"/test", "", body, &res); err != nil {
		return nil, err
	}
	out := &SandboxWebhookEventResult{
		OK:          res.OK,
		Environment: res.Environment,
		EventType:   res.EventType,
		Enqueued:    res.Enqueued.Enqueued,
	}
	if res.Enqueued.DeliveryID != nil {
		out.DeliveryID = *res.Enqueued.DeliveryID
	}
	return out, nil
}

// ResendWebhookDelivery re-enqueues an existing delivery and returns the new
// delivery id. POST /webhooks/deliveries/{id}/resend
func (s *SandboxService) ResendWebhookDelivery(ctx context.Context, deliveryID string) (string, error) {
	if err := s.requireTestKey(); err != nil {
		return "", err
	}
	var res struct {
		OK         bool   `json:"ok"`
		DeliveryID string `json:"delivery_id"`
	}
	if err := s.c.do(ctx, http.MethodPost, "/webhooks/deliveries/"+url.PathEscape(deliveryID)+"/resend", "", nil, &res); err != nil {
		return "", err
	}
	return res.DeliveryID, nil
}
