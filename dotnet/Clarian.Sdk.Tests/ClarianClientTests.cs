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

    [Fact]
    public void ToStringMasksKey()
    {
        var o = new ClarianOptions { ApiKey = "cl_test_sk_secret", WorkspaceId = Ws };
        var c = new ClarianClient(o).ForSubaccount("sub");
        Assert.DoesNotContain("cl_test_sk_secret", o.ToString() + c.ToString());
    }
}
