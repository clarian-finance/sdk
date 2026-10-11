using Clarian;

namespace Clarian.Sdk.Tests;

public class WebhooksTests
{
    private const string Secret = "whsec_test";
    private const string Body = "{\"id\":\"evt_1\",\"type\":\"payment.completed\",\"created_at\":\"2026-10-10T00:00:00Z\",\"data\":{\"payment_id\":\"p_1\",\"amount_minor\":\"1000\"}}";

    private static string Now(int offsetSeconds = 0) => DateTimeOffset.UtcNow.AddSeconds(offsetSeconds).ToString("O");

    [Fact]
    public void ValidDeliveryReturnsEvent()
    {
        var ts = Now();
        var headers = new Dictionary<string, string>
        {
            ["X-Clarian-Signature"] = ClarianWebhooks.Sign(Body, ts, Secret),
            ["X-Clarian-Timestamp"] = ts,
        };
        var e = ClarianWebhooks.ConstructEvent(Body, headers, Secret);
        Assert.Equal("evt_1", e.Id);
        Assert.Equal("payment.completed", e.Type);
        Assert.Equal("2026-10-10T00:00:00Z", e.CreatedAt);
        Assert.Equal("p_1", e.Data.GetProperty("payment_id").GetString());
    }

    [Fact]
    public void SignatureIsHmacHex() =>
        // Reference: python hmac.new(b"whsec_test", b"2026-01-01T00:00:00Z.{}", sha256).hexdigest()
        Assert.Equal("b6c7dc7bc1c7496302b1940537fb02a6258d9a65fa2a76ffbdedf698385c7e4c", ClarianWebhooks.Sign("{}", "2026-01-01T00:00:00Z", Secret));

    [Fact]
    public void BadSignatureRejected()
    {
        var ts = Now();
        Assert.False(ClarianWebhooks.VerifySignature(Body, ClarianWebhooks.Sign(Body + " ", ts, Secret), ts, Secret));
        Assert.Throws<InvalidOperationException>(() => ClarianWebhooks.ConstructEvent(Body, "deadbeef", ts, Secret));
    }

    [Fact]
    public void StaleTimestampRejected()
    {
        var ts = Now(-301);
        Assert.False(ClarianWebhooks.VerifySignature(Body, ClarianWebhooks.Sign(Body, ts, Secret), ts, Secret));
    }

    [Fact]
    public void UnparseableTimestampRejected()
    {
        const string ts = "not-a-date";
        Assert.False(ClarianWebhooks.VerifySignature(Body, ClarianWebhooks.Sign(Body, ts, Secret), ts, Secret));
        Assert.False(ClarianWebhooks.VerifySignature(Body, ClarianWebhooks.Sign(Body, "", Secret), "", Secret));
    }
}
