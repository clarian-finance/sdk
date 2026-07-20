using System.Text;

namespace Clarian.Sdk.Tests;

public class WebhooksTests
{
    private readonly string _secret = "whsec_test_secret";
    private readonly byte[] _body;
    private readonly string _ts;

    public WebhooksTests()
    {
        _body = Encoding.UTF8.GetBytes(
            """{"id":"evt_1","type":"pix_payin.completed","created_at":"2026-01-01T00:00:00Z","environment":"sandbox","data":{"transaction_id":"ord_abc","status":"completed","amount":19.50,"fee":0.37,"currency":"BRL"}}""");
        _ts = DateTimeOffset.UtcNow.ToString("o");
    }

    [Fact]
    public void SignVerifyRoundtrip()
    {
        var sig = ClarianWebhooks.SignPayload(_secret, _ts, _body);
        Assert.True(ClarianWebhooks.VerifySignature(_body, _ts, sig, _secret));
        var sig2 = ClarianWebhooks.SignPayload(_secret, _ts, Encoding.UTF8.GetString(_body));
        Assert.True(ClarianWebhooks.VerifySignature(Encoding.UTF8.GetString(_body), _ts, sig2, _secret));
    }

    [Fact]
    public void TamperedBodyRejected()
    {
        var sig = ClarianWebhooks.SignPayload(_secret, _ts, _body);
        var tampered = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(_body).Replace("ord_abc", "ord_evil"));
        Assert.False(ClarianWebhooks.VerifySignature(tampered, _ts, sig, _secret));
    }

    [Fact]
    public void WrongSecretRejected()
    {
        var sig = ClarianWebhooks.SignPayload("whsec_wrong", _ts, _body);
        Assert.False(ClarianWebhooks.VerifySignature(_body, _ts, sig, _secret));
    }

    [Fact]
    public void StaleTimestampRejected()
    {
        var stale = DateTimeOffset.UtcNow.AddMinutes(-6).ToString("o");
        var sig = ClarianWebhooks.SignPayload(_secret, stale, _body);
        Assert.False(ClarianWebhooks.VerifySignature(_body, stale, sig, _secret));
    }

    [Fact]
    public void FutureTimestampBeyondToleranceRejected()
    {
        var future = DateTimeOffset.UtcNow.AddMinutes(6).ToString("o");
        var sig = ClarianWebhooks.SignPayload(_secret, future, _body);
        Assert.False(ClarianWebhooks.VerifySignature(_body, future, sig, _secret));
    }

    [Fact]
    public void CustomTolerance()
    {
        var old = DateTimeOffset.UtcNow.AddSeconds(-10).ToString("o");
        var sig = ClarianWebhooks.SignPayload(_secret, old, _body);
        Assert.False(ClarianWebhooks.VerifySignature(_body, old, sig, _secret, 5));
        Assert.True(ClarianWebhooks.VerifySignature(_body, old, sig, _secret, 60));
    }

    [Fact]
    public void MissingInputsRejected()
    {
        var sig = ClarianWebhooks.SignPayload(_secret, _ts, _body);
        Assert.False(ClarianWebhooks.VerifySignature(_body, _ts, sig, ""));
        Assert.False(ClarianWebhooks.VerifySignature(_body, "", sig, _secret));
        Assert.False(ClarianWebhooks.VerifySignature(_body, _ts, "", _secret));
    }

    [Fact]
    public void ExtractHeaders()
    {
        var headers = new Dictionary<string, string?>
        {
            [ClarianWebhooks.HeaderTimestamp] = _ts,
            [ClarianWebhooks.HeaderSignature] = "abc123",
            [ClarianWebhooks.HeaderEvent] = "pix_payin.completed",
        };
        var h = ClarianWebhooks.ExtractHeaders(headers);
        Assert.Equal(_ts, h.Timestamp);
        Assert.Equal("abc123", h.Signature);
        Assert.Equal("pix_payin.completed", h.Event);
    }

    [Fact]
    public void ExtractHeadersCaseInsensitive()
    {
        var h = ClarianWebhooks.ExtractHeaders(new Dictionary<string, string?>
        {
            ["x-clarian-timestamp"] = "t1",
            ["x-clarian-signature"] = "s1",
        });
        Assert.Equal("t1", h.Timestamp);
        Assert.Equal("s1", h.Signature);
    }

    [Fact]
    public void ExtractHeadersListValues()
    {
        var h = ClarianWebhooks.ExtractHeaders(new Dictionary<string, IEnumerable<string>?>
        {
            ["X-Clarian-Timestamp"] = new[] { "t2" },
            ["X-Clarian-Signature"] = new[] { "s2" },
        });
        Assert.Equal("t2", h.Timestamp);
        Assert.Equal("s2", h.Signature);
    }

    [Fact]
    public void UnixTimestampAccepted()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var sig = ClarianWebhooks.SignPayload(_secret, now, _body);
        Assert.True(ClarianWebhooks.VerifySignature(_body, now, sig, _secret));
    }
}
