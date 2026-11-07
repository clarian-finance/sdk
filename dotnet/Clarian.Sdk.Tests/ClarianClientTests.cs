using System.Net;
using System.Numerics;
using Clarian;

namespace Clarian.Sdk.Tests;

public class ClarianClientTests
{
    private const string Ws = "ws_1";

    private static (ClarianClient, FakeHttpMessageHandler) Make(Func<CapturedRequest, HttpResponseMessage> responder, string key = "cl_test_sk_abc")
    {
        var handler = new FakeHttpMessageHandler(responder);
        var client = new ClarianClient(new ClarianOptions
        {
            ApiKey = key, WorkspaceId = Ws, BaseUrl = "https://api.local/", HttpClient = new HttpClient(handler),
        });
        return (client, handler);
    }

    private static HttpResponseMessage Ok(string json = "{\"data\":{\"ok\":true}}") => FakeHttpMessageHandler.Json(200, json);

    private static HttpResponseMessage Unavailable()
    {
        var r = FakeHttpMessageHandler.Json(503, "{\"error\":\"unavailable\"}");
        r.Headers.TryAddWithoutValidation("Retry-After", "0");
        return r;
    }

    [Fact]
    public async Task BuildsTestUrlAndHeaders()
    {
        var (c, h) = Make(_ => Ok());
        await c.Transactions.ListAsync(limit: 5, before: "p_9");
        Assert.Equal("GET", h.Last.Method);
        Assert.Equal("https://api.local/v1/test/transactions?limit=5&before=p_9", h.Last.Uri.ToString());
        Assert.Equal("Bearer cl_test_sk_abc", h.Last.Headers["Authorization"]);
        Assert.Equal(Ws, h.Last.Headers["X-Workspace-Id"]);
        Assert.False(h.Last.Headers.ContainsKey("X-Subaccount-Id"));
        Assert.False(h.Last.Headers.ContainsKey("Idempotency-Key"));
        Assert.Equal(ClarianEnvironment.Sandbox, c.Environment);
    }

    [Fact]
    public async Task LiveKeyUsesLivePathAndSubaccountHeader()
    {
        var (c, h) = Make(_ => Ok(), "cl_live_sk_x");
        await c.ForSubaccount("sub 1").Balances.ListAsync();
        Assert.Equal("/v1/live/balances", h.Last.AbsolutePath);
        Assert.Equal("sub 1", h.Last.Headers["X-Subaccount-Id"]);
    }

    [Fact]
    public void RejectsBadKeyPrefix() =>
        Assert.Throws<ArgumentException>(() => new ClarianClient(new ClarianOptions { ApiKey = "sk_x", WorkspaceId = Ws }));

    [Fact]
    public async Task SendsIdempotencyKeyAndStringAmounts()
    {
        var (c, h) = Make(_ => Ok());
        await c.Pix.Payins.CreateAsync(new { amount_minor = BigInteger.Parse("10000"), currency = "BRL" }, "k-1");
        Assert.Equal("POST", h.Last.Method);
        Assert.Equal("/v1/test/pix/payins", h.Last.AbsolutePath);
        Assert.Equal("k-1", h.Last.Headers["Idempotency-Key"]);
        Assert.StartsWith("application/json", h.Last.Headers["Content-Type"]);
        Assert.Equal("{\"amount_minor\":\"10000\",\"currency\":\"BRL\"}", h.Last.Body);
    }

    [Fact]
    public async Task RejectsFloatAmounts()
    {
        var (c, h) = Make(_ => Ok());
        await Assert.ThrowsAsync<ArgumentException>(() => c.Pix.Payouts.FeeAsync(new { amount_minor = 10.5 }));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task UnwrapsData()
    {
        var (c, _) = Make(_ => Ok("{\"data\":[{\"currency\":\"BRL\",\"balance_minor\":\"1000\"}]}"));
        var rows = await c.Balances.ListAsync();
        Assert.Equal("1000", rows[0].GetProperty("balance_minor").GetString());
    }

    [Fact]
    public async Task MapsErrors()
    {
        var (c, _) = Make(_ => FakeHttpMessageHandler.Json(422, "{\"error\":\"insufficient_balance\",\"message\":\"Not enough\",\"needed\":\"5\"}"));
        var e = await Assert.ThrowsAsync<ClarianException>(() => c.Settings.RetrieveAsync());
        Assert.Equal(422, e.Status);
        Assert.Equal("insufficient_balance", e.Code);
        Assert.Equal("Not enough", e.Message);
        Assert.Equal("5", e.Meta["needed"].GetString());
    }

    [Fact]
    public async Task NonJsonErrorBody()
    {
        var (c, _) = Make(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>") });
        var e = await Assert.ThrowsAsync<ClarianException>(() => c.Webhooks.TestAsync("w"));
        Assert.Equal(502, e.Status);
        Assert.Equal("invalid_json_response", e.Code);
    }

    [Fact]
    public async Task RetriesGet()
    {
        var n = 0;
        var (c, h) = Make(_ => ++n < 3 ? Unavailable() : Ok());
        await c.Wallets.ListAsync();
        Assert.Equal(3, h.Requests.Count);
    }

    [Fact]
    public async Task GivesUpAfterMaxRetries()
    {
        var (c, h) = Make(_ => Unavailable());
        var e = await Assert.ThrowsAsync<ClarianException>(() => c.Wallets.ListAsync());
        Assert.Equal(503, e.Status);
        Assert.Equal(3, h.Requests.Count);
    }

    [Fact]
    public async Task RetriesNetworkErrorOnGet()
    {
        var n = 0;
        var (c, h) = Make(_ => ++n < 2 ? throw new HttpRequestException("reset") : Ok());
        await c.Balances.ListAsync();
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task DoesNotRetryPostWithoutKey()
    {
        var (c, h) = Make(_ => Unavailable());
        await Assert.ThrowsAsync<ClarianException>(() => c.Customers.CreateAsync(new { kind = "person" }));
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task RetriesPostWithKey()
    {
        var n = 0;
        var (c, h) = Make(_ => ++n < 2 ? Unavailable() : Ok());
        await c.Crypto.SendAsync(new { amount_minor = "1" }, "k-2");
        Assert.Equal(2, h.Requests.Count);
        Assert.All(h.Requests, r => Assert.Equal("k-2", r.Headers["Idempotency-Key"]));
    }

    [Fact]
    public async Task NeverRetriesExecute()
    {
        var (c, h) = Make(_ => Unavailable());
        await Assert.ThrowsAsync<ClarianException>(() => c.Conversions.ExecuteAsync("q_1"));
        Assert.Single(h.Requests);
        Assert.Equal("{\"quote_id\":\"q_1\"}", h.Last.Body);
        Assert.False(h.Last.Headers.ContainsKey("Idempotency-Key"));
    }

    [Fact]
    public async Task TradingCryptoWithdrawSendsKey()
    {
        var (c, h) = Make(_ => Ok());
        await c.Trading.CryptoWithdrawAsync(new { amount_minor = "1000000", currency = "USDT", network = "tron" }, "tw-1");
        Assert.Equal("POST", h.Last.Method);
        Assert.Equal("/v1/test/trading/crypto-withdrawals", h.Last.Path);
        Assert.Equal("tw-1", h.Last.Headers["Idempotency-Key"]);
    }

    [Fact]
    public async Task TradingDepositRequiresKey()
    {
        var (c, h) = Make(_ => Ok());
        await Assert.ThrowsAsync<ArgumentException>(() => c.Trading.DepositAsync(new { amount_minor = "1" }, ""));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task MoneyCreatesRequireIdempotencyKey()
    {
        var (c, h) = Make(_ => Ok());
        await Assert.ThrowsAsync<ArgumentException>(() => c.Pix.Payins.CreateAsync(new { amount_minor = 1 }, ""));
        await Assert.ThrowsAsync<ArgumentException>(() => c.Pix.Payouts.CreateAsync(new { amount_minor = 1 }, ""));
        await Assert.ThrowsAsync<ArgumentException>(() => c.Crypto.SendAsync(new { amount_minor = 1 }, ""));
        await Assert.ThrowsAsync<ArgumentException>(() => c.PaymentLinks.ChargeAsync("l", new { }, ""));
        await Assert.ThrowsAsync<ArgumentException>(() => c.Checkout.PayAsync("s", new { }, ""));
        Assert.Empty(h.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    public async Task DotAndEmptyIdsRefused(string id)
    {
        var (c, h) = Make(_ => Ok());
        await Assert.ThrowsAsync<ArgumentException>(() => c.Wallets.RetrieveAsync(id));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public void BlankSubaccountRefused()
    {
        var (c, _) = Make(_ => Ok());
        Assert.Throws<ArgumentException>(() => c.ForSubaccount(" "));
        Assert.Throws<ArgumentException>(() => new ClarianClient(new ClarianOptions { ApiKey = "cl_test_sk_x", WorkspaceId = Ws, SubaccountId = "" }));
    }

    [Theory]
    [InlineData("http://api.example.com", false)]
    [InlineData("http://localhost.evil.com", false)]
    [InlineData("ftp://x", false)]
    [InlineData("https://api.example.com", true)]
    [InlineData("http://localhost:3000", true)]
    [InlineData("http://127.0.0.1", true)]
    [InlineData("http://host.docker.internal:8080", true)]
    public void BaseUrlMustBeHttps(string url, bool ok)
    {
        var make = () => new ClarianClient(new ClarianOptions { ApiKey = "cl_test_sk_x", WorkspaceId = Ws, BaseUrl = url });
        if (ok) make(); else Assert.Throws<ArgumentException>(make);
    }

    private static HttpResponseMessage Pdf(byte[] bytes)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        r.Content.Headers.TryAddWithoutValidation("Content-Type", "application/pdf");
        return r;
    }

    [Fact]
    public async Task ReadsHistoryRoutesAndReturnsStatementPdfAsBytes()
    {
        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var n = 0;
        var (c, h) = Make(_ => n++ switch
        {
            0 => FakeHttpMessageHandler.Json(200, "{\"data\":[{\"day\":\"2026-11-01\",\"balance_minor\":\"100\"}]}"),
            1 => Pdf(pdf),
            2 => FakeHttpMessageHandler.Json(404, "{\"error\":\"statement_empty\"}"),
            _ => Ok("{\"data\":{\"items\":[]}}"),
        });
        var days = await c.Balances.HistoryAsync(days: 7, currency: "BRL");
        Assert.Equal("100", days[0].GetProperty("balance_minor").GetString());
        Assert.Equal("https://api.local/v1/test/balances/history?days=7&currency=BRL", h.Requests[0].Uri.ToString());

        var bytes = await c.Transactions.StatementPdfAsync("2026-11-01", "2026-11-07");
        Assert.Equal(pdf, bytes);
        Assert.Equal("https://api.local/v1/test/statement/pdf?from=2026-11-01&to=2026-11-07", h.Requests[1].Uri.ToString());

        var err = await Assert.ThrowsAsync<ClarianException>(() => c.Transactions.StatementPdfAsync("2026-11-01", "2026-11-07"));
        Assert.Equal(404, err.Status);
        Assert.Equal("statement_empty", err.Code);

        await c.PaymentLinks.ReplaceProductsAsync("link-1", new object[] { new { product_id = "p1", quantity = 2 } });
        Assert.Equal("PUT", h.Requests[3].Method);
        Assert.Equal("https://api.local/v1/test/payment-links/link-1/products", h.Requests[3].Uri.ToString());
        Assert.Equal("{\"items\":[{\"product_id\":\"p1\",\"quantity\":2}]}", h.Requests[3].Body);
    }

    [Fact]
    public async Task ReadsRemainingNewRoutesWithTheirQuery()
    {
        var (c, h) = Make(_ => Ok());
        await c.Transactions.DailyAsync(days: 3, source: "pix");
        Assert.Equal("https://api.local/v1/test/transactions/daily?days=3&source=pix", h.Last.Uri.ToString());
        await c.Conversions.PriceHistoryAsync("USDT", "BRL", days: 30);
        Assert.Equal("https://api.local/v1/test/prices/history?base=USDT&quote=BRL&days=30", h.Last.Uri.ToString());
        await c.PaymentLinks.ProductsAsync("link 1");
        Assert.Equal("/v1/test/payment-links/link%201/products", h.Last.AbsolutePath);
        Assert.Equal("GET", h.Last.Method);
        await c.ApiKeys.UsageAsync();
        Assert.Equal("https://api.local/v1/test/api-keys/usage", h.Last.Uri.ToString());
    }

    [Fact]
    public void ToStringMasksKey()
    {
        var o = new ClarianOptions { ApiKey = "cl_test_sk_secret", WorkspaceId = Ws };
        var c = new ClarianClient(o).ForSubaccount("sub");
        Assert.DoesNotContain("cl_test_sk_secret", o.ToString() + c.ToString());
    }
}
