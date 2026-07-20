using System.Text.Json;

namespace Clarian.Sdk.Tests;

public class SandboxTests
{
    [Fact]
    public async Task LiveKeyRejectedBeforeNetwork()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("unexpected HTTP call"));
        using var c = new ClarianClient("cl_live_sk_x", "ws", baseUrl: "https://mock", handler: handler);

        var e1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            c.Sandbox.SimulateCashInAsync("ord_1", "completed"));
        Assert.Contains("cl_test_sk_", e1.Message);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            c.Sandbox.SimulateCashOutAsync("out_1", "completed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            c.Sandbox.SendWebhookEventAsync("wh1", SandboxService.EventPixPayinCompleted));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            c.Sandbox.ResendWebhookDeliveryAsync("del_1"));
    }

    [Fact]
    public async Task InvalidStatusNoHttp()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("unexpected HTTP call"));
        using var c = Client(handler);

        var e1 = await Assert.ThrowsAsync<ArgumentException>(() =>
            c.Sandbox.SimulateCashInAsync("ord_1", "bogus"));
        Assert.Contains("invalid cash-in", e1.Message);

        var e2 = await Assert.ThrowsAsync<ArgumentException>(() =>
            c.Sandbox.SimulateCashOutAsync("out_1", "expired"));
        Assert.Contains("invalid cash-out", e2.Message);
    }

    [Fact]
    public async Task SimulateCashIn_HappyPath()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            FakeHttpMessageHandler.Json(200,
                """{"ok":true,"environment":"sandbox","order":{"id":"ord_1","status":"completed","amount":19.50,"currency":"BRL"}}"""));
        using var c = Client(handler);

        var res = await c.Sandbox.SimulateCashInAsync("ord_1", "completed");
        Assert.Equal("ord_1", res.Order.Id);
        Assert.Equal("POST", handler.Last.Method);
        Assert.Equal("/pix/payins/ord_1/simulate", handler.Last.AbsolutePath);
        Assert.Equal("Bearer cl_test_sk_x", handler.Last.Headers["Authorization"]);

        using var body = JsonDocument.Parse(handler.Last.Body);
        Assert.Equal("completed", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SimulateCashIn_DefaultStatus()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            FakeHttpMessageHandler.Json(200,
                """{"ok":true,"order":{"id":"ord_2","status":"completed","amount":1,"currency":"BRL"}}"""));
        using var c = Client(handler);

        await c.Sandbox.SimulateCashInAsync("ord_2");
        using var body = JsonDocument.Parse(handler.Last.Body);
        Assert.Equal("completed", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SimulateCashOut_HappyPath()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            FakeHttpMessageHandler.Json(200,
                """{"ok":true,"order":{"id":"out_1","status":"failed","amount":1,"currency":"BRL"}}"""));
        using var c = Client(handler);

        var res = await c.Sandbox.SimulateCashOutAsync("out_1", "failed");
        Assert.Equal("failed", res.Order.Status);
        Assert.Equal("/pix/payouts/out_1/simulate", handler.Last.AbsolutePath);
    }

    [Fact]
    public async Task SendWebhookEvent_HappyPath()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            Assert.Equal("/webhooks/wh1/test", req.AbsolutePath);
            return FakeHttpMessageHandler.Json(202,
                """{"ok":true,"environment":"sandbox","event_type":"pix_payin.completed","enqueued":{"enqueued":1,"delivery_id":"del_99"}}""");
        });
        using var c = Client(handler);

        var res = await c.Sandbox.SendWebhookEventAsync("wh1", SandboxService.EventPixPayinCompleted);
        Assert.Equal(SandboxService.EventPixPayinCompleted, res.EventType);
        Assert.Equal(1, res.Enqueued);
        Assert.Equal("del_99", res.DeliveryId);

        using var body = JsonDocument.Parse(handler.Last.Body);
        Assert.Equal("pix_payin.completed", body.RootElement.GetProperty("event_type").GetString());
    }

    [Fact]
    public async Task ResendWebhookDelivery_HappyPath()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            FakeHttpMessageHandler.Json(202, """{"ok":true,"delivery_id":"del_new"}"""));
        using var c = Client(handler);

        var newId = await c.Sandbox.ResendWebhookDeliveryAsync("del_old");
        Assert.Equal("del_new", newId);
        Assert.Equal("/webhooks/deliveries/del_old/resend", handler.Last.AbsolutePath);
        Assert.Equal("POST", handler.Last.Method);
    }

    [Fact]
    public void MagicPixKeyConstants()
    {
        Assert.Equal("fail@sandbox.clarian", SandboxService.SandboxFailPixKey);
        Assert.Equal("pending@sandbox.clarian", SandboxService.SandboxPendingPixKey);
    }

    [Fact]
    public async Task PathEscape()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            FakeHttpMessageHandler.Json(200,
                """{"ok":true,"order":{"id":"x","status":"completed","amount":1,"currency":"BRL"}}"""));
        using var c = Client(handler);

        await c.Sandbox.SimulateCashInAsync("a/b", "completed");
        Assert.Equal("/pix/payins/a%2Fb/simulate", handler.Last.AbsolutePath);
    }

    private static ClarianClient Client(FakeHttpMessageHandler handler) =>
        new("cl_test_sk_x", "ws", baseUrl: "https://mock", handler: handler);
}
