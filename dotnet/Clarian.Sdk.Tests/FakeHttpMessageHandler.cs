using System.Net;
using System.Text;

namespace Clarian.Sdk.Tests;

internal sealed class CapturedRequest
{
    public required string Method { get; init; }
    public required Uri Uri { get; init; }
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
    public string Body { get; init; } = "";

    public string Path => Uri.PathAndQuery.Contains('?')
        ? Uri.AbsolutePath + Uri.Query
        : Uri.AbsolutePath;

    public string AbsolutePath => Uri.AbsolutePath;
}

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;
    private readonly List<CapturedRequest> _requests = new();

    public FakeHttpMessageHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public FakeHttpMessageHandler(Func<CapturedRequest, HttpResponseMessage> responder)
        : this((req, body) =>
        {
            var captured = Capture(req, body);
            return responder(captured);
        })
    {
    }

    public IReadOnlyList<CapturedRequest> Requests => _requests;
    public CapturedRequest Last => _requests[^1];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? ""
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var captured = Capture(request, body);
        _requests.Add(captured);
        return _responder(request, body);
    }

    private static CapturedRequest Capture(HttpRequestMessage request, string body)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in request.Headers)
            headers[h.Key] = string.Join(",", h.Value);
        if (request.Content is not null)
        {
            foreach (var h in request.Content.Headers)
                headers[h.Key] = string.Join(",", h.Value);
        }

        return new CapturedRequest
        {
            Method = request.Method.Method,
            Uri = request.RequestUri!,
            Headers = headers,
            Body = body,
        };
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    public static HttpResponseMessage Json(int status, string json) =>
        Json((HttpStatusCode)status, json);
}
