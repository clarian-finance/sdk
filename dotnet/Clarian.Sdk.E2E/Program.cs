// End-to-end run of every SDK method against a sandbox API, plus a spec drift check. Mirrors e2e/sandbox.ts.
// Usage (from dotnet/): CLARIAN_API_KEY=cl_test_sk_... CLARIAN_WORKSPACE_ID=... [CLARIAN_BASE_URL=http://localhost:3000] dotnet run --project Clarian.Sdk.E2E
using System.Numerics;
using System.Text.Json;
using Clarian;

var apiKey = Environment.GetEnvironmentVariable("CLARIAN_API_KEY") ?? "";
var workspaceId = Environment.GetEnvironmentVariable("CLARIAN_WORKSPACE_ID") ?? "";
if (!apiKey.StartsWith("cl_test_sk_") || workspaceId == "")
{
    Console.Error.WriteLine("CLARIAN_API_KEY (cl_test_sk_...) and CLARIAN_WORKSPACE_ID are required");
    return 2;
}
var clarian = new ClarianClient(new ClarianOptions
{
    ApiKey = apiKey, WorkspaceId = workspaceId, BaseUrl = Environment.GetEnvironmentVariable("CLARIAN_BASE_URL"),
});

// openapi.json sits at the repo root, above dotnet/.
var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "openapi.json"))) dir = dir.Parent;
using var spec = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir?.FullName ?? ".", "openapi.json")));
var schemas = spec.RootElement.GetProperty("components").GetProperty("schemas");

var outcomes = new List<(string Step, bool Ok, string Note)>();
var drift = new SortedDictionary<string, SortedSet<string>>();

void CheckShape(string schema, JsonElement v)
{
    var props = schemas.TryGetProperty(schema, out var s) && s.TryGetProperty("properties", out var p) ? p : default;
    var rows = v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : new List<JsonElement> { v };
    foreach (var row in rows.Where(r => r.ValueKind == JsonValueKind.Object).Take(5))
        foreach (var k in row.EnumerateObject())
            if (props.ValueKind != JsonValueKind.Object || !props.TryGetProperty(k.Name, out _))
                (drift.TryGetValue(schema, out var set) ? set : drift[schema] = new()).Add("+" + k.Name);
}

string Describe(Exception e) => e is ClarianException ce ? $"{ce.Status} {ce.Code}" : $"{e.GetType().Name}: {e.Message}";

async Task<JsonElement?> Step(string name, Func<Task<JsonElement>> fn, string? schema = null)
{
    try
    {
        var v = await fn();
        if (schema is not null) CheckShape(schema, v);
        var raw = v.GetRawText();
        outcomes.Add((name, true, raw.Length > 140 ? raw[..140] : raw));
        return v;
    }
    catch (Exception e)
    {
        outcomes.Add((name, false, Describe(e)));
        return null;
    }
}

async Task ExpectError(string name, string code, Func<Task> fn)
{
    try
    {
        await fn();
        outcomes.Add((name, false, "accepted, expected " + code));
    }
    catch (Exception e)
    {
        var got = e is ClarianException ce ? ce.Code : e.Message;
        outcomes.Add((name, got == code, got));
    }
}

string Key() => Guid.NewGuid().ToString();
string? Str(JsonElement? v, string prop) =>
    v is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(prop, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
// A fresh valid CPF per run: the API rejects a document already on file.
string Cpf()
{
    var ds = Enumerable.Range(0, 9).Select(_ => Random.Shared.Next(10)).ToList();
    int Digit(List<int> d) => d.Select((x, i) => x * (d.Count + 1 - i)).Sum() * 10 % 11 % 10;
    ds.Add(Digit(ds));
    ds.Add(Digit(ds));
    return string.Concat(ds);
}
BigInteger Brl(JsonElement? rows) =>
    rows is { ValueKind: JsonValueKind.Array } a
        ? a.EnumerateArray().Where(r => Str(r, "currency") == "BRL" && Str(r, "kind") == "available")
            .Select(r => BigInteger.Parse(Str(r, "balance_minor") ?? "0")).FirstOrDefault()
        : BigInteger.Zero;

await Step("ping", () => clarian.PingAsync());
var before = await Step("balances.list", () => clarian.Balances.ListAsync());

// Cash-in
var payin = await Step("pix.payins.create", () => clarian.Pix.Payins.CreateAsync(new { amount_minor = new BigInteger(10000), currency = "BRL" }, Key()), "Payment");
var payinId = Str(payin, "id");
if (payinId is not null)
{
    await Step("pix.payins.retrieve", () => clarian.Pix.Payins.RetrieveAsync(payinId), "Payment");
    await Step("pix.payins.simulate", () => clarian.Pix.Payins.SimulateAsync(payinId), "Payment");
}
var afterIn = await Step("balances after cash-in", () => clarian.Balances.ListAsync());
outcomes.Add(("cash-in credited 100.00", Brl(afterIn) - Brl(before) == 10000, (Brl(afterIn) - Brl(before)).ToString()));

// Cash-out
await Step("pix.payouts.fee", () => clarian.Pix.Payouts.FeeAsync(new { amount_minor = "1000" }), "PixFeeQuote");
await Step("pix.payouts.lookup", () => clarian.Pix.Payouts.LookupAsync(new { pix_key = "maria@example.com" }), "PixDictResult");
await Step("pix.payouts.listAccounts", () => clarian.Pix.Payouts.ListAccountsAsync(), "PayoutAccount");
var idem = Key();
var payoutBody = new { amount_minor = 1000, currency = "BRL", pix_key = "maria@example.com" };
var payout = await Step("pix.payouts.create", () => clarian.Pix.Payouts.CreateAsync(payoutBody, idem), "Payment");
var replay = await Step("pix.payouts.create replay", () => clarian.Pix.Payouts.CreateAsync(payoutBody, idem));
var payoutId = Str(payout, "id");
outcomes.Add(("payout replay returns same payment", payoutId is not null && Str(replay, "id") == payoutId, $"{payoutId} {Str(replay, "id")}"));
if (payoutId is not null)
{
    await Step("pix.payouts.retrieve", () => clarian.Pix.Payouts.RetrieveAsync(payoutId), "Payment");
    await Step("pix.payouts.simulate", () => clarian.Pix.Payouts.SimulateAsync(payoutId), "Payment");
}
var afterOut = await Step("balances after cash-out", () => clarian.Balances.ListAsync());
outcomes.Add(("cash-out debited 10.00 + fee", Brl(afterIn) - Brl(afterOut) > 1000, (Brl(afterIn) - Brl(afterOut)).ToString()));

// Quote and convert: pay 50.00 BRL for USDT
await Step("conversions.prices", () => clarian.Conversions.PricesAsync(new[] { "USDT" }));
var quote = await Step("conversions.quote", () => clarian.Conversions.QuoteAsync(new { base_currency = "BRL", quote_currency = "USDT", base_amount_minor = new BigInteger(5000) }), "Quote");
var quoteId = Str(quote, "id");
if (quoteId is not null)
{
    var exec = await Step("conversions.execute", () => clarian.Conversions.ExecuteAsync(quoteId));
    JsonElement? ledger = exec is { ValueKind: JsonValueKind.Object } ex && ex.TryGetProperty("ledger", out var l) ? l : null;
    outcomes.Add(("convert debited BRL, credited USDT", Str(ledger, "from_currency") == "BRL" && Str(ledger, "to_currency") == "USDT", ledger?.GetRawText() ?? "{}"));
    await ExpectError("conversions.execute twice", "quote_not_open", () => clarian.Conversions.ExecuteAsync(quoteId));
}

// Wallets and crypto
var wallets = await Step("wallets.list", () => clarian.Wallets.ListAsync(), "Wallet");
var walletId = wallets is { ValueKind: JsonValueKind.Array } wl && wl.GetArrayLength() > 0 ? Str(wl[0], "id") : null;
if (walletId is not null)
{
    await Step("wallets.retrieve", () => clarian.Wallets.RetrieveAsync(walletId), "Wallet");
    await Step("wallets.balance", () => clarian.Wallets.BalanceAsync(walletId));
    await Step("wallets.transactions", () => clarian.Wallets.TransactionsAsync(walletId, limit: 5), "Payment");
}
await Step("crypto.networks", () => clarian.Crypto.NetworksAsync(), "PayableNetwork");

// Ledger views
await Step("transactions.list", () => clarian.Transactions.ListAsync(limit: 5), "Payment");
if (payinId is not null) await Step("transactions.retrieve", () => clarian.Transactions.RetrieveAsync(payinId), "Payment");
await Step("transactions.statement", () => clarian.Transactions.StatementAsync(limit: 5), "StatementLine");

// Customers and KYC
var customer = await Step("customers.create", () => clarian.Customers.CreateAsync(new { kind = "person", display_name = "SDK E2E", document_type = "cpf", document_number = Cpf() }), "Customer");
await Step("customers.list", () => clarian.Customers.ListAsync(), "Customer");
var customerId = Str(customer, "id");
if (customerId is not null)
{
    await Step("customers.retrieve", () => clarian.Customers.RetrieveAsync(customerId), "Customer");
    await Step("customers.update", () => clarian.Customers.UpdateAsync(customerId, new { display_name = "SDK E2E 2" }), "Customer");
}
await Step("kyc.list", () => clarian.Kyc.ListAsync(), "KycCase");

// Webhooks
var hook = await Step("webhooks.create", () => clarian.Webhooks.CreateAsync(new { url = "https://hooks.example.com/clarian", events = new[] { "payment.completed" } }), "WebhookCreateResponse");
var secret = Str(hook, "secret");
outcomes.Add(("webhook secret returned once", secret is { Length: > 20 }, secret is null ? "missing" : "present"));
var hookId = Str(hook, "id");
if (hookId is not null)
{
    await Step("webhooks.retrieve", () => clarian.Webhooks.RetrieveAsync(hookId), "Webhook");
    await Step("webhooks.update", () => clarian.Webhooks.UpdateAsync(hookId, new { events = new[] { "payment.completed", "payment.failed" } }), "Webhook");
    await Step("webhooks.test", () => clarian.Webhooks.TestAsync(hookId));
    await Step("webhooks.list", () => clarian.Webhooks.ListAsync(), "Webhook");
    await Step("webhooks.deliveries", () => clarian.Webhooks.DeliveriesAsync(), "WebhookDelivery");
    await Step("webhooks.delete", () => clarian.Webhooks.DeleteAsync(hookId));
}
await ExpectError("webhooks.create private url", "invalid_webhook_url", () => clarian.Webhooks.CreateAsync(new { url = "https://169.254.169.254/x" }));

// Payment links and checkout
var link = await Step("paymentLinks.create", () => clarian.PaymentLinks.CreateAsync(new { description = "SDK E2E", amount_minor = "2500", currency = "BRL" }), "PaymentLink");
await Step("paymentLinks.list", () => clarian.PaymentLinks.ListAsync(), "PaymentLink");
var linkId = Str(link, "id");
if (linkId is not null)
{
    await Step("paymentLinks.retrieve", () => clarian.PaymentLinks.RetrieveAsync(linkId), "PaymentLink");
    await Step("paymentLinks.update", () => clarian.PaymentLinks.UpdateAsync(linkId, new { title = "SDK E2E 2" }), "PaymentLink");
    await Step("paymentLinks.splits", () => clarian.PaymentLinks.SplitsAsync(linkId), "PaymentLinkSplit");
    await Step("paymentLinks.charge", () => clarian.PaymentLinks.ChargeAsync(linkId, new { payer_name = "Maria Silva", payer_document = "52998224725" }, Key()), "CheckoutPaymentResult");
    var slug = Str(link, "slug");
    if (slug is not null) await Step("checkout.retrieve", () => clarian.Checkout.RetrieveAsync(slug), "PublicCheckout");
}
await Step("paymentLinks.payments", async () => (await clarian.PaymentLinks.PaymentsAsync(limit: 5)).GetProperty("items"), "Payment");
await Step("paymentLinks.stats", () => clarian.PaymentLinks.StatsAsync());

// Sub-accounts
var sub = await Step("subaccounts.create", () => clarian.Subaccounts.CreateAsync(new { display_name = $"SDK E2E {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" }), "SubAccount");
await Step("subaccounts.list", () => clarian.Subaccounts.ListAsync(), "SubAccount");
var subId = Str(sub, "id");
if (subId is not null)
{
    await Step("subaccounts.retrieve", () => clarian.Subaccounts.RetrieveAsync(subId), "SubAccount");
    await Step("subaccounts.balances", () => clarian.Subaccounts.BalancesAsync(subId));
    await Step("subaccounts.transactions", () => clarian.Subaccounts.TransactionsAsync(subId), "Payment");
    await Step("forSubaccount().balances", () => clarian.ForSubaccount(subId).Balances.ListAsync());
}

// Workspace admin reads
await Step("whitelist.list", () => clarian.Whitelist.ListAsync(), "WhitelistEntry");
await Step("whitelist.settings", () => clarian.Whitelist.SettingsAsync(), "WhitelistSettings");
await Step("bankAccounts.list", () => clarian.BankAccounts.ListAsync(), "BankAccount");
await Step("apiKeys.list", () => clarian.ApiKeys.ListAsync(), "ApiKey");
await Step("apiKeys.audit", () => clarian.ApiKeys.AuditAsync(limit: 5), "ApiKeyAuditEntry");
await Step("settings.retrieve", () => clarian.Settings.RetrieveAsync(), "WorkspaceSettings");
await Step("notifications.list", () => clarian.Notifications.ListAsync(), "Notification");
await Step("team.list", () => clarian.Team.ListAsync());
await Step("workflows.list", () => clarian.Workflows.ListAsync());
await Step("workflows.runs", () => clarian.Workflows.RunsAsync());
await Step("approvals.list", () => clarian.Approvals.ListAsync());
await Step("approvals.policy", () => clarian.Approvals.PolicyAsync());
await Step("treasury.list", () => clarian.Treasury.ListAsync());

// Client-side guards
await ExpectError("missing idempotency key", "idempotencyKey is required: reuse the same key to retry safely (Parameter 'idempotencyKey')",
    () => clarian.Pix.Payouts.CreateAsync(new { amount_minor = 1 }, ""));

foreach (var o in outcomes) Console.WriteLine($"{(o.Ok ? "PASS" : "FAIL")} {o.Step} | {o.Note}");
Console.WriteLine("\nSPEC DRIFT (keys returned but not in the schema):");
foreach (var (name, keys) in drift) Console.WriteLine($"  {name} {string.Join(" ", keys)}");
var failed = outcomes.Count(o => !o.Ok);
Console.WriteLine($"\n{outcomes.Count - failed}/{outcomes.Count} passed");
return failed == 0 ? 0 : 1;
