package clarian

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

func TestNew_envInference(t *testing.T) {
	t.Parallel()
	cases := []struct {
		name   string
		apiKey string
		want   string
	}{
		{"test prefix", "cl_test_sk_abc", BaseURLTest},
		{"live prefix", "cl_live_sk_abc", BaseURLLive},
		{"unknown defaults live", "other_key", BaseURLLive},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			c := New(tc.apiKey, "ws")
			if c.baseURL != tc.want {
				t.Fatalf("baseURL = %q, want %q", c.baseURL, tc.want)
			}
		})
	}
}

func TestNew_WithBaseURLOverrides(t *testing.T) {
	t.Parallel()
	c := New("cl_test_sk_x", "ws", WithBaseURL("http://localhost:9999/v1/"))
	if c.baseURL != "http://localhost:9999/v1" {
		t.Fatalf("baseURL = %q", c.baseURL)
	}
}

func TestAuthHeadersOnEveryCall(t *testing.T) {
	t.Parallel()
	var gotAuth, gotWS, gotMethod, gotPath string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		gotAuth = r.Header.Get("Authorization")
		gotWS = r.Header.Get("X-Workspace-Id")
		gotMethod = r.Method
		gotPath = r.URL.Path
		_, _ = w.Write([]byte(`{"ok":true,"environment":"sandbox","master_account_id":"m1"}`))
	}))
	t.Cleanup(srv.Close)

	c := New("cl_test_sk_key", "ws-uuid", WithBaseURL(srv.URL))
	if _, err := c.Ping(context.Background()); err != nil {
		t.Fatalf("Ping: %v", err)
	}
	if gotAuth != "Bearer cl_test_sk_key" {
		t.Errorf("Authorization = %q", gotAuth)
	}
	if gotWS != "ws-uuid" {
		t.Errorf("X-Workspace-Id = %q", gotWS)
	}
	if gotMethod != http.MethodGet || gotPath != "/ping" {
		t.Errorf("request = %s %s", gotMethod, gotPath)
	}
}

func TestCashInCreate_idempotencyAndPayer(t *testing.T) {
	t.Parallel()
	var gotIdem, gotBody string
	var gotAuth, gotWS string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		gotAuth = r.Header.Get("Authorization")
		gotWS = r.Header.Get("X-Workspace-Id")
		gotIdem = r.Header.Get("Idempotency-Key")
		raw, _ := io.ReadAll(r.Body)
		gotBody = string(raw)
		if r.URL.Path != "/cash-in/pix" || r.Method != http.MethodPost {
			t.Errorf("unexpected %s %s", r.Method, r.URL.Path)
		}
		w.WriteHeader(http.StatusCreated)
		_, _ = w.Write([]byte(`{"ok":true,"environment":"sandbox","order":{"id":"ord_1","status":"pending","amount":19.50,"currency":"BRL","pix":{"qr_code":"qr","copy_paste":"brcode","end_to_end_id":null},"fee":0.50}}`))
	}))
	t.Cleanup(srv.Close)

	c := New("cl_test_sk_x", "ws-uuid", WithBaseURL(srv.URL))
	res, err := c.CashIn.Create(context.Background(), "pay-ext-1", CashInRequest{
		Amount: json.Number("19.50"),
		Payer:  Payer{Name: "Ana", DocumentNumber: "52998224725"},
	})
	if err != nil {
		t.Fatalf("Create: %v", err)
	}
	if res.Order.ID != "ord_1" || res.Order.Status != TxStatusPending {
		t.Fatalf("order = %+v", res.Order)
	}
	if res.Order.Pix.CopyPaste == nil || *res.Order.Pix.CopyPaste != "brcode" {
		t.Fatalf("pix = %+v", res.Order.Pix)
	}
	if gotAuth != "Bearer cl_test_sk_x" || gotWS != "ws-uuid" {
		t.Errorf("auth headers: %q %q", gotAuth, gotWS)
	}
	if gotIdem != "pay-ext-1" {
		t.Errorf("Idempotency-Key = %q", gotIdem)
	}

	var body map[string]json.RawMessage
	if err := json.Unmarshal([]byte(gotBody), &body); err != nil {
		t.Fatalf("body: %v", err)
	}
	if string(body["amount"]) != "19.50" {
		t.Errorf("amount wire = %s, want number 19.50", body["amount"])
	}
	var payer Payer
	if err := json.Unmarshal(body["payer"], &payer); err != nil {
		t.Fatalf("payer: %v", err)
	}
	if payer.Name != "Ana" || payer.DocumentNumber != "52998224725" {
		t.Errorf("payer = %+v", payer)
	}
}

func TestCashOutCreate_idempotencyAndPixKeyType(t *testing.T) {
	t.Parallel()
	var gotIdem, gotBody string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		gotIdem = r.Header.Get("Idempotency-Key")
		raw, _ := io.ReadAll(r.Body)
		gotBody = string(raw)
		w.WriteHeader(http.StatusCreated)
		_, _ = w.Write([]byte(`{"ok":true,"environment":"sandbox","order":{"id":"out_9","status":"processing","amount":10.00}}`))
	}))
	t.Cleanup(srv.Close)

	c := New("cl_test_sk_x", "ws", WithBaseURL(srv.URL))
	out, err := c.CashOut.Create(context.Background(), "repasse-1", CashOutRequest{
		Amount:     json.Number("10.00"),
		PixKey:     "abc-key",
		PixKeyType: PixKeyEVP,
	})
	if err != nil {
		t.Fatalf("Create: %v", err)
	}
	if out.Order.ID != "out_9" || out.Order.Status != TxStatusProcessing {
		t.Fatalf("order = %+v", out.Order)
	}
	if gotIdem != "repasse-1" {
		t.Errorf("Idempotency-Key = %q", gotIdem)
	}
	if !strings.Contains(gotBody, `"pix_key_type":"EVP"`) {
		t.Errorf("body missing EVP: %s", gotBody)
	}
	if !strings.Contains(gotBody, `"amount":10.00`) && !strings.Contains(gotBody, `"amount":10`) {
		t.Errorf("amount not numeric: %s", gotBody)
	}
}

func TestCashInGet(t *testing.T) {
	t.Parallel()
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/cash-in/ord_paid" {
			t.Errorf("path = %s", r.URL.Path)
		}
		_, _ = w.Write([]byte(`{"ok":true,"order":{"id":"ord_paid","status":"completed","amount":19.50}}`))
	}))
	t.Cleanup(srv.Close)

	c := New("cl_live_sk_x", "ws", WithBaseURL(srv.URL))
	res, err := c.CashIn.Get(context.Background(), "ord_paid")
	if err != nil {
		t.Fatalf("Get: %v", err)
	}
	if res.Order.Status != TxStatusCompleted {
		t.Fatalf("status = %s", res.Order.Status)
	}
}

func TestBalancesList(t *testing.T) {
	t.Parallel()
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/account/balances" {
			t.Errorf("path = %s", r.URL.Path)
		}
		_, _ = w.Write([]byte(`{"ok":true,"balances":[{"currency":"BRL","available":100.50,"pending":0,"locked":0}]}`))
	}))
	t.Cleanup(srv.Close)

	c := New("cl_test_sk_x", "ws", WithBaseURL(srv.URL))
	bals, err := c.Balances.List(context.Background())
	if err != nil {
		t.Fatalf("List: %v", err)
	}
	if len(bals) != 1 || bals[0].Currency != "BRL" || bals[0].Available != "100.50" {
		t.Fatalf("balances = %+v", bals)
	}
}

func TestTransactionsListAndGet(t *testing.T) {
	t.Parallel()
	mux := http.NewServeMux()
	mux.HandleFunc("/transactions/", func(w http.ResponseWriter, r *http.Request) {
		id := strings.TrimPrefix(r.URL.Path, "/transactions/")
		_, _ = w.Write([]byte(`{"transaction":{"id":"` + id + `","type":"pix_in","status":"completed","amount":10,"fee":0.1,"currency":"BRL","created_at":"t","updated_at":"t"}}`))
	})
	mux.HandleFunc("/transactions", func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Query().Get("type") != "pix_in" || r.URL.Query().Get("limit") != "20" {
			t.Errorf("query = %s", r.URL.RawQuery)
		}
		_, _ = w.Write([]byte(`{"transactions":[{"id":"tx1","type":"pix_in","status":"completed","amount":10,"fee":0,"currency":"BRL","created_at":"t","updated_at":"t"}]}`))
	})
	srv := httptest.NewServer(mux)
	t.Cleanup(srv.Close)

	c := New("cl_test_sk_x", "ws", WithBaseURL(srv.URL))
	list, err := c.Transactions.List(context.Background(), ListTransactionsParams{Type: "pix_in", Limit: 20})
	if err != nil {
		t.Fatalf("List: %v", err)
	}
	if len(list) != 1 || list[0].ID != "tx1" {
		t.Fatalf("list = %+v", list)
	}
	tx, err := c.Transactions.Get(context.Background(), "tx-99")
	if err != nil {
		t.Fatalf("Get: %v", err)
	}
	if tx.ID != "tx-99" {
		t.Fatalf("tx = %+v", tx)
	}
}

func TestWebhooksCRUD(t *testing.T) {
	t.Parallel()
	// Fixtures match live api-gateway wrappers (subscription/secret siblings).
	mux := http.NewServeMux()
	mux.HandleFunc("/webhooks/", func(w http.ResponseWriter, r *http.Request) {
		switch r.Method {
		case http.MethodGet:
			_, _ = w.Write([]byte(`{"ok":true,"environment":"sandbox","subscription":{"id":"wh1","url":"https://ex.com","events":["pix_payin.completed"],"is_active":true,"description":null,"environment":"sandbox","created_at":"t","updated_at":"t2"}}`))
		case http.MethodPatch:
			_, _ = w.Write([]byte(`{"ok":true,"environment":"sandbox","subscription":{"id":"wh1","url":"https://ex.com/v2","events":["pix_payin.completed"],"is_active":false,"description":null,"environment":"sandbox","created_at":"t","updated_at":"t3"}}`))
		case http.MethodDelete:
			w.WriteHeader(http.StatusOK)
			_, _ = w.Write([]byte(`{"ok":true}`))
		default:
			t.Errorf("method %s", r.Method)
		}
	})
	mux.HandleFunc("/webhooks", func(w http.ResponseWriter, r *http.Request) {
		switch r.Method {
		case http.MethodGet:
			_, _ = w.Write([]byte(`{"ok":true,"environment":"sandbox","subscriptions":[{"id":"wh1","url":"https://ex.com","events":["pix_payin.completed"],"is_active":true,"created_at":"t","updated_at":"t2"}]}`))
		case http.MethodPost:
			w.WriteHeader(http.StatusCreated)
			_, _ = w.Write([]byte(`{"ok":true,"environment":"sandbox","subscription":{"id":"wh1","url":"https://ex.com","events":["pix_payin.completed"],"is_active":true,"description":null,"environment":"sandbox","created_at":"t","updated_at":"t"},"secret":"whsec_abc"}`))
		default:
			t.Errorf("method %s", r.Method)
		}
	})
	srv := httptest.NewServer(mux)
	t.Cleanup(srv.Close)

	c := New("cl_test_sk_x", "ws", WithBaseURL(srv.URL))
	ctx := context.Background()

	created, err := c.Webhooks.Create(ctx, WebhookInput{URL: "https://ex.com", Events: []string{"pix_payin.completed"}})
	if err != nil {
		t.Fatalf("Create: %v", err)
	}
	if created.Secret != "whsec_abc" || created.ID != "wh1" {
		t.Fatalf("created = %+v secret=%q", created.Webhook, created.Secret)
	}

	subs, err := c.Webhooks.List(ctx)
	if err != nil || len(subs) != 1 {
		t.Fatalf("List: %v %+v", err, subs)
	}
	got, err := c.Webhooks.Get(ctx, "wh1")
	if err != nil || got.ID != "wh1" || got.UpdatedAt != "t2" {
		t.Fatalf("Get: %v %+v", err, got)
	}
	active := false
	upd, err := c.Webhooks.Update(ctx, "wh1", WebhookInput{URL: "https://ex.com/v2", Events: []string{"pix_payin.completed"}, IsActive: &active})
	if err != nil || upd.IsActive || upd.URL != "https://ex.com/v2" {
		t.Fatalf("Update: %v %+v", err, upd)
	}
	if err := c.Webhooks.Delete(ctx, "wh1"); err != nil {
		t.Fatalf("Delete: %v", err)
	}
}

func TestAPIError_jsonCode(t *testing.T) {
	t.Parallel()
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusPaymentRequired)
		_, _ = w.Write([]byte(`{"error":"insufficient_balance","detail":"saldo insuficiente"}`))
	}))
	t.Cleanup(srv.Close)

	c := New("cl_test_sk_x", "ws", WithBaseURL(srv.URL))
	_, err := c.CashOut.Create(context.Background(), "idem", CashOutRequest{Amount: json.Number("100")})
	if err == nil {
		t.Fatal("expected error")
	}
	var apiErr *APIError
	if !errors.As(err, &apiErr) {
		t.Fatalf("want *APIError, got %T %v", err, err)
	}
	if apiErr.Status != 402 || apiErr.Code != "insufficient_balance" {
		t.Fatalf("apiErr = %+v", apiErr)
	}
	if !strings.Contains(apiErr.Body, "insufficient_balance") {
		t.Errorf("Body = %q", apiErr.Body)
	}
}

func TestAPIError_nonJSONBody(t *testing.T) {
	t.Parallel()
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusTooManyRequests)
		_, _ = w.Write([]byte(`rate limited`))
	}))
	t.Cleanup(srv.Close)

	c := New("cl_test_sk_x", "ws", WithBaseURL(srv.URL))
	err := c.do(context.Background(), http.MethodGet, "/ping", "", nil, nil)
	if err == nil {
		t.Fatal("expected error")
	}
	var apiErr *APIError
	if !errors.As(err, &apiErr) {
		t.Fatalf("want *APIError, got %T %v", err, err)
	}
	if apiErr.Status != 429 || apiErr.Code != "" || apiErr.Body != "rate limited" {
		t.Fatalf("apiErr = %+v", apiErr)
	}
}

func TestDo_doesNotSetIdempotencyOnGET(t *testing.T) {
	t.Parallel()
	var gotIdem string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		gotIdem = r.Header.Get("Idempotency-Key")
		_, _ = w.Write([]byte(`{"ok":true,"order":{"id":"x","status":"pending"}}`))
	}))
	t.Cleanup(srv.Close)

	c := New("cl_test_sk_x", "ws", WithBaseURL(srv.URL))
	if _, err := c.CashIn.Get(context.Background(), "x"); err != nil {
		t.Fatalf("Get: %v", err)
	}
	if gotIdem != "" {
		t.Errorf("unexpected Idempotency-Key on GET: %q", gotIdem)
	}
}
