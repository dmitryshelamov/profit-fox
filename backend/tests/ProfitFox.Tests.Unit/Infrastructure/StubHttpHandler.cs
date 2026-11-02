using System.Net;

namespace ProfitFox.Tests.Unit.Infrastructure;

/// <summary>Answers requests from a queue of prepared responses and records what was asked.</summary>
public class StubHttpHandler(TimeProvider? clock = null) : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new Queue<Func<HttpResponseMessage>>();

    public List<Uri> Requests { get; } = [];

    public List<DateTimeOffset> RequestTimes { get; } = [];

    /// <summary>The body of each request that has one, in order.</summary>
    public List<string> Bodies { get; } = [];

    public StubHttpHandler RespondJson(string json)
    {
        return Respond(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    public StubHttpHandler Respond(HttpStatusCode status)
    {
        return Respond(() => new HttpResponseMessage(status));
    }

    public StubHttpHandler Respond(Func<HttpResponseMessage> response)
    {
        _responses.Enqueue(response);

        return this;
    }

    public HttpClient CreateClient()
    {
        return new HttpClient(this) { BaseAddress = new Uri("https://bybit.test") };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        RequestTimes.Add((clock ?? TimeProvider.System).GetUtcNow());

        if (request.Content is not null)
        {
            Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        }

        return _responses.Dequeue()();
    }
}