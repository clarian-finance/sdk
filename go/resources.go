package clarian

import (
	"context"
	"net/http"
	"strings"
)

// One type per API area, in the order of the public reference. Bodies are Row (see openapi.json).

type Balances struct{ c *Client }

func (r *Balances) List(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "balances", nil)
}

type Wallets struct{ c *Client }

func (r *Wallets) List(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "wallets", nil)
}
func (r *Wallets) Create(ctx context.Context, body Row) (Row, error) {
	if body == nil {
		body = Row{}
	}
	return post[Row](ctx, r.c, "wallets", body)
}
func (r *Wallets) Retrieve(ctx context.Context, id string) (Row, error) {
	return get[Row](ctx, r.c, "wallets/"+seg(id), nil)
}
func (r *Wallets) Balance(ctx context.Context, id string) (Row, error) {
	return get[Row](ctx, r.c, "wallets/"+seg(id)+"/balance", nil)
}
func (r *Wallets) Transactions(ctx context.Context, id string, p Params) ([]Row, error) {
	return get[[]Row](ctx, r.c, "wallets/"+seg(id)+"/transactions", p)
}

type Transactions struct{ c *Client }

// List takes limit and before (id of the last row of the previous page).
func (r *Transactions) List(ctx context.Context, p Params) ([]Row, error) {
	return get[[]Row](ctx, r.c, "transactions", p)
}
func (r *Transactions) Retrieve(ctx context.Context, id string) (Row, error) {
	return get[Row](ctx, r.c, "transactions/"+seg(id), nil)
}
func (r *Transactions) Statement(ctx context.Context, p Params) ([]Row, error) {
	return get[[]Row](ctx, r.c, "statement", p)
}

type Pix struct {
	Payins  *PixPayins
	Payouts *PixPayouts
}

// PixPayins is PIX cash-in: a charge the payer pays with a QR code / copia-e-cola.
type PixPayins struct{ c *Client }

func (r *PixPayins) Create(ctx context.Context, body Row, idempotencyKey string) (Row, error) {
	return postKeyed[Row](ctx, r.c, "pix/payins", body, idempotencyKey)
}
func (r *PixPayins) Retrieve(ctx context.Context, id string) (Row, error) {
	return get[Row](ctx, r.c, "pix/payins/"+seg(id), nil)
}

// Simulate settles or fails a pending charge (sandbox only). nil body means {"status":"completed"}.
func (r *PixPayins) Simulate(ctx context.Context, id string, body Row) (Row, error) {
	return post[Row](ctx, r.c, "pix/payins/"+seg(id)+"/simulate", outcome(body))
}

// PixPayouts is PIX cash-out to the workspace's registered settlement key.
type PixPayouts struct{ c *Client }

func (r *PixPayouts) Create(ctx context.Context, body Row, idempotencyKey string) (Row, error) {
	return postKeyed[Row](ctx, r.c, "pix/payouts", body, idempotencyKey)
}
func (r *PixPayouts) Retrieve(ctx context.Context, id string) (Row, error) {
	return get[Row](ctx, r.c, "pix/payouts/"+seg(id), nil)
}
func (r *PixPayouts) Fee(ctx context.Context, body Row) (Row, error) {
	return post[Row](ctx, r.c, "pix/payouts/fee", body)
}
func (r *PixPayouts) Lookup(ctx context.Context, body Row) (Row, error) {
	return post[Row](ctx, r.c, "pix/payouts/dict", body)
}
func (r *PixPayouts) Simulate(ctx context.Context, id string, body Row) (Row, error) {
	return post[Row](ctx, r.c, "pix/payouts/"+seg(id)+"/simulate", outcome(body))
}
func (r *PixPayouts) ListAccounts(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "pix/payout-accounts", nil)
}

func outcome(body Row) Row {
	if body == nil {
		return Row{"status": "completed"}
	}
	return body
}

// Conversions quote then convert, always base_currency -> quote_currency.
type Conversions struct{ c *Client }

func (r *Conversions) Quote(ctx context.Context, body Row) (Row, error) {
	return post[Row](ctx, r.c, "rfq/quote", body)
}

// Execute converts a quote once. Never retried; after a lost reply, check Transactions.List.
func (r *Conversions) Execute(ctx context.Context, quoteID string) (Row, error) {
	return post[Row](ctx, r.c, "rfq/execute", Row{"quote_id": quoteID})
}
func (r *Conversions) Prices(ctx context.Context, symbols ...string) (Row, error) {
	return get[Row](ctx, r.c, "prices", Params{"symbols": strings.Join(symbols, ",")})
}

type Crypto struct{ c *Client }

func (r *Crypto) Send(ctx context.Context, body Row, idempotencyKey string) (Row, error) {
	return postKeyed[Row](ctx, r.c, "crypto/send", body, idempotencyKey)
}
func (r *Crypto) Simulate(ctx context.Context, id string, body Row) (Row, error) {
	return post[Row](ctx, r.c, "crypto/send/"+seg(id)+"/simulate", outcome(body))
}
func (r *Crypto) Networks(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "crypto/networks", nil)
}

type Customers struct{ c *Client }

func (r *Customers) List(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "customers", nil)
}
func (r *Customers) Create(ctx context.Context, body Row) (Row, error) {
	return post[Row](ctx, r.c, "customers", body)
}
func (r *Customers) Retrieve(ctx context.Context, id string) (Row, error) {
	return get[Row](ctx, r.c, "customers/"+seg(id), nil)
}
func (r *Customers) Update(ctx context.Context, id string, body Row) (Row, error) {
	return call[Row](ctx, r.c, http.MethodPatch, "customers/"+seg(id), reqOpts{body: body})
}

type Kyc struct{ c *Client }

func (r *Kyc) List(ctx context.Context) ([]Row, error) { return get[[]Row](ctx, r.c, "kyc", nil) }
func (r *Kyc) Create(ctx context.Context, body Row) (Row, error) {
	return post[Row](ctx, r.c, "kyc", body)
}
func (r *Kyc) Retrieve(ctx context.Context, id string) (Row, error) {
	return get[Row](ctx, r.c, "kyc/"+seg(id), nil)
}
func (r *Kyc) ListRfi(ctx context.Context, id string) ([]Row, error) {
	return get[[]Row](ctx, r.c, "kyc/"+seg(id)+"/rfi", nil)
}
func (r *Kyc) AnswerRfi(ctx context.Context, id string, body Row) (Row, error) {
	return post[Row](ctx, r.c, "kyc/"+seg(id)+"/rfi", body)
}

type Whitelist struct{ c *Client }

func (r *Whitelist) List(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "whitelist", nil)
}
func (r *Whitelist) Settings(ctx context.Context) (Row, error) {
	return get[Row](ctx, r.c, "whitelist/settings", nil)
}

type BankAccounts struct{ c *Client }

func (r *BankAccounts) List(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "bank-accounts", nil)
}

type Webhooks struct{ c *Client }

func (r *Webhooks) List(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "webhooks", nil)
}

// Create returns the signing "secret" in this reply only. Store it.
func (r *Webhooks) Create(ctx context.Context, body Row) (Row, error) {
	return post[Row](ctx, r.c, "webhooks", body)
}
func (r *Webhooks) Retrieve(ctx context.Context, id string) (Row, error) {
	return get[Row](ctx, r.c, "webhooks/"+seg(id), nil)
}
func (r *Webhooks) Update(ctx context.Context, id string, body Row) (Row, error) {
	return call[Row](ctx, r.c, http.MethodPatch, "webhooks/"+seg(id), reqOpts{body: body})
}
func (r *Webhooks) Delete(ctx context.Context, id string) (Row, error) {
	return call[Row](ctx, r.c, http.MethodDelete, "webhooks/"+seg(id), reqOpts{})
}

// Test queues a signed test.ping (sandbox key only).
func (r *Webhooks) Test(ctx context.Context, id string) (Row, error) {
	return post[Row](ctx, r.c, "webhooks/"+seg(id)+"/test", nil)
}
func (r *Webhooks) Deliveries(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "webhooks/deliveries", nil)
}
func (r *Webhooks) Resend(ctx context.Context, deliveryID string) (Row, error) {
	return post[Row](ctx, r.c, "webhooks/deliveries/"+seg(deliveryID)+"/resend", nil)
}

type PaymentLinks struct{ c *Client }

func (r *PaymentLinks) List(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "payment-links", nil)
}
func (r *PaymentLinks) Create(ctx context.Context, body Row) (Row, error) {
	return post[Row](ctx, r.c, "payment-links", body)
}
func (r *PaymentLinks) Retrieve(ctx context.Context, id string) (Row, error) {
	return get[Row](ctx, r.c, "payment-links/"+seg(id), nil)
}
func (r *PaymentLinks) Update(ctx context.Context, id string, body Row) (Row, error) {
	return call[Row](ctx, r.c, http.MethodPatch, "payment-links/"+seg(id), reqOpts{body: body})
}
func (r *PaymentLinks) Delete(ctx context.Context, id string) (Row, error) {
	return call[Row](ctx, r.c, http.MethodDelete, "payment-links/"+seg(id), reqOpts{})
}

// Payments is paginated: {items, next_cursor}; pass next_cursor back as "cursor".
func (r *PaymentLinks) Payments(ctx context.Context, p Params) (Row, error) {
	return get[Row](ctx, r.c, "payment-links/payments", p)
}
func (r *PaymentLinks) Stats(ctx context.Context, p Params) (Row, error) {
	return get[Row](ctx, r.c, "payment-links/stats", p)
}
func (r *PaymentLinks) Splits(ctx context.Context, id string) ([]Row, error) {
	return get[[]Row](ctx, r.c, "payment-links/"+seg(id)+"/splits", nil)
}
func (r *PaymentLinks) ReplaceSplits(ctx context.Context, id string, body Row) ([]Row, error) {
	return call[[]Row](ctx, r.c, http.MethodPut, "payment-links/"+seg(id)+"/splits", reqOpts{body: body})
}
func (r *PaymentLinks) Charge(ctx context.Context, id string, body Row, idempotencyKey string) (Row, error) {
	return postKeyed[Row](ctx, r.c, "payment-links/"+seg(id)+"/charges", body, idempotencyKey)
}

// Checkout is the public checkout page of a payment link.
type Checkout struct{ c *Client }

func (r *Checkout) Retrieve(ctx context.Context, slug string) (Row, error) {
	return get[Row](ctx, r.c, "checkout/"+seg(slug), nil)
}
func (r *Checkout) Pay(ctx context.Context, slug string, body Row, idempotencyKey string) (Row, error) {
	return postKeyed[Row](ctx, r.c, "checkout/"+seg(slug)+"/pay", body, idempotencyKey)
}

type Subaccounts struct{ c *Client }

func (r *Subaccounts) List(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "subaccounts", nil)
}
func (r *Subaccounts) Create(ctx context.Context, body Row) (Row, error) {
	return post[Row](ctx, r.c, "subaccounts", body)
}
func (r *Subaccounts) Retrieve(ctx context.Context, id string) (Row, error) {
	return get[Row](ctx, r.c, "subaccounts/"+seg(id), nil)
}
func (r *Subaccounts) Balances(ctx context.Context, id string) ([]Row, error) {
	return get[[]Row](ctx, r.c, "subaccounts/"+seg(id)+"/balances", nil)
}
func (r *Subaccounts) Transactions(ctx context.Context, id string, p Params) ([]Row, error) {
	return get[[]Row](ctx, r.c, "subaccounts/"+seg(id)+"/transactions", p)
}

type APIKeys struct{ c *Client }

func (r *APIKeys) List(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "api-keys", nil)
}
func (r *APIKeys) Audit(ctx context.Context, p Params) ([]Row, error) {
	return get[[]Row](ctx, r.c, "api-keys/audit", p)
}

type Settings struct{ c *Client }

func (r *Settings) Retrieve(ctx context.Context) (Row, error) {
	return get[Row](ctx, r.c, "settings", nil)
}

type Notifications struct{ c *Client }

func (r *Notifications) List(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "notifications", nil)
}

type Team struct{ c *Client }

func (r *Team) List(ctx context.Context) ([]Row, error) { return get[[]Row](ctx, r.c, "team", nil) }

type Workflows struct{ c *Client }

func (r *Workflows) List(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "workflows", nil)
}
func (r *Workflows) Create(ctx context.Context, body Row) (Row, error) {
	return post[Row](ctx, r.c, "workflows", body)
}
func (r *Workflows) Runs(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "workflows/runs", nil)
}

type Approvals struct{ c *Client }

func (r *Approvals) List(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "approvals", nil)
}
func (r *Approvals) Policy(ctx context.Context) (Row, error) {
	return get[Row](ctx, r.c, "approval-policy", nil)
}

type Treasury struct{ c *Client }

func (r *Treasury) List(ctx context.Context) ([]Row, error) {
	return get[[]Row](ctx, r.c, "treasury", nil)
}
func (r *Treasury) Create(ctx context.Context, body Row) (Row, error) {
	return post[Row](ctx, r.c, "treasury", body)
}
