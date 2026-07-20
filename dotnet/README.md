# Clarian.Sdk (.NET)

Official .NET SDK for the [Clarian Finance API](https://api.clarian.finance). PIX cash-in/cash-out, balances, wallets, transactions, and webhooks.

Package: `Clarian` namespace · NuGet package id: `Clarian.Sdk` · version **0.4.1** · **stdlib only** (HttpClient + System.Text.Json). Targets `net8.0`.

## Install

Until the package is published to NuGet, use one of:

### Local package source

```bash
cd dotnet
dotnet pack Clarian.Sdk -o ./nupkg
```

Then in your app:

```xml
<!-- Directory.Build.props or NuGet.config: add a local source pointing at ./nupkg -->
<PackageReference Include="Clarian.Sdk" Version="0.4.1" />
```

```bash
dotnet nuget add source /absolute/path/to/sdk/dotnet/nupkg --name clarian-local
dotnet add package Clarian.Sdk --version 0.4.1 --source clarian-local
```

### ProjectReference (monorepo)

```xml
<ItemGroup>
  <ProjectReference Include="..\path\to\sdk\dotnet\Clarian.Sdk\Clarian.Sdk.csproj" />
</ItemGroup>
```

## Quickstart

```csharp
using Clarian;

var client = new ClarianClient(
    Environment.GetEnvironmentVariable("CLARIAN_API_KEY")!,      // cl_live_sk_... or cl_test_sk_...
    Environment.GetEnvironmentVariable("CLARIAN_WORKSPACE_ID")!   // workspace UUID
);
// cl_test_sk_ -> sandbox; cl_live_sk_ (and any other prefix) -> production/live.
// Override with baseUrl: "...", timeout: TimeSpan.FromSeconds(60).

await client.PingAsync();

// Receive BRL via PIX (Idempotency-Key is required).
var charge = await client.CashIn.CreateAsync(
    new CashInRequest
    {
        Amount = 250.00m,
        Payer = new Payer
        {
            Name = "Maria Silva",
            DocumentNumber = "12345678900",
        },
        Description = "Order #1234",
    },
    "order-1234");
Console.WriteLine(charge.Order.Pix.CopyPaste);

// Send BRL via PIX (idempotency key required; retries never double-send).
var payout = await client.CashOut.CreateAsync(
    new CashOutRequest { Amount = 100.00m },
    "withdrawal-2026-07-01-001");
Console.WriteLine($"{payout.Order.Id} {payout.Order.Status}");

// Optional: preview a PIX key owner before paying out.
var info = await client.CashOut.DictCheckAsync("maria@example.com", "EMAIL");

var balances = await client.Balances.ListAsync();
```

Auth headers (set automatically): `Authorization: Bearer <key>` and `X-Workspace-Id: <uuid>`.

## Environments

| Key prefix       | Base URL                                                    |
|------------------|-------------------------------------------------------------|
| `cl_test_sk_…`   | `https://api.clarian.finance/functions/v1/api-gateway/test` |
| `cl_live_sk_…`   | `https://api.clarian.finance/functions/v1/api-gateway/live` |
| any other prefix | **live** (same as `cl_live_sk_…`); not rejected             |

Only the `cl_test_sk_` prefix selects sandbox. Unknown prefixes default to live so a mis-typed key can hit production; pass `baseUrl` when you need an explicit host.

## Error handling

Non-2xx responses throw `ClarianException` with `StatusCode`, `Code` (JSON `error` when present), `Message`, and `Meta`. The exception text truncates the raw body to 500 characters.

```csharp
try
{
    await client.CashOut.CreateAsync(new CashOutRequest { Amount = 100m }, "payout-inv-42");
}
catch (ClarianException e)
{
    Console.WriteLine($"{e.StatusCode} {e.Code} {e.Message}");
}
```

## Verifying webhooks

Deliveries are signed with **HMAC-SHA256** (hex) over `` `{X-Clarian-Timestamp}.{rawBody}` `` using the full `whsec_…` secret (prefix included; do not strip it). Reject timestamps older than **5 minutes** (replay protection). Comparison is constant-time.

```csharp
using Clarian;

// ASP.NET Core example
app.MapPost("/webhooks/clarian", async (HttpRequest req) =>
{
    using var reader = new StreamReader(req.Body);
    var rawBody = await reader.ReadToEndAsync();
    var headers = ClarianWebhooks.ExtractHeaders(
        req.Headers.ToDictionary(h => h.Key, h => (IEnumerable<string>?)h.Value));
    var secret = Environment.GetEnvironmentVariable("CLARIAN_WEBHOOK_SECRET")!; // whsec_...

    if (!ClarianWebhooks.VerifySignature(rawBody, headers.Timestamp, headers.Signature, secret))
        return Results.Unauthorized();

    // process JSON body...
    return Results.Ok();
});
```

For local handler tests, sign a payload the same way the server does:

```csharp
var sig = ClarianWebhooks.SignPayload(secret, timestamp, rawBody);
```

Create a subscription (secret shown **once**):

```csharp
var sub = await client.Webhooks.CreateAsync(new WebhookInput
{
    Url = "https://example.com/webhooks/clarian",
    Events = new List<string> { "pix_payin.completed", "pix_payout.completed" },
});
// store sub.Secret immediately; it is never returned again
Console.WriteLine($"{sub.Id} {sub.Secret}");
```

## Sandbox testing

Sandbox helpers live on `client.Sandbox`. They require a `cl_test_sk_…` key and throw `InvalidOperationException` for live keys **before** any HTTP call. Outside the test environment the gateway returns `404 {error:"sandbox_only"}`.

```csharp
// Advance a pending cash-in (empty/null status -> completed).
var order = await client.Sandbox.SimulateCashInAsync(charge.Order.Id, "completed");
// status: "completed" | "expired" | "failed"

// Advance a cash-out: "completed" | "failed"
await client.Sandbox.SimulateCashOutAsync(payout.Order.Id, "failed");

// Enqueue a sample webhook for a subscription.
var result = await client.Sandbox.SendWebhookEventAsync(
    sub.Id, SandboxService.EventPixPayinCompleted);
var newId = await client.Sandbox.ResendWebhookDeliveryAsync(result.DeliveryId);
```

### Magic PIX keys

| Constant / key              | Sandbox payout behavior                                  |
|-----------------------------|----------------------------------------------------------|
| `SandboxFailPixKey`         | `fail@sandbox.clarian` (fails + refund)                  |
| `SandboxPendingPixKey`      | `pending@sandbox.clarian` (stays pending until simulated) |

```csharp
await client.CashOut.CreateAsync(
    new CashOutRequest
    {
        Amount = 10.00m,
        PixKey = SandboxService.SandboxFailPixKey,
    },
    "payout-fail-1");
```

Sample event type constants: `EventPixPayinCreated`, `EventPixPayinCompleted`, `EventPixPayinExpired`, `EventPixPayoutCreated`, `EventPixPayoutCompleted`, `EventPixPayoutFailed`, `EventCheckoutPaid` (on `SandboxService`).

## API surface

| Method | Path | Client |
|--------|------|--------|
| `GET` | `/ping` | `client.PingAsync()` |
| `POST` | `/cash-in/pix` | `client.CashIn.CreateAsync(request, idempotencyKey)` |
| `GET` | `/cash-in/{id}` | `client.CashIn.RetrieveAsync(id)` |
| `POST` | `/pix/payouts/dict` | `client.CashOut.DictCheckAsync(pixKey, keyType)` |
| `POST` | `/cash-out/pix` | `client.CashOut.CreateAsync(request, idempotencyKey)` |
| `GET` | `/cash-out/{id}` | `client.CashOut.RetrieveAsync(id)` |
| `GET` | `/account/balances` | `client.Balances.ListAsync()` |
| `GET` | `/transactions` | `client.Transactions.ListAsync(...)` |
| `GET` | `/transactions/{id}` | `client.Transactions.RetrieveAsync(id)` |
| `GET` | `/wallets` | `client.Wallets.ListAsync(network)` |
| `GET` | `/wallets/{id}/balance` | `client.Wallets.RetrieveBalanceAsync(id)` |
| | webhooks CRUD | `client.Webhooks.CreateAsync/ListAsync/UpdateAsync/DeleteAsync` |
| | verify delivery | `ClarianWebhooks.VerifySignature` / `ExtractHeaders` / `SignPayload` |
| `POST` | `/pix/payins/{id}/simulate` | `client.Sandbox.SimulateCashInAsync(id, status)` |
| `POST` | `/pix/payouts/{id}/simulate` | `client.Sandbox.SimulateCashOutAsync(id, status)` |
| `POST` | `/webhooks/{id}/test` | `client.Sandbox.SendWebhookEventAsync(id, eventType)` |
| `POST` | `/webhooks/deliveries/{id}/resend` | `client.Sandbox.ResendWebhookDeliveryAsync(id)` |

## Development

```bash
cd dotnet
dotnet test
# or: ~/.dotnet/dotnet test
```
