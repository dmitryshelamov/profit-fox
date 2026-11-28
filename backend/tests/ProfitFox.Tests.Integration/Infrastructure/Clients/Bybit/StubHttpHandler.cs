using System.Net;

namespace ProfitFox.Tests.Integration.Infrastructure.Clients.Bybit;

/// <summary>
///     Bybit for the tests of its client (change add-exchange-accounts, design.md, decision 10): answers the requests from a
///     queue of prepared answers and records each request with its headers, so no request leaves the test.
/// </summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _answers = new();

    public List<(Uri Uri, Dictionary<string, string> Headers)> Requests { get; } = [];

    public StubHttpHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return Answer(() => new HttpResponseMessage(status) { Content = new StringContent(json) });
    }

    public StubHttpHandler Status(HttpStatusCode status)
    {
        return Answer(() => new HttpResponseMessage(status) { Content = new StringContent("") });
    }

    public StubHttpHandler Answer(Func<HttpResponseMessage> answer)
    {
        _answers.Enqueue(answer);
        return this;
    }

    public HttpClient Client()
    {
        return new HttpClient(this, disposeHandler: false);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.RequestUri!, request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase)));
        return Task.FromResult(_answers.Dequeue()());
    }
}
