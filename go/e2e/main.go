// End-to-end run of every SDK method against a sandbox API, plus a spec drift check.
// Usage (from go/): CLARIAN_API_KEY=cl_test_sk_… CLARIAN_WORKSPACE_ID=… [CLARIAN_BASE_URL=http://localhost:3000] go run ./e2e
package main

import (
	"context"
	"crypto/rand"
	"encoding/json"
	"errors"
	"fmt"
	"math/big"
	mrand "math/rand"
	"os"
	"sort"
	"strings"
	"time"

	clarian "github.com/clarian-finance/sdk/go"
)

type outcome struct {
	step string
	ok   bool
	note string
}

var (
	ctx      = context.Background()
	outcomes []outcome
	drift    = map[string]map[string]bool{}
	schemas  map[string]struct {
		Properties map[string]any `json:"properties"`
	}
)

func checkShape(schema string, v any) {
	props := schemas[schema].Properties
	var rows []clarian.Row
	switch t := v.(type) {
	case clarian.Row:
		rows = []clarian.Row{t}
	case []clarian.Row:
		rows = t
	}
	for i, r := range rows {
		if i == 5 {
			break
		}
		for k := range r {
			if _, ok := props[k]; !ok {
				if drift[schema] == nil {
					drift[schema] = map[string]bool{}
				}
				drift[schema]["+"+k] = true
			}
		}
	}
}

func note(err error) string {
	var e *clarian.Error
	if errors.As(err, &e) {
		return fmt.Sprintf("%d %s", e.Status, e.Code)
	}
	return err.Error()
}

func step[T any](name string, fn func() (T, error), schema ...string) (T, bool) {
	v, err := fn()
	if err != nil {
		outcomes = append(outcomes, outcome{name, false, note(err)})
		return v, false
	}
	if len(schema) > 0 {
		checkShape(schema[0], any(v))
	}
	b, _ := json.Marshal(v)
	outcomes = append(outcomes, outcome{name, true, string(b[:min(len(b), 140)])})
	return v, true
}

func expectError(name, code string, fn func() error) {
	err := fn()
	if err == nil {
		outcomes = append(outcomes, outcome{name, false, "accepted, expected " + code})
		return
	}
	got := err.Error()
	var e *clarian.Error
	if errors.As(err, &e) {
		got = e.Code
	}
	outcomes = append(outcomes, outcome{name, got == code, got})
}

func check(name string, ok bool, n string) { outcomes = append(outcomes, outcome{name, ok, n}) }

func key() string {
	b := make([]byte, 16)
	rand.Read(b)
	return fmt.Sprintf("%x-%x-%x-%x-%x", b[0:4], b[4:6], b[6:8], b[8:10], b[10:])
}

// A fresh valid CPF per run: the API rejects a document already on file.
func cpf() string {
	ds := make([]int, 9)
	for i := range ds {
		ds[i] = mrand.Intn(10)
	}
	digit := func(ds []int) int {
		s := 0
		for i, d := range ds {
			s += d * (len(ds) + 1 - i)
		}
		return (s * 10 % 11) % 10
	}
	ds = append(ds, digit(ds))
	ds = append(ds, digit(ds))
	var sb strings.Builder
	for _, d := range ds {
		sb.WriteString(fmt.Sprint(d))
	}
	return sb.String()
}

func brl(rows []clarian.Row) *big.Int {
	n := new(big.Int)
	for _, r := range rows {
		if r["currency"] == "BRL" && r["kind"] == "available" {
			n.SetString(fmt.Sprint(r["balance_minor"]), 10)
		}
	}
	return n
}

func str(r clarian.Row, k string) string { s, _ := r[k].(string); return s }

func main() {
	apiKey, ws := os.Getenv("CLARIAN_API_KEY"), os.Getenv("CLARIAN_WORKSPACE_ID")
	if !strings.HasPrefix(apiKey, "cl_test_sk_") || ws == "" {
		fmt.Fprintln(os.Stderr, "CLARIAN_API_KEY (cl_test_sk_…) and CLARIAN_WORKSPACE_ID are required")
		os.Exit(2)
	}
	var opts []clarian.Option
	if u := os.Getenv("CLARIAN_BASE_URL"); u != "" {
		opts = append(opts, clarian.WithBaseURL(u))
	}
	c, err := clarian.New(apiKey, ws, opts...)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(2)
	}
	specPath := os.Getenv("CLARIAN_SPEC")
	if specPath == "" {
		specPath = "../openapi.json"
	}
	raw, err := os.ReadFile(specPath)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(2)
	}
	var spec struct {
		Components struct {
			Schemas map[string]struct {
				Properties map[string]any `json:"properties"`
			} `json:"schemas"`
		} `json:"components"`
	}
	json.Unmarshal(raw, &spec)
	schemas = spec.Components.Schemas

	step("ping", func() (clarian.Row, error) { return c.Ping(ctx) })
	before, _ := step("balances.list", func() ([]clarian.Row, error) { return c.Balances.List(ctx) })

	// Cash-in
	payin, _ := step("pix.payins.create", func() (clarian.Row, error) {
		return c.Pix.Payins.Create(ctx, clarian.Row{"amount_minor": big.NewInt(10000), "currency": "BRL"}, key())
	}, "Payment")
	if id := str(payin, "id"); id != "" {
		step("pix.payins.retrieve", func() (clarian.Row, error) { return c.Pix.Payins.Retrieve(ctx, id) }, "Payment")
		step("pix.payins.simulate", func() (clarian.Row, error) { return c.Pix.Payins.Simulate(ctx, id, nil) }, "Payment")
	}
	afterIn, _ := step("balances after cash-in", func() ([]clarian.Row, error) { return c.Balances.List(ctx) })
	d := new(big.Int).Sub(brl(afterIn), brl(before))
	check("cash-in credited 100.00", d.Cmp(big.NewInt(10000)) == 0, d.String())

	// Cash-out
	step("pix.payouts.fee", func() (clarian.Row, error) { return c.Pix.Payouts.Fee(ctx, clarian.Row{"amount_minor": "1000"}) }, "PixFeeQuote")
	step("pix.payouts.lookup", func() (clarian.Row, error) {
		return c.Pix.Payouts.Lookup(ctx, clarian.Row{"pix_key": "maria@example.com"})
	}, "PixDictResult")
	step("pix.payouts.listAccounts", func() ([]clarian.Row, error) { return c.Pix.Payouts.ListAccounts(ctx) }, "PayoutAccount")
	idem := key()
	payoutBody := clarian.Row{"amount_minor": 1000, "currency": "BRL", "pix_key": "maria@example.com"}
	payout, _ := step("pix.payouts.create", func() (clarian.Row, error) { return c.Pix.Payouts.Create(ctx, payoutBody, idem) }, "Payment")
	replay, _ := step("pix.payouts.create replay", func() (clarian.Row, error) { return c.Pix.Payouts.Create(ctx, payoutBody, idem) })
	check("payout replay returns same payment", str(payout, "id") != "" && str(replay, "id") == str(payout, "id"), str(payout, "id")+" "+str(replay, "id"))
	if id := str(payout, "id"); id != "" {
		step("pix.payouts.retrieve", func() (clarian.Row, error) { return c.Pix.Payouts.Retrieve(ctx, id) }, "Payment")
		step("pix.payouts.simulate", func() (clarian.Row, error) { return c.Pix.Payouts.Simulate(ctx, id, nil) }, "Payment")
	}
	afterOut, _ := step("balances after cash-out", func() ([]clarian.Row, error) { return c.Balances.List(ctx) })
	d = new(big.Int).Sub(brl(afterIn), brl(afterOut))
	check("cash-out debited 10.00 + fee", d.Cmp(big.NewInt(1000)) > 0, d.String())

	// Quote and convert: pay 50.00 BRL for USDT
	step("conversions.prices", func() (clarian.Row, error) { return c.Conversions.Prices(ctx, "USDT") })
	quote, _ := step("conversions.quote", func() (clarian.Row, error) {
		return c.Conversions.Quote(ctx, clarian.Row{"base_currency": "BRL", "quote_currency": "USDT", "base_amount_minor": big.NewInt(5000)})
	}, "Quote")
	if id := str(quote, "id"); id != "" {
		exec, _ := step("conversions.execute", func() (clarian.Row, error) { return c.Conversions.Execute(ctx, id) })
		ledger, _ := exec["ledger"].(map[string]any)
		lb, _ := json.Marshal(ledger)
		check("convert debited BRL, credited USDT", ledger["from_currency"] == "BRL" && ledger["to_currency"] == "USDT", string(lb))
		expectError("conversions.execute twice", "quote_not_open", func() error { _, e := c.Conversions.Execute(ctx, id); return e })
	}

	// Wallets and crypto
	wallets, _ := step("wallets.list", func() ([]clarian.Row, error) { return c.Wallets.List(ctx) }, "Wallet")
	if len(wallets) > 0 {
		if id := str(wallets[0], "id"); id != "" {
			step("wallets.retrieve", func() (clarian.Row, error) { return c.Wallets.Retrieve(ctx, id) }, "Wallet")
			step("wallets.balance", func() (clarian.Row, error) { return c.Wallets.Balance(ctx, id) })
			step("wallets.transactions", func() ([]clarian.Row, error) { return c.Wallets.Transactions(ctx, id, clarian.Params{"limit": "5"}) }, "Payment")
		}
	}
	step("crypto.networks", func() ([]clarian.Row, error) { return c.Crypto.Networks(ctx) }, "PayableNetwork")

	// Ledger views
	step("transactions.list", func() ([]clarian.Row, error) { return c.Transactions.List(ctx, clarian.Params{"limit": "5"}) }, "Payment")
	if id := str(payin, "id"); id != "" {
		step("transactions.retrieve", func() (clarian.Row, error) { return c.Transactions.Retrieve(ctx, id) }, "Payment")
	}
	step("transactions.statement", func() ([]clarian.Row, error) { return c.Transactions.Statement(ctx, clarian.Params{"limit": "5"}) }, "StatementLine")

	// Customers and KYC
	customer, _ := step("customers.create", func() (clarian.Row, error) {
		return c.Customers.Create(ctx, clarian.Row{"kind": "person", "display_name": "SDK E2E", "document_type": "cpf", "document_number": cpf()})
	}, "Customer")
	step("customers.list", func() ([]clarian.Row, error) { return c.Customers.List(ctx) }, "Customer")
	if id := str(customer, "id"); id != "" {
		step("customers.retrieve", func() (clarian.Row, error) { return c.Customers.Retrieve(ctx, id) }, "Customer")
		step("customers.update", func() (clarian.Row, error) {
			return c.Customers.Update(ctx, id, clarian.Row{"display_name": "SDK E2E 2"})
		}, "Customer")
	}
	step("kyc.list", func() ([]clarian.Row, error) { return c.Kyc.List(ctx) }, "KycCase")

	// Webhooks
	hook, _ := step("webhooks.create", func() (clarian.Row, error) {
		return c.Webhooks.Create(ctx, clarian.Row{"url": "https://hooks.example.com/clarian", "events": []any{"payment.completed"}})
	}, "WebhookCreateResponse")
	secret := str(hook, "secret")
	check("webhook secret returned once", len(secret) > 20, map[bool]string{true: "present", false: "missing"}[secret != ""])
	if id := str(hook, "id"); id != "" {
		step("webhooks.retrieve", func() (clarian.Row, error) { return c.Webhooks.Retrieve(ctx, id) }, "Webhook")
		step("webhooks.update", func() (clarian.Row, error) {
			return c.Webhooks.Update(ctx, id, clarian.Row{"events": []any{"payment.completed", "payment.failed"}})
		}, "Webhook")
		step("webhooks.test", func() (clarian.Row, error) { return c.Webhooks.Test(ctx, id) })
		step("webhooks.list", func() ([]clarian.Row, error) { return c.Webhooks.List(ctx) }, "Webhook")
		step("webhooks.deliveries", func() ([]clarian.Row, error) { return c.Webhooks.Deliveries(ctx) }, "WebhookDelivery")
		step("webhooks.delete", func() (clarian.Row, error) { return c.Webhooks.Delete(ctx, id) })
	}
	expectError("webhooks.create private url", "invalid_webhook_url", func() error {
		_, e := c.Webhooks.Create(ctx, clarian.Row{"url": "https://169.254.169.254/x"})
		return e
	})

	// Payment links and checkout
	link, _ := step("paymentLinks.create", func() (clarian.Row, error) {
		return c.PaymentLinks.Create(ctx, clarian.Row{"description": "SDK E2E", "amount_minor": "2500", "currency": "BRL"})
	}, "PaymentLink")
	step("paymentLinks.list", func() ([]clarian.Row, error) { return c.PaymentLinks.List(ctx) }, "PaymentLink")
	if id := str(link, "id"); id != "" {
		step("paymentLinks.retrieve", func() (clarian.Row, error) { return c.PaymentLinks.Retrieve(ctx, id) }, "PaymentLink")
		step("paymentLinks.update", func() (clarian.Row, error) { return c.PaymentLinks.Update(ctx, id, clarian.Row{"title": "SDK E2E 2"}) }, "PaymentLink")
		step("paymentLinks.splits", func() ([]clarian.Row, error) { return c.PaymentLinks.Splits(ctx, id) }, "PaymentLinkSplit")
		step("paymentLinks.charge", func() (clarian.Row, error) {
			return c.PaymentLinks.Charge(ctx, id, clarian.Row{"payer_name": "Maria Silva", "payer_document": "52998224725"}, key())
		}, "CheckoutPaymentResult")
		if slug := str(link, "slug"); slug != "" {
			step("checkout.retrieve", func() (clarian.Row, error) { return c.Checkout.Retrieve(ctx, slug) }, "PublicCheckout")
		}
	}
	step("paymentLinks.payments", func() ([]clarian.Row, error) {
		p, err := c.PaymentLinks.Payments(ctx, clarian.Params{"limit": "5"})
		var items []clarian.Row
		if arr, ok := p["items"].([]any); ok {
			for _, x := range arr {
				if r, ok := x.(map[string]any); ok {
					items = append(items, r)
				}
			}
		}
		return items, err
	}, "Payment")
	step("paymentLinks.stats", func() (clarian.Row, error) { return c.PaymentLinks.Stats(ctx, nil) })

	// Sub-accounts
	sub, _ := step("subaccounts.create", func() (clarian.Row, error) {
		return c.Subaccounts.Create(ctx, clarian.Row{"display_name": fmt.Sprintf("SDK E2E %d", time.Now().UnixMilli())})
	}, "SubAccount")
	step("subaccounts.list", func() ([]clarian.Row, error) { return c.Subaccounts.List(ctx) }, "SubAccount")
	if id := str(sub, "id"); id != "" {
		step("subaccounts.retrieve", func() (clarian.Row, error) { return c.Subaccounts.Retrieve(ctx, id) }, "SubAccount")
		step("subaccounts.balances", func() ([]clarian.Row, error) { return c.Subaccounts.Balances(ctx, id) })
		step("subaccounts.transactions", func() ([]clarian.Row, error) { return c.Subaccounts.Transactions(ctx, id, nil) }, "Payment")
		step("forSubaccount().balances", func() ([]clarian.Row, error) { return c.ForSubaccount(id).Balances.List(ctx) })
	}

	// Workspace admin reads
	step("whitelist.list", func() ([]clarian.Row, error) { return c.Whitelist.List(ctx) }, "WhitelistEntry")
	step("whitelist.settings", func() (clarian.Row, error) { return c.Whitelist.Settings(ctx) }, "WhitelistSettings")
	step("bankAccounts.list", func() ([]clarian.Row, error) { return c.BankAccounts.List(ctx) }, "BankAccount")
	step("apiKeys.list", func() ([]clarian.Row, error) { return c.APIKeys.List(ctx) }, "ApiKey")
	step("apiKeys.audit", func() ([]clarian.Row, error) { return c.APIKeys.Audit(ctx, clarian.Params{"limit": "5"}) }, "ApiKeyAuditEntry")
	step("settings.retrieve", func() (clarian.Row, error) { return c.Settings.Retrieve(ctx) }, "WorkspaceSettings")
	step("notifications.list", func() ([]clarian.Row, error) { return c.Notifications.List(ctx) }, "Notification")
	step("team.list", func() ([]clarian.Row, error) { return c.Team.List(ctx) })
	step("workflows.list", func() ([]clarian.Row, error) { return c.Workflows.List(ctx) })
	step("workflows.runs", func() ([]clarian.Row, error) { return c.Workflows.Runs(ctx) })
	step("approvals.list", func() ([]clarian.Row, error) { return c.Approvals.List(ctx) })
	step("approvals.policy", func() (clarian.Row, error) { return c.Approvals.Policy(ctx) })
	step("treasury.list", func() ([]clarian.Row, error) { return c.Treasury.List(ctx) })

	// Client-side guards
	expectError("missing idempotency key", clarian.ErrIdempotencyKeyRequired.Error(), func() error {
		_, e := c.Pix.Payouts.Create(ctx, clarian.Row{"amount_minor": 1}, "")
		return e
	})

	failed := 0
	for _, o := range outcomes {
		s := "PASS"
		if !o.ok {
			s = "FAIL"
			failed++
		}
		fmt.Println(s, o.step, "|", o.note)
	}
	fmt.Println("\nSPEC DRIFT (keys returned but not in the schema):")
	for name, keys := range drift {
		ks := make([]string, 0, len(keys))
		for k := range keys {
			ks = append(ks, k)
		}
		sort.Strings(ks)
		fmt.Println(" ", name, strings.Join(ks, " "))
	}
	fmt.Printf("\n%d/%d passed\n", len(outcomes)-failed, len(outcomes))
	if failed > 0 {
		os.Exit(1)
	}
}
