# Clarian.Sdk (.NET)

.NET SDK for the Clarian API v2: PIX cash-in and cash-out, BRL/stablecoin quotes and conversion, wallets and crypto sends, customers and KYC, payment links, sub-accounts and webhooks. Zero runtime dependencies (System.Text.Json only). .NET 8+.

## Install

```bash
dotnet add package Clarian.Sdk
```

Or reference `dotnet/Clarian.Sdk/Clarian.Sdk.csproj` from a clone of `github.com/clarian-finance/sdk`.

## Quick start

```csharp
using Clarian;

var clarian = new ClarianClient(new ClarianOptions
{
    ApiKey = Environment.GetEnvironmentVariable("CLARIAN_API_KEY")!, // cl_test_sk_... (sandbox) or cl_live_sk_... (live)
    WorkspaceId = Environment.GetEnvironmentVariable("CLARIAN_WORKSPACE_ID")!,
});

await clarian.PingAsync();
var balances = await clarian.Balances.ListAsync();
```

The key prefix picks the environment: `cl_test_sk_` calls `/v1/test`, `cl_live_sk_` calls `/v1/live`. Create keys in the dashboard under Negócios > API (needs 2FA). Give each key only the permissions it uses.

Request bodies are any JSON-serializable object with the snake_case fields of the OpenAPI reference (anonymous objects, dictionaries or your own records). Replies are the unwrapped `data` as a `JsonElement`; call `.Deserialize<T>()` to map them to your own types.

## Amounts

Every amount is an integer in minor units, sent and returned as a string. BRL has scale 2 (R$ 10,00 is `"1000"`), USDT and USDC scale 6 (1 USDT is `"1000000"`). Pass a `string`, `int`, `long` or `BigInteger`; a `double`, `float` or `decimal` in a body throws `ArgumentException`. Balances carry their `scale`.

```csharp
var brl = balances.EnumerateArray().First(b =>
    b.GetProperty("currency").GetString() == "BRL" && b.GetProperty("kind").GetString() == "available");
// brl.balance_minor = "1250050", brl.scale = 2 -> R$ 12.500,50
var minor = BigInteger.Parse(brl.GetProperty("balance_minor").GetString()!);
```

## Cash-in (receive PIX)

```csharp
var charge = await clarian.Pix.Payins.CreateAsync(new { amount_minor = "25000", currency = "BRL" }, "order-981");
var qr = charge.GetProperty("pix_qr_code").GetString(); // copia-e-cola for the payer
// later, or from the webhook:
var paid = await clarian.Pix.Payins.RetrieveAsync(charge.GetProperty("id").GetString()!);
```

The QR is valid for 30 minutes. The balance moves from `pending` to `available` when the PIX settles.

## Cash-out (send PIX)

```csharp
var fee = await clarian.Pix.Payouts.FeeAsync(new { amount_minor = "10000" }); // fee_minor, debit_minor
var payout = await clarian.Pix.Payouts.CreateAsync(
    new { amount_minor = "10000", currency = "BRL", pix_key = "settlement@yourcompany.com" },
    "withdrawal-2026-10-10-001");
```

Over the API, the money goes to your workspace's registered settlement key. Register it once in the dashboard; until then the call answers 403 `settlement_key_required`. Amount plus fee leave `available` at once and return if the PIX fails.

## Quote and convert

The direction is always `base_currency` -> `quote_currency`: you pay `base_amount_minor` of the base and receive the quote currency.

```csharp
// Buy USDT with R$ 500,00
var quote = await clarian.Conversions.QuoteAsync(new { base_currency = "BRL", quote_currency = "USDT", base_amount_minor = "50000" });
quote.GetProperty("quote_amount_minor"); // USDT you receive (scale 6), after fees
quote.GetProperty("expires_at");         // execute before this

var result = await clarian.Conversions.ExecuteAsync(quote.GetProperty("id").GetString()!);
// result.ledger.to_minor: settled amount; absent while result.status == "processing"
```

A quote converts once: a second execute is 409 `quote_not_open`, an expired one 410 `quote_expired`. `ExecuteAsync` is never retried automatically; after a lost reply, find the payment with `Transactions.ListAsync()`. To sell USDT for BRL, swap the currencies.

## Wallets and crypto

```csharp
var wallets = await clarian.Wallets.ListAsync();
await clarian.Wallets.BalanceAsync(wallets[0].GetProperty("id").GetString()!);
await clarian.Crypto.NetworksAsync();
// API keys send only to wallets registered in the dashboard whitelist (403 destination_not_whitelisted).
await clarian.Crypto.SendAsync(new { amount_minor = "5000000", currency = "USDT", network = "polygon", to_address = "0x..." }, "send-17");
```

## Customers, KYC and sub-accounts

```csharp
var customer = await clarian.Customers.CreateAsync(new
{
    kind = "person", display_name = "Maria Silva", document_type = "cpf", document_number = "52998224725",
});
await clarian.Kyc.CreateAsync(new { party_id = customer.GetProperty("id").GetString() });

var sub = await clarian.Subaccounts.CreateAsync(new { display_name = "Store 12" });
var store = clarian.ForSubaccount(sub.GetProperty("id").GetString()!); // every call acts on that sub-account
await store.Pix.Payins.CreateAsync(new { amount_minor = "1000" }, "store12-order-1");
```

## Payment links

```csharp
var link = await clarian.PaymentLinks.CreateAsync(new { amount_minor = "4990", currency = "BRL", description = "Plano mensal" });
var page = await clarian.PaymentLinks.PaymentsAsync(limit: 50); // { items, next_cursor }
```

## Webhooks

```csharp
var hook = await clarian.Webhooks.CreateAsync(new { url = "https://example.com/clarian", events = new[] { "payment.completed", "payment.failed" } });
var secret = hook.GetProperty("secret").GetString(); // shown only here: store it
await clarian.Webhooks.TestAsync(hook.GetProperty("id").GetString()!); // sandbox key: sends a signed test.ping
```

Verify every delivery on the raw body before trusting it:

```csharp
// ASP.NET Core minimal API. An empty secret throws ArgumentException.
var webhookSecret = Environment.GetEnvironmentVariable("CLARIAN_WEBHOOK_SECRET")
    ?? throw new InvalidOperationException("CLARIAN_WEBHOOK_SECRET is not set");
app.MapPost("/clarian", async (HttpRequest req) =>
{
    using var reader = new StreamReader(req.Body);
    var raw = await reader.ReadToEndAsync();
    WebhookEvent evt;
    try
    {
        evt = ClarianWebhooks.ConstructEvent(raw,
            req.Headers["x-clarian-signature"].ToString(), req.Headers["x-clarian-timestamp"].ToString(), webhookSecret);
    }
    catch (InvalidOperationException) { return Results.BadRequest(); }

    if (evt.Type == "payment.completed")
    {
        var paymentId = evt.Data.GetProperty("payment_id").GetString();
        var amount = evt.Data.GetProperty("amount_minor").GetString();
    }
    return Results.Ok();
});
```

`ConstructEvent` also takes any `IEnumerable<KeyValuePair<string, string>>` of headers (matched case-insensitively). The signature is HMAC-SHA256 hex of `{x-clarian-timestamp}.{rawBody}`; deliveries older than 5 minutes and unparseable timestamps are refused. Dedupe on `evt.Id` and answer 2xx fast. Failed deliveries are listed in `Webhooks.DeliveriesAsync()`. `Webhooks.TestAsync()` and `Webhooks.ResendAsync(id)` work with a sandbox key only; in live, reconcile missed events with `Transactions.ListAsync()`. `ClarianWebhooks.Sign` produces a valid signature for tests of your own handler.

## Sandbox

With a `cl_test_sk_` key nothing reaches a bank or chain. Settle or fail anything pending:

```csharp
await clarian.Pix.Payins.SimulateAsync(chargeId);                                     // completed
await clarian.Pix.Payouts.SimulateAsync(payoutId, new { status = "failed", reason = "test" });
await clarian.Crypto.SimulateAsync(sendId);
```

## Errors, retries and idempotency

Non-2xx replies throw `ClarianException` with `Status`, the API's stable `Code` and the other reply fields in `Meta`:

```csharp
try
{
    await clarian.Pix.Payouts.CreateAsync(new { amount_minor = "10000", pix_key = "x" }, "w-1");
}
catch (ClarianException e) when (e.Code == "insufficient_balance")
{
    // ...
}
```

Money-moving creates (`Pix.Payins.CreateAsync`, `Pix.Payouts.CreateAsync`, `Crypto.SendAsync`, `Trading.DepositAsync`, `Trading.WithdrawAsync`, `Trading.CryptoWithdrawAsync`, `PaymentLinks.ChargeAsync`, `Checkout.PayAsync`) require an idempotency key; an empty one throws `ArgumentException` before any request. Retrying with the same key returns the same payment and never pays twice. The SDK retries 429/502/503/504 and network errors up to `MaxRetries` (default 2), honoring `Retry-After`, but only reads, deletes and calls that carry an idempotency key. The API allows 120 requests per minute per key.

Options: `BaseUrl` (default `https://api.clarian.finance`), `Timeout` (default 30 s), `MaxRetries`, `SubaccountId`, `HttpClient` (bring your own, e.g. from `IHttpClientFactory`). Every method takes an optional `CancellationToken`.

## Not on the API

Actions that need a person's own second factor stay in the dashboard: inviting or changing team members, voting on approvals, creating or rotating API keys, saving payout accounts and whitelist entries, and changing workspace settings. Their read endpoints are in the SDK.

## Reference

Every method is `async` and ends in `Async`.

| Area | Methods |
|---|---|
| `Balances` | `List` |
| `Pix.Payins` | `Create`, `Retrieve`, `Simulate` |
| `Pix.Payouts` | `Create`, `Retrieve`, `Fee`, `Lookup`, `Simulate`, `ListAccounts` |
| `Conversions` | `Quote`, `Execute`, `Prices` |
| `Wallets` | `List`, `Create`, `Retrieve`, `Balance`, `Transactions` |
| `Crypto` | `Send`, `Simulate`, `Networks` |
| `Trading` | `Summary`, `Deposit`, `Withdraw`, `CryptoWithdraw` |
| `Transactions` | `List`, `Retrieve`, `Statement` |
| `Customers` | `List`, `Create`, `Retrieve`, `Update` |
| `Kyc` | `List`, `Create`, `Retrieve`, `ListRfi`, `AnswerRfi` |
| `Webhooks` | `List`, `Create`, `Retrieve`, `Update`, `Delete`, `Test`, `Deliveries`, `Resend` |
| `PaymentLinks` | `List`, `Create`, `Retrieve`, `Update`, `Delete`, `Payments`, `Stats`, `Splits`, `ReplaceSplits`, `Charge` |
| `Checkout` | `Retrieve`, `Pay` |
| `Subaccounts` | `List`, `Create`, `Retrieve`, `Balances`, `Transactions` |
| `Whitelist`, `BankAccounts`, `ApiKeys`, `Settings`, `Notifications`, `Team`, `Approvals` | read |
| `Workflows`, `Treasury` | `List`, `Create` (+ `Workflows.Runs`) |

Field names follow the OpenAPI reference (`openapi.json` at the repo root, also at `https://app.clarian.finance/openapi.json`).

## Development

```bash
cd dotnet
dotnet test
CLARIAN_API_KEY=cl_test_sk_... CLARIAN_WORKSPACE_ID=... CLARIAN_BASE_URL=http://localhost:3000 dotnet run --project Clarian.Sdk.E2E
```
