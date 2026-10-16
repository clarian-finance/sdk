package clarian

import (
	"context"
	"errors"
	"fmt"
	"io"
	"math/big"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync/atomic"
	"testing"
	"time"
)

const testKey = "cl_test_sk_abc"

func server(t *testing.T, h http.HandlerFunc) (*Client, *int32) {
	var hits int32
	s := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		atomic.AddInt32(&hits, 1)
		h(w, r)
	}))
	t.Cleanup(s.Close)
	c, err := New(testKey, "ws_1", WithBaseURL(s.URL+"/"))
	if err != nil {
		t.Fatal(err)
	}
	return c, &hits
}

func TestEnvironmentFromKey(t *testing.T) {
	if _, err := New("bad", "ws"); err == nil {
		t.Fatal("bad key accepted")
	}
	if _, err := New(testKey, ""); err == nil {
		t.Fatal("empty workspace accepted")
	}
	c, _ := New("cl_live_sk_x", "ws")
	if c.Environment() != "live" || c.env != "live" {
		t.Fatal("live env")
	}
}

func TestRequestHeadersAndUnwrap(t *testing.T) {
	var got *http.Request
	var body string
	c, _ := server(t, func(w http.ResponseWriter, r *http.Request) {
		got = r
		b, _ := io.ReadAll(r.Body)
		body = string(b)
		w.Write([]byte(`{"data":{"id":"p_1","amount_minor":"10000"}}`))
	})
	amt, _ := new(big.Int).SetString("123456789012345678901234567890", 10)
	p, err := c.ForSubaccount("sub_9").Pix.Payins.Create(context.Background(), Row{"amount_minor": amt, "currency": "BRL"}, "idem-1")
	if err != nil {
		t.Fatal(err)
	}
	if p["id"] != "p_1" {
		t.Fatalf("unwrap: %v", p)
	}
	if got.URL.Path != "/v1/test/pix/payins" || got.Method != "POST" {
		t.Fatalf("url %s %s", got.Method, got.URL.Path)
	}
	for k, v := range map[string]string{"Authorization": "Bearer " + testKey, "X-Workspace-Id": "ws_1",
		"X-Subaccount-Id": "sub_9", "Idempotency-Key": "idem-1", "Content-Type": "application/json"} {
		if got.Header.Get(k) != v {
			t.Errorf("header %s = %q", k, got.Header.Get(k))
		}
	}
	if body != `{"amount_minor":"123456789012345678901234567890","currency":"BRL"}` {
		t.Fatalf("body %s", body)
	}
}

func TestIntAmountAsStringAndFloatRefused(t *testing.T) {
	var body string
	c, hits := server(t, func(w http.ResponseWriter, r *http.Request) {
		b, _ := io.ReadAll(r.Body)
		body = string(b)
		w.Write([]byte(`{"data":{}}`))
	})
	if _, err := c.Pix.Payouts.Fee(context.Background(), Row{"amount_minor": 1000}); err != nil || body != `{"amount_minor":"1000"}` {
		t.Fatalf("int amount: %v %s", err, body)
	}
	if _, err := c.Pix.Payouts.Fee(context.Background(), Row{"amount_minor": 10.5}); err == nil || *hits != 1 {
		t.Fatal("float accepted")
	}
}

func TestQueryAndNoKeyHeaders(t *testing.T) {
	var got *http.Request
	c, _ := server(t, func(w http.ResponseWriter, r *http.Request) { got = r; w.Write([]byte(`{"data":[]}`)) })
	if _, err := c.Transactions.List(context.Background(), Params{"limit": "5", "before": ""}); err != nil {
		t.Fatal(err)
	}
	if got.URL.RawQuery != "limit=5" || got.Header.Get("Idempotency-Key") != "" || got.Header.Get("X-Subaccount-Id") != "" {
		t.Fatalf("query %q headers %v", got.URL.RawQuery, got.Header)
	}
}

func TestErrorMapping(t *testing.T) {
	c, _ := server(t, func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(422)
		w.Write([]byte(`{"error":"insufficient_balance","message":"not enough","available":"5"}`))
	})
	_, err := c.Balances.List(context.Background())
	var e *Error
	if !errors.As(err, &e) || e.Status != 422 || e.Code != "insufficient_balance" || e.Message != "not enough" || e.Meta["available"] != "5" {
		t.Fatalf("got %#v", err)
	}
}

func TestRetryRules(t *testing.T) {
	ctx := context.Background()
	fail := func(w http.ResponseWriter, r *http.Request) { w.Header().Set("Retry-After", "0"); w.WriteHeader(503) }

	c, hits := server(t, fail)
	if _, err := c.Balances.List(ctx); err == nil || *hits != 3 {
		t.Fatalf("GET retried: hits=%d", *hits)
	}
	c, hits = server(t, fail)
	c.Customers.Create(ctx, Row{"kind": "person"})
	if *hits != 1 {
		t.Fatalf("POST without key retried: %d", *hits)
	}
	c, hits = server(t, fail)
	c.Conversions.Execute(ctx, "q_1")
	if *hits != 1 {
		t.Fatalf("execute retried: %d", *hits)
	}
	c, hits = server(t, fail)
	c.Pix.Payouts.Create(ctx, Row{"amount_minor": "1"}, "k")
	if *hits != 3 {
		t.Fatalf("keyed POST not retried: %d", *hits)
	}
	var n int32
	c, _ = server(t, func(w http.ResponseWriter, r *http.Request) {
		if atomic.AddInt32(&n, 1) == 1 {
			fail(w, r)
			return
		}
		w.Write([]byte(`{"data":[{"currency":"BRL"}]}`))
	})
	if rows, err := c.Balances.List(ctx); err != nil || len(rows) != 1 {
		t.Fatalf("recovered: %v %v", rows, err)
	}
}

func TestIdempotencyKeyRequired(t *testing.T) {
	c, hits := server(t, func(w http.ResponseWriter, r *http.Request) {})
	ctx := context.Background()
	calls := []func() error{
		func() error { _, e := c.Pix.Payins.Create(ctx, Row{}, ""); return e },
		func() error { _, e := c.Pix.Payouts.Create(ctx, Row{}, ""); return e },
		func() error { _, e := c.Crypto.Send(ctx, Row{}, ""); return e },
		func() error { _, e := c.Trading.Deposit(ctx, Row{"amount_minor": "100"}, ""); return e },
		func() error { _, e := c.Trading.Withdraw(ctx, Row{}, ""); return e },
		func() error { _, e := c.Trading.CryptoWithdraw(ctx, Row{}, ""); return e },
		func() error { _, e := c.PaymentLinks.Charge(ctx, "l", Row{}, ""); return e },
		func() error { _, e := c.Checkout.Pay(ctx, "s", Row{}, ""); return e },
	}
	for i, f := range calls {
		if !errors.Is(f(), ErrIdempotencyKeyRequired) {
			t.Errorf("call %d accepted empty key", i)
		}
	}
	if *hits != 0 {
		t.Fatal("network call made")
	}
}

func TestWebhookVerify(t *testing.T) {
	body := []byte(`{"id":"ev_1","type":"payment.completed","created_at":"x","data":{"payment_id":"p_1","amount_minor":"100"}}`)
	ts := time.Now().UTC().Format(time.RFC3339Nano)
	h := http.Header{}
	h.Set("X-Clarian-Timestamp", ts)
	h.Set("X-Clarian-Signature", SignWebhookPayload(body, ts, "sec"))
	ev, err := ConstructWebhookEvent(body, h, "sec")
	if err != nil || ev.ID != "ev_1" || ev.Type != "payment.completed" || ev.Data["payment_id"] != "p_1" {
		t.Fatalf("valid: %v %+v", err, ev)
	}
	if _, err := ConstructWebhookEvent(body, h, "other"); !errors.Is(err, ErrInvalidWebhook) {
		t.Fatal("bad signature accepted")
	}
	old := time.Now().Add(-301 * time.Second).UTC().Format(time.RFC3339)
	if VerifyWebhookSignature(body, SignWebhookPayload(body, old, "sec"), old, "sec", DefaultWebhookTolerance) {
		t.Fatal("stale accepted")
	}
	if VerifyWebhookSignature(body, SignWebhookPayload(body, "garbage", "sec"), "garbage", "sec", DefaultWebhookTolerance) {
		t.Fatal("unparseable timestamp accepted")
	}
}

func TestSegEncodesDotSegments(t *testing.T) {
	if got := seg(".."); got != "%2E%2E" {
		t.Fatalf("seg(..) = %q", got)
	}
	if got := seg("../api-keys"); got != "%2E%2E%2Fapi-keys" {
		t.Fatalf("seg(../api-keys) = %q", got)
	}
}

func TestFloatNestedInRowSliceRefused(t *testing.T) {
	if _, err := normalize("", Row{"splits": []Row{{"amount_minor": 12.5}}}); err == nil {
		t.Fatal("float inside []Row was accepted")
	}
}

func TestWebhookEmptySecretRefused(t *testing.T) {
	body := []byte(`{}`)
	ts := time.Now().UTC().Format(time.RFC3339Nano)
	h := http.Header{}
	h.Set("X-Clarian-Timestamp", ts)
	h.Set("X-Clarian-Signature", SignWebhookPayload(body, ts, ""))
	if _, err := ConstructWebhookEvent(body, h, ""); !errors.Is(err, ErrWebhookSecretRequired) {
		t.Fatalf("empty secret: %v", err)
	}
	if VerifyWebhookSignature(body, SignWebhookPayload(body, ts, ""), ts, "", DefaultWebhookTolerance) {
		t.Fatal("empty secret verified")
	}
}

func TestDotAndEmptyIDsRefused(t *testing.T) {
	c, _ := New("cl_test_sk_x", "ws", WithBaseURL("http://127.0.0.1:1"))
	for _, id := range []string{"", ".", ".."} {
		if _, err := c.Wallets.Retrieve(context.Background(), id); !errors.Is(err, ErrInvalidID) {
			t.Fatalf("id %q: %v", id, err)
		}
	}
}

func TestBlankSubaccountRefused(t *testing.T) {
	if _, err := New("cl_test_sk_x", "ws", WithSubaccount(" ")); err == nil {
		t.Fatal("blank WithSubaccount accepted")
	}
	c, _ := New("cl_test_sk_x", "ws")
	defer func() {
		if recover() == nil {
			t.Fatal("blank ForSubaccount did not panic")
		}
	}()
	c.ForSubaccount("")
}

func TestBaseURLMustBeHTTPS(t *testing.T) {
	for _, u := range []string{"http://api.example.com", "http://localhost.evil.com", "ftp://x", "not a url"} {
		if _, err := New("cl_test_sk_x", "ws", WithBaseURL(u)); err == nil {
			t.Fatalf("%s accepted", u)
		}
	}
	for _, u := range []string{"https://api.example.com", "http://localhost:3000", "http://127.0.0.1", "http://host.docker.internal:8080"} {
		if _, err := New("cl_test_sk_x", "ws", WithBaseURL(u)); err != nil {
			t.Fatalf("%s refused: %v", u, err)
		}
	}
}

func TestPrintingMasksKey(t *testing.T) {
	c, _ := New("cl_test_sk_secret", "ws")
	for _, f := range []string{"%v", "%+v", "%#v", "%s"} {
		if out := fmt.Sprintf(f, c) + fmt.Sprintf(f, *c); strings.Contains(out, "cl_test_sk_secret") {
			t.Fatalf("%s leaked key: %s", f, out)
		}
	}
}

func TestTradingCryptoWithdraw(t *testing.T) {
	var got *http.Request
	c, _ := server(t, func(w http.ResponseWriter, r *http.Request) {
		got = r
		w.Write([]byte(`{"data":{"id":"p_1"}}`))
	})
	if _, err := c.Trading.CryptoWithdraw(context.Background(), Row{"amount_minor": "5000000", "currency": "USDT", "network": "polygon"}, "tw-1"); err != nil {
		t.Fatal(err)
	}
	if got.Method != "POST" || got.URL.Path != "/v1/test/trading/crypto-withdrawals" || got.Header.Get("Idempotency-Key") != "tw-1" {
		t.Fatalf("got %s %s key=%q", got.Method, got.URL.Path, got.Header.Get("Idempotency-Key"))
	}
}
