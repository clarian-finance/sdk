using System.Net;
using System.Text.Json;

namespace Clarian.Sdk.Tests;

public class ClarianClientTests
{
    [Fact]
    public void EnvDetection_TestPrefix()
    {
        using var c = new ClarianClient("cl_test_sk_abc", "ws");
        Assert.Equal(ClarianClient.BaseUrlTest, c.BaseUrl);
    }

    [Fact]
    public void EnvDetection_LivePrefix()
    {
        using var c = new ClarianClient("cl_live_sk_abc", "ws");
        Assert.Equal(ClarianClient.BaseUrlLive, c.BaseUrl);
    }

    [Fact]
    public void EnvDetection_UnknownDefaultsLive()
    {
        using var c = new ClarianClient("other_key", "ws");
        Assert.Equal(ClarianClient.BaseUrlLive, c.BaseUrl);
    }

    [Fact]
    public void BaseUrlOverride_StripsTrailingSlash()
    {
        using var c = new ClarianClient("cl_test_sk_x", "ws", baseUrl: "http://localhost:9999/v1/");
        Assert.Equal("http://localhost:9999/v1", c.BaseUrl);
    }

    [Fact]
    public async Task AuthHeaders_OnEveryCall()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            FakeHttpMessageHandler.Json(200,
                """{"ok":true,"environment":"sandbox","master_account_id":"m1"}"""));
        using var c = Client("cl_test_sk_key", "ws-uuid", handler);

        var res = await c.PingAsync();
        Assert.True(res.Ok);
        Assert.Equal("GET", handler.Last.Method);
        Assert.Equal("/ping", handler.Last.AbsolutePath);
        Assert.Equal("Bearer cl_test_sk_key", handler.Last.Headers["Authorization"]);
        Assert.Equal("ws-uuid", handler.Last.Headers["X-Workspace-Id"]);
    }

    [Fact]
    public async Task CashInCreate_SendsIdempotencyAndWorkspace()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            FakeHttpMessageHandler.Json(201,
                """{"ok":true,"environment":"sandbox","order":{"id":"ord_1","status":"pending","amount":19.50,"currency":"BRL","pix":{"copy_paste":"brcode"}}}"""));
        using var c = Client("cl_test_sk_x", "ws-uuid", handler);

        var res = await c.CashIn.CreateAsync(
            new CashInRequest
            {
                Amount = 19.50m,
                Payer = new Payer { Name = "Ana", DocumentNumber = "52998224725" },
            },
            "pay-ext-1");

        Assert.Equal("ord_1", res.Order.Id);
        Assert.Equal("POST", handler.Last.Method);
        Assert.Equal("/cash-in/pix", handler.Last.AbsolutePath);
        Assert.Equal("pay-ext-1", handler.Last.Headers["Idempotency-Key"]);
        Assert.Equal("ws-uuid", handler.Last.Headers["X-Workspace-Id"]);

        using var sent = JsonDocument.Parse(handler.Last.Body);
        Assert.Equal(19.50m, sent.RootElement.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task CashOutCreate_SendsIdempotency()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            FakeHttpMessageHandler.Json(201,
                """{"ok":true,"order":{"id":"out_9","status":"processing","amount":10,"currency":"BRL"}}"""));
        using var c = Client("cl_test_sk_x", "ws", handler);

        await c.CashOut.CreateAsync(
            new CashOutRequest { Amount = 10m, PixKey = "abc", PixKeyType = "EVP" },
            "repasse-1");

        Assert.Equal("repasse-1", handler.Last.Headers["Idempotency-Key"]);
        Assert.Equal("/cash-out/pix", handler.Last.AbsolutePath);
    }

    [Fact]
    public async Task GetDoesNotSendIdempotencyKey()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            FakeHttpMessageHandler.Json(200,
                """{"ok":true,"order":{"id":"x","status":"pending","amount":1,"currency":"BRL"}}"""));
        using var c = Client("cl_test_sk_x", "ws", handler);

        await c.CashIn.RetrieveAsync("x");
        Assert.False(handler.Last.Headers.ContainsKey("Idempotency-Key"));
    }

    [Fact]
    public async Task CashInRetrieve_HappyPath()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            FakeHttpMessageHandler.Json(200,
                """{"ok":true,"order":{"id":"ord_paid","status":"completed","amount":1,"currency":"BRL"}}"""));
        using var c = Client("cl_live_sk_x", "ws", handler);

        var res = await c.CashIn.RetrieveAsync("ord_paid");
        Assert.Equal("completed", res.Order.Status);
        Assert.Equal("/cash-in/ord_paid", handler.Last.AbsolutePath);
    }

    [Fact]
    public async Task CashOutRetrieve_HappyPath()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            FakeHttpMessageHandler.Json(200,
                """{"ok":true,"order":{"id":"out_1","status":"completed","amount":1,"currency":"BRL"}}"""));
        using var c = Client("cl_test_sk_x", "ws", handler);

        var res = await c.CashOut.RetrieveAsync("out_1");
        Assert.Equal("out_1", res.Order.Id);
        Assert.Equal("/cash-out/out_1", handler.Last.AbsolutePath);
    }

    [Fact]
    public async Task CashOutDictCheck_HappyPath()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            FakeHttpMessageHandler.Json(200,
                """{"ok":true,"dict":{"name":"Maria","keyType":"EMAIL"}}"""));
        using var c = Client("cl_test_sk_x", "ws", handler);

        var res = await c.CashOut.DictCheckAsync("maria@example.com", "EMAIL");
        Assert.Equal("Maria", res.Dict.GetProperty("name").GetString());
        Assert.Equal("POST", handler.Last.Method);
        Assert.Equal("/pix/payouts/dict", handler.Last.AbsolutePath);

        using var sent = JsonDocument.Parse(handler.Last.Body);
        Assert.Equal("maria@example.com", sent.RootElement.GetProperty("pix_key").GetString());
        Assert.Equal("EMAIL", sent.RootElement.GetProperty("key_type").GetString());
    }

    [Fact]
    public async Task BalancesList_HappyPath()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            FakeHttpMessageHandler.Json(200,
                """{"ok":true,"balances":[{"currency":"BRL","available":100.50,"pending":0,"locked":0}]}"""));
        using var c = Client("cl_test_sk_x", "ws", handler);

        var bals = await c.Balances.ListAsync();
        Assert.Single(bals);
        Assert.Equal("BRL", bals[0].Currency);
        Assert.Equal("/account/balances", handler.Last.AbsolutePath);
    }

    [Fact]
    public async Task TransactionsListAndRetrieve_HappyPath()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.AbsolutePath == "/transactions")
            {
                Assert.Contains("type=pix_in", req.Uri.Query);
                Assert.Contains("limit=20", req.Uri.Query);
                return FakeHttpMessageHandler.Json(200,
                    """{"transactions":[{"id":"tx1","type":"pix_in","status":"completed","amount":10,"fee":0,"currency":"BRL"}]}""");
            }
            if (req.AbsolutePath == "/transactions/tx-99")
            {
                return FakeHttpMessageHandler.Json(200,
                    """{"transaction":{"id":"tx-99","type":"pix_in","status":"completed","amount":10,"fee":0.1,"currency":"BRL"}}""");
            }
            throw new InvalidOperationException("unexpected path " + req.AbsolutePath);
        });
        using var c = Client("cl_test_sk_x", "ws", handler);

        var list = await c.Transactions.ListAsync(new ListTransactionsParams { Type = "pix_in", Limit = 20 });
        Assert.Equal("tx1", list[0].Id);

        var tx = await c.Transactions.RetrieveAsync("tx-99");
        Assert.Equal("tx-99", tx.Id);
    }

    [Fact]
    public async Task WalletsListAndBalance_HappyPath()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.AbsolutePath == "/wallets")
            {
                return FakeHttpMessageHandler.Json(200,
                    """{"ok":true,"wallets":[{"id":"w1","network":"polygon","address":"0xabc"}]}""");
            }
            if (req.AbsolutePath == "/wallets/w1/balance")
            {
                return FakeHttpMessageHandler.Json(200,
                    """{"wallet_id":"w1","network":"polygon","address":"0xabc","balances":[{"currency":"USDT","amount":"1.5"}]}""");
            }
            throw new InvalidOperationException("unexpected path " + req.AbsolutePath);
        });
        using var c = Client("cl_test_sk_x", "ws", handler);

        var wallets = await c.Wallets.ListAsync();
        Assert.Equal("w1", wallets[0].Id);

        var bal = await c.Wallets.RetrieveBalanceAsync("w1");
        Assert.Equal("w1", bal.WalletId);
        Assert.Equal("USDT", bal.Balances[0].Currency);
    }

    [Fact]
    public async Task WebhooksCrud_HappyPath()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Method == "POST" && req.AbsolutePath == "/webhooks")
            {
                return FakeHttpMessageHandler.Json(201,
                    """{"ok":true,"subscription":{"id":"wh1","url":"https://ex.com","events":["pix_payin.completed"],"is_active":true},"secret":"whsec_abc"}""");
            }
            if (req.Method == "GET" && req.AbsolutePath == "/webhooks")
            {
                return FakeHttpMessageHandler.Json(200,
                    """{"subscriptions":[{"id":"wh1","url":"https://ex.com","events":["pix_payin.completed"],"is_active":true}]}""");
            }
            if (req.Method == "PATCH" && req.AbsolutePath == "/webhooks/wh1")
            {
                return FakeHttpMessageHandler.Json(200,
                    """{"subscription":{"id":"wh1","url":"https://ex.com/v2","events":["pix_payin.completed"],"is_active":false}}""");
            }
            if (req.Method == "DELETE" && req.AbsolutePath == "/webhooks/wh1")
            {
                return FakeHttpMessageHandler.Json(200, """{"ok":true}""");
            }
            throw new InvalidOperationException("unexpected " + req.Method + " " + req.AbsolutePath);
        });
        using var c = Client("cl_test_sk_x", "ws", handler);

        var created = await c.Webhooks.CreateAsync(new WebhookInput
        {
            Url = "https://ex.com",
            Events = new List<string> { "pix_payin.completed" },
        });
        Assert.Equal("whsec_abc", created.Secret);
        Assert.Equal("wh1", created.Id);

        Assert.Single(await c.Webhooks.ListAsync());

        var upd = await c.Webhooks.UpdateAsync("wh1", new WebhookInput
        {
            Url = "https://ex.com/v2",
            Events = new List<string> { "pix_payin.completed" },
            IsActive = false,
        });
        Assert.False(upd.IsActive);

        await c.Webhooks.DeleteAsync("wh1");
        Assert.Equal("DELETE", handler.Last.Method);
    }

    [Fact]
    public async Task ErrorMapping_JsonCodeAndMeta()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            FakeHttpMessageHandler.Json(402,
                """{"error":"insufficient_balance","detail":"saldo insuficiente","available":10,"requested":100}"""));
        using var c = Client("cl_test_sk_x", "ws", handler);

        var err = await Assert.ThrowsAsync<ClarianException>(() =>
            c.CashOut.CreateAsync(new CashOutRequest { Amount = 100 }, "idem"));

        Assert.Equal(402, err.StatusCode);
        Assert.Equal("insufficient_balance", err.Code);
        Assert.Equal("saldo insuficiente", err.Message);
        Assert.Equal(10L, Convert.ToInt64(err.Meta["available"]));
        Assert.Contains("insufficient_balance", err.ToString());
        Assert.Contains("insufficient_balance", ((Exception)err).Message);
    }

    [Fact]
    public async Task ErrorMapping_BodyTruncatedTo500()
    {
        var longBody = new string('x', 600);
        var handler = new FakeHttpMessageHandler((_, _) =>
            FakeHttpMessageHandler.Json(429, longBody));
        using var c = Client("cl_test_sk_x", "ws", handler);

        var err = await Assert.ThrowsAsync<ClarianException>(() => c.PingAsync());
        Assert.Equal(429, err.StatusCode);
        Assert.Equal("", err.Code);
        Assert.Equal(500, err.Message.Length);
        Assert.True(((Exception)err).Message.Length <= 500 + "HTTP 429: ".Length);
        Assert.DoesNotContain(new string('x', 600), ((Exception)err).Message);
        Assert.Equal(longBody, err.Body);
        Assert.NotNull(err.Meta);
    }

    private static ClarianClient Client(string key, string ws, FakeHttpMessageHandler handler) =>
        new(key, ws, baseUrl: "https://mock", handler: handler);
}
