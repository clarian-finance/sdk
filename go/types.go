package clarian

import "encoding/json"

// Amounts are BRL reais as JSON numbers. Use json.Number so callers control
// decimal handling (stdlib only — no money library).

// TxStatus is the lifecycle of a cash-in, cash-out, or ledger transaction.
type TxStatus string

const (
	TxStatusPending    TxStatus = "pending"
	TxStatusProcessing TxStatus = "processing"
	TxStatusCompleted  TxStatus = "completed"
	TxStatusFailed     TxStatus = "failed"
	TxStatusCancelled  TxStatus = "cancelled"
)

// PixKeyType is the PIX key kind for cash-out destinations.
type PixKeyType string

const (
	PixKeyCPF   PixKeyType = "CPF"
	PixKeyCNPJ  PixKeyType = "CNPJ"
	PixKeyEmail PixKeyType = "EMAIL"
	PixKeyPhone PixKeyType = "PHONE"
	PixKeyEVP   PixKeyType = "EVP"
)

// Environment is the API environment string returned by the gateway.
type Environment string

const (
	EnvProduction Environment = "production"
	EnvSandbox    Environment = "sandbox"
)

// Payer identifies the person or company expected to pay a cash-in.
type Payer struct {
	Name           string `json:"name"`
	DocumentNumber string `json:"document_number"`
}

// CashInRequest creates a dynamic PIX charge.
type CashInRequest struct {
	Amount      json.Number `json:"amount"`
	Payer       Payer       `json:"payer"`
	Description string      `json:"description,omitempty"`
	ExternalID  string      `json:"external_id,omitempty"`
}

// PixDetails holds the EMV payload and optional QR image for a cash-in.
type PixDetails struct {
	QRCode     *string `json:"qr_code"`
	CopyPaste  *string `json:"copy_paste"`
	EndToEndID *string `json:"end_to_end_id"`
}

// CashInOrder is a PIX cash-in order.
type CashInOrder struct {
	ID          string       `json:"id"`
	Status      TxStatus     `json:"status"`
	Amount      json.Number  `json:"amount"`
	Currency    string       `json:"currency"`
	Pix         PixDetails   `json:"pix"`
	Fee         *json.Number `json:"fee"` // null when not yet known
	Description *string      `json:"description"`
	ExpiresAt   *string      `json:"expires_at"`
	CreatedAt   *string      `json:"created_at"`
}

// CashInOrderResponse wraps a cash-in order.
type CashInOrderResponse struct {
	OK          bool        `json:"ok"`
	Environment Environment `json:"environment"`
	Order       CashInOrder `json:"order"`
}

// CashOutRequest sends BRL via PIX. With a registered settlement key, Amount
// alone is enough; otherwise set PixKey (and preferably PixKeyType).
type CashOutRequest struct {
	Amount      json.Number `json:"amount"`
	Description string      `json:"description,omitempty"`
	PixKey      string      `json:"pix_key,omitempty"`
	PixKeyType  PixKeyType  `json:"pix_key_type,omitempty"`
	ExternalID  string      `json:"external_id,omitempty"`
}

// CashOutOrder is a PIX cash-out order.
type CashOutOrder struct {
	ID          string       `json:"id"`
	Status      TxStatus     `json:"status"`
	Amount      json.Number  `json:"amount"`
	PixAmount   *json.Number `json:"pix_amount"` // null when not yet known
	Currency    string       `json:"currency"`
	Fee         *json.Number `json:"fee"` // null when not yet known
	FeeBearer   *string      `json:"fee_bearer"`
	EndToEndID  *string      `json:"end_to_end_id"`
	Description *string      `json:"description"`
	CreatedAt   *string      `json:"created_at"`
}

// CashOutOrderResponse wraps a cash-out order.
type CashOutOrderResponse struct {
	OK          bool         `json:"ok"`
	Environment Environment  `json:"environment"`
	Order       CashOutOrder `json:"order"`
}

// Balance is an available/pending/locked balance for one currency.
type Balance struct {
	Currency  string      `json:"currency"`
	Available json.Number `json:"available"`
	Pending   json.Number `json:"pending"`
	Locked    json.Number `json:"locked"`
}

// Transaction is a ledger entry (cash-in or cash-out).
type Transaction struct {
	ID          string      `json:"id"`
	Type        string      `json:"type"`
	Status      TxStatus    `json:"status"`
	Amount      json.Number `json:"amount"`
	Fee         json.Number `json:"fee"`
	Currency    string      `json:"currency"`
	Description *string     `json:"description"`
	ExternalID  *string     `json:"external_id"`
	Environment Environment `json:"environment"`
	CreatedAt   string      `json:"created_at"`
	UpdatedAt   string      `json:"updated_at"`
}

// ListTransactionsParams filters GET /transactions. Zero values are omitted.
type ListTransactionsParams struct {
	Type   string // pix_in | pix_out
	Status string // pending | processing | completed | failed | cancelled
	Limit  int
}

// PingResponse is returned by GET /ping.
type PingResponse struct {
	OK              bool        `json:"ok"`
	Environment     Environment `json:"environment"`
	MasterAccountID string      `json:"master_account_id"`
}

// WebhookInput creates or updates a webhook subscription.
type WebhookInput struct {
	URL         string   `json:"url"`
	Events      []string `json:"events"`
	Description string   `json:"description,omitempty"`
	IsActive    *bool    `json:"is_active,omitempty"`
}

// Webhook is a webhook subscription (secret is never re-returned after create).
// Nested under "subscription" on create/get/update responses from the gateway.
type Webhook struct {
	ID          string      `json:"id"`
	URL         string      `json:"url"`
	Events      []string    `json:"events"`
	IsActive    bool        `json:"is_active"`
	Description *string     `json:"description"`
	Environment Environment `json:"environment"`
	CreatedAt   string      `json:"created_at"`
	UpdatedAt   string      `json:"updated_at"`
}

// WebhookWithSecret is returned only from Webhooks.Create — store Secret now.
// On the wire, secret is a sibling of subscription, not nested inside it.
type WebhookWithSecret struct {
	Webhook
	Secret string
}

// EventData is the typed payload inside a verified webhook delivery.
// Unknown fields remain available via Event.RawData.
type EventData struct {
	TransactionID string      `json:"transaction_id"`
	ExternalID    string      `json:"external_id"`
	Amount        json.Number `json:"amount"`
	Fee           json.Number `json:"fee"`
	Currency      string      `json:"currency"`
	Status        string      `json:"status"`
}

// Event is a verified webhook envelope.
type Event struct {
	ID          string
	Type        string
	CreatedAt   string
	Environment string
	Data        EventData
	// RawData is the original "data" object for forward-compatible fields.
	RawData json.RawMessage
}
