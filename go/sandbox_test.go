package clarian

import (
	"context"
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

// failRoundTripper fails the test if any HTTP request is attempted.
type failRoundTripper struct{ t *testing.T }

func (f failRoundTripper) RoundTrip(*http.Request) (*http.Response, error) {
	f.t.Fatal("unexpected HTTP call")
	return nil, nil
}

func TestSandbox_requiresTestKey(t *testing.T) {
	t.Parallel()
	c := New("cl_live_sk_x", "ws", WithHTTPClient(&http.Client{
		Transport: failRoundTripper{t},
	}))
	ctx := context.Background()

	if _, err := c.Sandbox.SimulateCashIn(ctx, "ord_1", "completed"); err == nil || !strings.Contains(err.Error(), "sandbox helpers require a cl_test_sk_ key") {
		t.Fatalf("SimulateCashIn: %v", err)
	}
	if _, err := c.Sandbox.SimulateCashOut(ctx, "out_1", "completed"); err == nil || !strings.Contains(err.Error(), "sandbox helpers require a cl_test_sk_ key") {
		t.Fatalf("SimulateCashOut: %v", err)
	}
	if _, err := c.Sandbox.SendWebhookEvent(ctx, "wh1", EventPixPayinCompleted); err == nil || !strings.Contains(err.Error(), "sandbox helpers require a cl_test_sk_ key") {
		t.Fatalf("SendWebhookEvent: %v", err)
	}
	if _, err := c.Sandbox.ResendWebhookDelivery(ctx, "del_1"); err == nil || !strings.Contains(err.Error(), "sandbox helpers require a cl_test_sk_ key") {
		t.Fatalf("ResendWebhookDelivery: %v", err)
	}
}

func TestSandbox_invalidStatusNoHTTP(t *testing.T) {
	t.Parallel()
	c := New("cl_test_sk_x", "ws", WithHTTPClient(&http.Client{
		Transport: failRoundTripper{t},
	}))
	ctx := context.Background()

	if _, err := c.Sandbox.SimulateCashIn(ctx, "ord_1", "bogus"); err == nil || !strings.Contains(err.Error(), "invalid cash-in simulate status") {
		t.Fatalf("SimulateCashIn: %v", err)
	}
	if _, err := c.Sandbox.SimulateCashOut(ctx, "out_1", "expired"); err == nil || !strings.Contains(err.Error(), "invalid cash-out simulate status") {
		t.Fatalf("SimulateCashOut: %v", err)
	}
}

func TestSandbox_SimulateCashIn(t *testing.T) {
	t.Parallel()
	var gotMethod, gotPath, gotAuth, gotBody string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		gotMethod = r.Method
		gotPath = r.URL.Path
		gotAuth = r.Header.Get("Authorization")
		raw, _ := io.ReadAll(r.Body)
		gotBody = string(raw)
		_, _ = w.Write([]byte(`{"ok":true,"environment":"sandbox","order":{"id":"ord_1","status":"completed","amount":19.50}}`))
	}))
	t.Cleanup(srv.Close)

	c := New("cl_test_sk_x", "ws-uuid", WithBaseURL(srv.URL))
	res, err := c.Sandbox.SimulateCashIn(context.Background(), "ord_1", "completed")
	if err != nil {
		t.Fatalf("SimulateCashIn: %v", err)
	}
	if res.Order.ID != "ord_1" || res.Order.Status != TxStatusCompleted {
		t.Fatalf("order = %+v", res.Order)
	}
	if gotMethod != http.MethodPost || gotPath != "/pix/payins/ord_1/simulate" {
		t.Errorf("request = %s %s", gotMethod, gotPath)
	}
	if gotAuth != "Bearer cl_test_sk_x" {
		t.Errorf("Authorization = %q", gotAuth)
	}
	var body map[string]string
	if err := json.Unmarshal([]byte(gotBody), &body); err != nil {
		t.Fatalf("body: %v", err)
	}
	if body["status"] != "completed" {
		t.Errorf("body = %s", gotBody)
	}
}

func TestSandbox_SimulateCashIn_defaultStatus(t *testing.T) {
	t.Parallel()
	var gotBody string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		raw, _ := io.ReadAll(r.Body)
		gotBody = string(raw)
		_, _ = w.Write([]byte(`{"ok":true,"order":{"id":"ord_2","status":"completed","amount":1}}`))
	}))
	t.Cleanup(srv.Close)

	c := New("cl_test_sk_x", "ws", WithBaseURL(srv.URL))
	if _, err := c.Sandbox.SimulateCashIn(context.Background(), "ord_2", ""); err != nil {
		t.Fatalf("SimulateCashIn: %v", err)
	}
	if !strings.Contains(gotBody, `"status":"completed"`) {
		t.Errorf("body = %s", gotBody)
	}
}

func TestSandbox_ResendWebhookDelivery(t *testing.T) {
	t.Parallel()
	var gotMethod, gotPath, gotAuth string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		gotMethod = r.Method
		gotPath = r.URL.Path
		gotAuth = r.Header.Get("Authorization")
		w.WriteHeader(http.StatusAccepted)
		_, _ = w.Write([]byte(`{"ok":true,"delivery_id":"del_new"}`))
	}))
	t.Cleanup(srv.Close)

	c := New("cl_test_sk_x", "ws-uuid", WithBaseURL(srv.URL))
	id, err := c.Sandbox.ResendWebhookDelivery(context.Background(), "del_old")
	if err != nil {
		t.Fatalf("ResendWebhookDelivery: %v", err)
	}
	if id != "del_new" {
		t.Fatalf("delivery_id = %q", id)
	}
	if gotMethod != http.MethodPost || gotPath != "/webhooks/deliveries/del_old/resend" {
		t.Errorf("request = %s %s", gotMethod, gotPath)
	}
	if gotAuth != "Bearer cl_test_sk_x" {
		t.Errorf("Authorization = %q", gotAuth)
	}
}

func TestSandbox_SendWebhookEvent(t *testing.T) {
	t.Parallel()
	var gotPath, gotBody string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		gotPath = r.URL.Path
		raw, _ := io.ReadAll(r.Body)
		gotBody = string(raw)
		w.WriteHeader(http.StatusAccepted)
		_, _ = w.Write([]byte(`{"ok":true,"environment":"sandbox","event_type":"pix_payin.completed","enqueued":{"enqueued":1,"delivery_id":"del_99"}}`))
	}))
	t.Cleanup(srv.Close)

	c := New("cl_test_sk_x", "ws", WithBaseURL(srv.URL))
	res, err := c.Sandbox.SendWebhookEvent(context.Background(), "wh1", EventPixPayinCompleted)
	if err != nil {
		t.Fatalf("SendWebhookEvent: %v", err)
	}
	if gotPath != "/webhooks/wh1/test" {
		t.Errorf("path = %s", gotPath)
	}
	if !strings.Contains(gotBody, `"event_type":"pix_payin.completed"`) {
		t.Errorf("body = %s", gotBody)
	}
	if !res.OK || res.EventType != EventPixPayinCompleted || res.Enqueued != 1 || res.DeliveryID != "del_99" {
		t.Fatalf("result = %+v", res)
	}
}

func TestSandbox_pathEscape(t *testing.T) {
	t.Parallel()
	var gotPath string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		gotPath = r.URL.EscapedPath()
		_, _ = w.Write([]byte(`{"ok":true,"order":{"id":"x","status":"completed"}}`))
	}))
	t.Cleanup(srv.Close)

	c := New("cl_test_sk_x", "ws", WithBaseURL(srv.URL))
	// Slash in id must be escaped in the path segment.
	if _, err := c.Sandbox.SimulateCashIn(context.Background(), "a/b", "completed"); err != nil {
		t.Fatalf("SimulateCashIn: %v", err)
	}
	if gotPath != "/pix/payins/a%2Fb/simulate" {
		t.Errorf("path = %s", gotPath)
	}
}
