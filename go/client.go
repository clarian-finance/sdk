// Package clarian is the Go SDK for the Clarian API v2.
package clarian

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"math/big"
	"math/rand"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"time"
)

// DefaultBaseURL is the API origin; requests go to <origin>/v1/{live|test}/<path>.
const DefaultBaseURL = "https://api.clarian.finance"

// Row is a JSON object as returned by the API. Numbers decode as json.Number.
// ponytail: map rows instead of 688 lines of hand-written structs; openapi.json documents the fields.
type Row = map[string]any

// Params is an optional query string; empty values are dropped.
type Params map[string]string

// ErrIdempotencyKeyRequired is returned by money-moving creates called with an empty key.
var ErrIdempotencyKeyRequired = errors.New("idempotencyKey is required: reuse the same key to retry safely")

// Error is every non-2xx reply. Code is the API's stable "error" string, e.g. insufficient_balance.
type Error struct {
	Status  int
	Code    string
	Message string
	Meta    Row
}

func (e *Error) Error() string { return fmt.Sprintf("clarian: %d %s: %s", e.Status, e.Code, e.Message) }

// Client calls the Clarian API. Build it with New.
type Client struct {
	apiKey, workspaceID, subaccountID, baseURL, env string
	http                                            *http.Client
	maxRetries                                      int
	subaccountSet                                   bool

	Balances      *Balances
	Wallets       *Wallets
	Transactions  *Transactions
	Pix           *Pix
	Conversions   *Conversions
	Crypto        *Crypto
	Trading       *Trading
	Customers     *Customers
	Kyc           *Kyc
	Whitelist     *Whitelist
	BankAccounts  *BankAccounts
	Webhooks      *Webhooks
	PaymentLinks  *PaymentLinks
	Checkout      *Checkout
	Subaccounts   *Subaccounts
	APIKeys       *APIKeys
	Settings      *Settings
	Notifications *Notifications
	Team          *Team
	Workflows     *Workflows
	Approvals     *Approvals
	Treasury      *Treasury
}

// Option configures a Client.
type Option func(*Client)

// WithBaseURL overrides the API origin (e.g. http://localhost:3000).
func WithBaseURL(u string) Option { return func(c *Client) { c.baseURL = strings.TrimRight(u, "/") } }

// WithHTTPClient sets the HTTP client (timeouts, proxies, tests). Default timeout 30s.
func WithHTTPClient(h *http.Client) Option {
	return func(c *Client) {
		if h != nil {
			c.http = h
		}
	}
}

// WithMaxRetries sets retries for 429/502/503/504 and network errors. Default 2.
func WithMaxRetries(n int) Option { return func(c *Client) { c.maxRetries = n } }

// WithSubaccount acts on one child workspace (sends X-Subaccount-Id).
func WithSubaccount(id string) Option {
	return func(c *Client) { c.subaccountID, c.subaccountSet = strings.TrimSpace(id), true }
}

// ErrInvalidID is returned for an empty, "." or ".." path id: a URL parser resolves dot-segments to another route.
var ErrInvalidID = errors.New("clarian: id must not be empty, \".\" or \"..\"")

// localHTTP are the only origins allowed over plain http, for local testing.
var localHTTP = map[string]bool{"localhost": true, "127.0.0.1": true, "host.docker.internal": true}

func checkBaseURL(raw string) error {
	u, err := url.Parse(raw)
	if err != nil || u.Host == "" || u.User != nil {
		return fmt.Errorf("clarian: invalid base URL %q", raw)
	}
	if u.Scheme == "https" || (u.Scheme == "http" && localHTTP[strings.ToLower(u.Hostname())]) {
		return nil
	}
	return errors.New("clarian: base URL must be https (http only for localhost, 127.0.0.1, host.docker.internal)")
}

// New builds a client. The key prefix picks the environment: cl_test_sk_ -> /v1/test, cl_live_sk_ -> /v1/live.
func New(apiKey, workspaceID string, opts ...Option) (*Client, error) {
	c := &Client{apiKey: apiKey, workspaceID: workspaceID, baseURL: DefaultBaseURL,
		http: &http.Client{Timeout: 30 * time.Second}, maxRetries: 2}
	switch {
	case strings.HasPrefix(apiKey, "cl_live_sk_"):
		c.env = "live"
	case strings.HasPrefix(apiKey, "cl_test_sk_"):
		c.env = "test"
	default:
		return nil, errors.New("invalid API key format: must start with cl_live_sk_ or cl_test_sk_")
	}
	if workspaceID == "" {
		return nil, errors.New("workspaceID is required")
	}
	for _, o := range opts {
		o(c)
	}
	if err := checkBaseURL(c.baseURL); err != nil {
		return nil, err
	}
	if c.subaccountSet && c.subaccountID == "" {
		return nil, errors.New("clarian: subaccount id is required")
	}
	c.Balances = &Balances{c}
	c.Wallets = &Wallets{c}
	c.Transactions = &Transactions{c}
	c.Pix = &Pix{Payins: &PixPayins{c}, Payouts: &PixPayouts{c}}
	c.Conversions = &Conversions{c}
	c.Crypto = &Crypto{c}
	c.Trading = &Trading{c}
	c.Customers = &Customers{c}
	c.Kyc = &Kyc{c}
	c.Whitelist = &Whitelist{c}
	c.BankAccounts = &BankAccounts{c}
	c.Webhooks = &Webhooks{c}
	c.PaymentLinks = &PaymentLinks{c}
	c.Checkout = &Checkout{c}
	c.Subaccounts = &Subaccounts{c}
	c.APIKeys = &APIKeys{c}
	c.Settings = &Settings{c}
	c.Notifications = &Notifications{c}
	c.Team = &Team{c}
	c.Workflows = &Workflows{c}
	c.Approvals = &Approvals{c}
	c.Treasury = &Treasury{c}
	return c, nil
}

// Environment is "live" or "sandbox".
func (c *Client) Environment() string {
	if c.env == "live" {
		return "live"
	}
	return "sandbox"
}

// String masks the API key so printing a client (%v, %+v) never reveals it.
func (c Client) String() string {
	return fmt.Sprintf("clarian.Client{env: %s, workspace: %s, subaccount: %s, apiKey: ****}", c.env, c.workspaceID, c.subaccountID)
}

// GoString masks the API key for %#v.
func (c Client) GoString() string { return c.String() }

// ForSubaccount returns a client that acts on one child workspace. It panics on a blank id,
// which would otherwise drop the header and silently act on the parent workspace.
func (c *Client) ForSubaccount(id string) *Client {
	if strings.TrimSpace(id) == "" {
		panic("clarian: ForSubaccount needs a non-empty id")
	}
	n, _ := New(c.apiKey, c.workspaceID, WithBaseURL(c.baseURL), WithHTTPClient(c.http), WithMaxRetries(c.maxRetries), WithSubaccount(id))
	return n
}

// Ping checks the key and workspace.
func (c *Client) Ping(ctx context.Context) (Row, error) { return get[Row](ctx, c, "ping", nil) }

type reqOpts struct {
	query   Params
	body    any
	idemKey string
	otp     string
}

// normalize sends amounts as strings: *big.Int and integers under *_minor keys become strings; floats are refused.
func normalize(key string, v any) (any, error) {
	minor := strings.HasSuffix(key, "_minor")
	switch t := v.(type) {
	case float32, float64:
		return nil, fmt.Errorf("clarian: %q is a float; amounts are integer minor units (string, int or *big.Int)", key)
	case *big.Int:
		return t.String(), nil
	case big.Int:
		return t.String(), nil
	case int, int8, int16, int32, int64, uint, uint8, uint16, uint32, uint64:
		if minor {
			return fmt.Sprint(t), nil
		}
		return t, nil
	case map[string]any:
		out := make(map[string]any, len(t))
		for k, x := range t {
			n, err := normalize(k, x)
			if err != nil {
				return nil, err
			}
			out[k] = n
		}
		return out, nil
	case []map[string]any:
		out := make([]any, len(t))
		for i, x := range t {
			n, err := normalize(key, x)
			if err != nil {
				return nil, err
			}
			out[i] = n
		}
		return out, nil
	case []any:
		out := make([]any, len(t))
		for i, x := range t {
			n, err := normalize(key, x)
			if err != nil {
				return nil, err
			}
			out[i] = n
		}
		return out, nil
	}
	return v, nil
}

var retryStatus = map[int]bool{429: true, 502: true, 503: true, 504: true}

func backoff(attempt int, retryAfter string) time.Duration {
	if s, err := strconv.ParseFloat(retryAfter, 64); err == nil && s >= 0 {
		return time.Duration(min(s, 30) * float64(time.Second))
	}
	return time.Duration(rand.Float64() * float64(min(8000, 500<<attempt)) * float64(time.Millisecond))
}

func (c *Client) do(ctx context.Context, method, path string, o reqOpts, out any) error {
	for _, p := range strings.Split(path, "/") {
		if p == "" || p == "%2E" || p == "%2E%2E" {
			return ErrInvalidID
		}
	}
	u := fmt.Sprintf("%s/v1/%s/%s", c.baseURL, c.env, path)
	q := url.Values{}
	for k, v := range o.query {
		if v != "" {
			q.Set(k, v)
		}
	}
	if len(q) > 0 {
		u += "?" + q.Encode()
	}
	var payload []byte
	if o.body != nil {
		b, err := normalize("", o.body)
		if err != nil {
			return err
		}
		if payload, err = json.Marshal(b); err != nil {
			return err
		}
	}
	// Only a request that cannot double-apply is retried: reads, deletes, or a create with an idempotency key.
	retries := 0
	if method == http.MethodGet || method == http.MethodDelete || o.idemKey != "" {
		retries = c.maxRetries
	}
	var resp *http.Response
	for attempt := 0; ; attempt++ {
		var body io.Reader
		if payload != nil {
			body = bytes.NewReader(payload)
		}
		req, err := http.NewRequestWithContext(ctx, method, u, body)
		if err != nil {
			return err
		}
		req.Header.Set("Authorization", "Bearer "+c.apiKey)
		req.Header.Set("X-Workspace-Id", c.workspaceID)
		if payload != nil {
			req.Header.Set("Content-Type", "application/json")
		}
		if c.subaccountID != "" {
			req.Header.Set("X-Subaccount-Id", c.subaccountID)
		}
		if o.idemKey != "" {
			req.Header.Set("Idempotency-Key", o.idemKey)
		}
		if o.otp != "" {
			req.Header.Set("X-OTP-Code", o.otp)
		}
		resp, err = c.http.Do(req)
		again := err != nil || retryStatus[resp.StatusCode]
		if !again || attempt >= retries {
			if err != nil {
				return err
			}
			break
		}
		ra := ""
		if resp != nil {
			ra = resp.Header.Get("Retry-After")
			resp.Body.Close()
		}
		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-time.After(backoff(attempt, ra)):
		}
	}
	defer resp.Body.Close()
	raw, err := io.ReadAll(resp.Body)
	if err != nil {
		return err
	}
	var env map[string]json.RawMessage
	obj := Row{}
	if len(raw) > 0 {
		if json.Unmarshal(raw, &env) != nil {
			if resp.StatusCode >= 300 {
				return &Error{Status: resp.StatusCode, Code: "invalid_json_response", Message: string(raw[:min(len(raw), 500)])}
			}
			return fmt.Errorf("clarian: invalid JSON response: %.200s", raw)
		}
	}
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		_ = decode(raw, &obj)
		e := &Error{Status: resp.StatusCode, Code: "unknown_error", Meta: Row{}}
		for k, v := range obj {
			switch k {
			case "error":
				if s, ok := v.(string); ok {
					e.Code = s
				}
			case "message":
				e.Message, _ = v.(string)
			default:
				e.Meta[k] = v
			}
		}
		if e.Message == "" {
			e.Message = e.Code
		}
		return e
	}
	if out == nil {
		return nil
	}
	if d, ok := env["data"]; ok {
		raw = d
	}
	if len(raw) == 0 {
		raw = []byte("{}")
	}
	return decode(raw, out)
}

func decode(raw []byte, out any) error {
	d := json.NewDecoder(bytes.NewReader(raw))
	d.UseNumber()
	return d.Decode(out)
}

func call[T any](ctx context.Context, c *Client, method, path string, o reqOpts) (T, error) {
	var out T
	err := c.do(ctx, method, path, o, &out)
	return out, err
}

func get[T any](ctx context.Context, c *Client, path string, q Params) (T, error) {
	return call[T](ctx, c, http.MethodGet, path, reqOpts{query: q})
}

func post[T any](ctx context.Context, c *Client, path string, body any) (T, error) {
	return call[T](ctx, c, http.MethodPost, path, reqOpts{body: body})
}

// postKeyed is a money-moving create: an empty key fails before any network call.
func postKeyed[T any](ctx context.Context, c *Client, path string, body any, key string) (T, error) {
	if key == "" {
		var zero T
		return zero, ErrIdempotencyKeyRequired
	}
	return call[T](ctx, c, http.MethodPost, path, reqOpts{body: body, idemKey: key})
}

// seg escapes one path segment; an empty, "." or ".." id is refused by do (ErrInvalidID).
func seg(id string) string { return strings.ReplaceAll(url.PathEscape(id), ".", "%2E") }
