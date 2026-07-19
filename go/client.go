package clarian

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"time"
)

// Production and sandbox gateway bases (api.clarian.finance).
const (
	BaseURLLive = "https://api.clarian.finance/functions/v1/api-gateway/live"
	BaseURLTest = "https://api.clarian.finance/functions/v1/api-gateway/test"
)

// Client is the Clarian Finance API client.
type Client struct {
	apiKey      string
	workspaceID string
	baseURL     string
	http        *http.Client

	CashIn       *CashInService
	CashOut      *CashOutService
	Balances     *BalancesService
	Transactions *TransactionsService
	Webhooks     *WebhooksService
}

// Option configures a Client.
type Option func(*Client)

// WithBaseURL overrides the environment-inferred gateway URL.
func WithBaseURL(baseURL string) Option {
	return func(c *Client) {
		c.baseURL = strings.TrimRight(baseURL, "/")
	}
}

// WithHTTPClient sets a custom HTTP client (timeouts, transport, etc.).
func WithHTTPClient(hc *http.Client) Option {
	return func(c *Client) {
		if hc != nil {
			c.http = hc
		}
	}
}

// New builds a Client. The base URL is inferred from the API key prefix
// (cl_test_sk_ → sandbox, otherwise live) unless WithBaseURL is set.
func New(apiKey, workspaceID string, opts ...Option) *Client {
	base := BaseURLLive
	if strings.HasPrefix(apiKey, "cl_test_sk_") {
		base = BaseURLTest
	}
	c := &Client{
		apiKey:      apiKey,
		workspaceID: workspaceID,
		baseURL:     base,
		http:        &http.Client{Timeout: 30 * time.Second},
	}
	for _, opt := range opts {
		opt(c)
	}
	c.CashIn = &CashInService{c: c}
	c.CashOut = &CashOutService{c: c}
	c.Balances = &BalancesService{c: c}
	c.Transactions = &TransactionsService{c: c}
	c.Webhooks = &WebhooksService{c: c}
	return c
}

// Ping checks credentials and returns workspace context.
func (c *Client) Ping(ctx context.Context) (*PingResponse, error) {
	var out PingResponse
	if err := c.do(ctx, http.MethodGet, "/ping", "", nil, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// do sends an authenticated JSON request. Non-2xx responses become *APIError.
// Transport and decode failures are wrapped with %w.
func (c *Client) do(ctx context.Context, method, path, idempotencyKey string, body any, out any) error {
	var reader io.Reader
	if body != nil {
		raw, err := json.Marshal(body)
		if err != nil {
			return fmt.Errorf("clarian: marshal body: %w", err)
		}
		reader = bytes.NewReader(raw)
	}

	req, err := http.NewRequestWithContext(ctx, method, c.baseURL+path, reader)
	if err != nil {
		return fmt.Errorf("clarian: new request: %w", err)
	}
	req.Header.Set("Authorization", "Bearer "+c.apiKey)
	req.Header.Set("X-Workspace-Id", c.workspaceID)
	if body != nil {
		req.Header.Set("Content-Type", "application/json")
	}
	if idempotencyKey != "" {
		req.Header.Set("Idempotency-Key", idempotencyKey)
	}

	res, err := c.http.Do(req)
	if err != nil {
		return fmt.Errorf("clarian: request: %w", err)
	}
	defer res.Body.Close()

	raw, err := io.ReadAll(res.Body)
	if err != nil {
		return fmt.Errorf("clarian: read body: %w", err)
	}
	if res.StatusCode < 200 || res.StatusCode >= 300 {
		return newAPIError(res.StatusCode, raw)
	}
	if out == nil || len(raw) == 0 {
		return nil
	}
	if err := json.Unmarshal(raw, out); err != nil {
		return fmt.Errorf("clarian: decode response: %w", err)
	}
	return nil
}

// CashInService creates and fetches PIX cash-in orders.
type CashInService struct{ c *Client }

// Create generates a dynamic PIX QR / copia-e-cola. idempotencyKey is required
// by the API — retries with the same key return the original order.
func (s *CashInService) Create(ctx context.Context, idempotencyKey string, req CashInRequest) (*CashInOrderResponse, error) {
	var out CashInOrderResponse
	if err := s.c.do(ctx, http.MethodPost, "/cash-in/pix", idempotencyKey, req, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// Get retrieves a cash-in order by ID.
func (s *CashInService) Get(ctx context.Context, id string) (*CashInOrderResponse, error) {
	var out CashInOrderResponse
	if err := s.c.do(ctx, http.MethodGet, "/cash-in/"+url.PathEscape(id), "", nil, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// CashOutService creates and fetches PIX cash-out orders.
type CashOutService struct{ c *Client }

// Create sends BRL via PIX. idempotencyKey is required — retries never double-send.
func (s *CashOutService) Create(ctx context.Context, idempotencyKey string, req CashOutRequest) (*CashOutOrderResponse, error) {
	var out CashOutOrderResponse
	if err := s.c.do(ctx, http.MethodPost, "/cash-out/pix", idempotencyKey, req, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// Get retrieves a cash-out order by ID.
func (s *CashOutService) Get(ctx context.Context, id string) (*CashOutOrderResponse, error) {
	var out CashOutOrderResponse
	if err := s.c.do(ctx, http.MethodGet, "/cash-out/"+url.PathEscape(id), "", nil, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// BalancesService reads workspace balances.
type BalancesService struct{ c *Client }

// List returns balances per currency (BRL, USDT, USDC).
func (s *BalancesService) List(ctx context.Context) ([]Balance, error) {
	var res struct {
		Balances []Balance `json:"balances"`
	}
	if err := s.c.do(ctx, http.MethodGet, "/account/balances", "", nil, &res); err != nil {
		return nil, err
	}
	return res.Balances, nil
}

// TransactionsService lists and fetches ledger transactions.
type TransactionsService struct{ c *Client }

// List returns transactions, optionally filtered.
func (s *TransactionsService) List(ctx context.Context, params ListTransactionsParams) ([]Transaction, error) {
	q := url.Values{}
	if params.Type != "" {
		q.Set("type", params.Type)
	}
	if params.Status != "" {
		q.Set("status", params.Status)
	}
	if params.Limit > 0 {
		q.Set("limit", strconv.Itoa(params.Limit))
	}
	path := "/transactions"
	if enc := q.Encode(); enc != "" {
		path += "?" + enc
	}
	var res struct {
		Transactions []Transaction `json:"transactions"`
	}
	if err := s.c.do(ctx, http.MethodGet, path, "", nil, &res); err != nil {
		return nil, err
	}
	return res.Transactions, nil
}

// Get returns a single transaction by ID.
func (s *TransactionsService) Get(ctx context.Context, id string) (*Transaction, error) {
	var res struct {
		Transaction Transaction `json:"transaction"`
	}
	if err := s.c.do(ctx, http.MethodGet, "/transactions/"+url.PathEscape(id), "", nil, &res); err != nil {
		return nil, err
	}
	return &res.Transaction, nil
}

// WebhooksService manages webhook subscriptions.
type WebhooksService struct{ c *Client }

// List returns all webhook subscriptions.
// Response shape: {ok, environment, subscriptions: [...]}.
func (s *WebhooksService) List(ctx context.Context) ([]Webhook, error) {
	var res struct {
		Subscriptions []Webhook `json:"subscriptions"`
	}
	if err := s.c.do(ctx, http.MethodGet, "/webhooks", "", nil, &res); err != nil {
		return nil, err
	}
	return res.Subscriptions, nil
}

// Create registers a webhook URL. Secret is a sibling of subscription in the
// 201 body and is shown only once — store it.
// Response shape: {ok, environment, subscription: {...}, secret: "whsec_..."}.
func (s *WebhooksService) Create(ctx context.Context, input WebhookInput) (*WebhookWithSecret, error) {
	var res struct {
		Subscription Webhook `json:"subscription"`
		Secret       string  `json:"secret"`
	}
	if err := s.c.do(ctx, http.MethodPost, "/webhooks", "", input, &res); err != nil {
		return nil, err
	}
	return &WebhookWithSecret{Webhook: res.Subscription, Secret: res.Secret}, nil
}

// Get retrieves a webhook subscription by ID.
// Response shape: {ok, environment, subscription: {...}}.
func (s *WebhooksService) Get(ctx context.Context, id string) (*Webhook, error) {
	var res struct {
		Subscription Webhook `json:"subscription"`
	}
	if err := s.c.do(ctx, http.MethodGet, "/webhooks/"+url.PathEscape(id), "", nil, &res); err != nil {
		return nil, err
	}
	return &res.Subscription, nil
}

// Update patches a webhook subscription.
// Response shape: {ok, environment, subscription: {...}}.
func (s *WebhooksService) Update(ctx context.Context, id string, input WebhookInput) (*Webhook, error) {
	var res struct {
		Subscription Webhook `json:"subscription"`
	}
	if err := s.c.do(ctx, http.MethodPatch, "/webhooks/"+url.PathEscape(id), "", input, &res); err != nil {
		return nil, err
	}
	return &res.Subscription, nil
}

// Delete removes a webhook subscription. Response shape: {ok: true}.
func (s *WebhooksService) Delete(ctx context.Context, id string) error {
	return s.c.do(ctx, http.MethodDelete, "/webhooks/"+url.PathEscape(id), "", nil, nil)
}
