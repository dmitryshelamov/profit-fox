using System.Diagnostics.Metrics;
using System.Net;
using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

using ProfitFox.Infrastructure.Exchanges.Bybit;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Bybit;

/// <summary>Requirements "Счётчик запросов к Bybit REST" and "Исход «лимит Bybit»": every HTTP attempt to Bybit REST is counted once.</summary>
public class BybitRestMetricsHandlerTests
{
    private ServiceProvider _services = null!;
    private MetricCollector<long> _requests = null!;
    private BybitLimitHeadroom _headroom = null!;

    // The provider owns and disposes the factory.
    private IMeterFactory Meters => _services.GetRequiredService<IMeterFactory>();

    [SetUp]
    public void SetUp()
    {
        _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _requests = new MetricCollector<long>(Meters, "ProfitFox.Bybit", "profitfox.bybit.rest.requests");
        _headroom = new BybitLimitHeadroom(Meters);
    }

    [TearDown]
    public void TearDown()
    {
        _requests.Dispose();
        _services.Dispose();
    }

    [Test]
    public async Task Kline_request_is_counted_by_its_path()
    {
        using StubHttpHandler stub = new StubHttpHandler().RespondJson("""{"retCode":0,"retMsg":"OK","result":{},"retExtInfo":{},"time":0}""");

        await SendAsync(stub, "https://api.bybit.com/v5/market/kline?category=linear&symbol=BTCUSDT&interval=1");

        ShouldBeCounted("/v5/market/kline", 200, "ok");
    }

    [Test]
    public async Task Failed_answer_is_an_error()
    {
        using StubHttpHandler stub = new StubHttpHandler().Respond(HttpStatusCode.BadGateway);

        await SendAsync(stub, "https://api.bybit.com/v5/market/tickers?category=linear");

        ShouldBeCounted("/v5/market/tickers", 502, "error");
    }

    // 403 is a ban of the IP.
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    public async Task Refusal_over_the_limit_is_rate_limited(HttpStatusCode status)
    {
        using StubHttpHandler stub = new StubHttpHandler().Respond(status);

        await SendAsync(stub, "https://api.bybit.com/v5/market/tickers?category=linear");

        ShouldBeCounted("/v5/market/tickers", (int)status, "rate_limited");
    }

    // The limit of an account on an endpoint comes as a code in an OK answer.
    [Test]
    public async Task Code_10006_in_an_ok_answer_is_rate_limited()
    {
        using StubHttpHandler stub = new StubHttpHandler().RespondJson("""{"retCode":10006,"retMsg":"Too many visits!","result":{},"retExtInfo":{},"time":0}""");

        await SendAsync(stub, "https://api.bybit.com/v5/order/create");

        ShouldBeCounted("/v5/order/create", 200, "rate_limited");
    }

    // 170131: not enough funds.
    [Test]
    public async Task Other_code_in_an_ok_answer_is_an_error()
    {
        using StubHttpHandler stub = new StubHttpHandler().RespondJson("""{"retCode":170131,"retMsg":"Insufficient balance.","result":{},"retExtInfo":{},"time":0}""");

        await SendAsync(stub, "https://api.bybit.com/v5/order/create");

        ShouldBeCounted("/v5/order/create", 200, "error");
    }

    // The outcome then goes by the HTTP code alone.
    [Test]
    public async Task Ok_answer_that_is_not_json_is_ok()
    {
        using StubHttpHandler stub = new StubHttpHandler().RespondJson("<html>maintenance</html>");

        await SendAsync(stub, "https://api.bybit.com/v5/market/time");

        ShouldBeCounted("/v5/market/time", 200, "ok");
    }

    [TestCase("""{"result":{}}""")]
    [TestCase("[1]")]
    public async Task Ok_answer_without_a_code_is_ok(string json)
    {
        using StubHttpHandler stub = new StubHttpHandler().RespondJson(json);

        await SendAsync(stub, "https://api.bybit.com/v5/market/time");

        ShouldBeCounted("/v5/market/time", 200, "ok");
    }

    // Not Bybit's form: the request itself must not fail because of the metrics.
    [TestCase("""{"retCode":"10006"}""")]
    [TestCase("""{"retCode":null}""")]
    [TestCase("""{"retCode":1.5}""")]
    public async Task Code_that_is_not_a_whole_number_is_no_code(string json)
    {
        using StubHttpHandler stub = new StubHttpHandler().RespondJson(json);

        await SendAsync(stub, "https://api.bybit.com/v5/order/create");

        ShouldBeCounted("/v5/order/create", 200, "ok");
    }

    // A timeout or a broken connection: the attempt is counted without a code, and the caller sees the same exception.
    [Test]
    public async Task Attempt_without_an_answer_is_an_error_without_a_code()
    {
        var failure = new HttpRequestException("connection reset");
        using StubHttpHandler stub = new StubHttpHandler().Respond(() => throw failure);

        HttpRequestException thrown = await Should.ThrowAsync<HttpRequestException>(() => SendAsync(stub, "https://api.bybit.com/v5/market/tickers?category=linear"));

        thrown.ShouldBeSameAs(failure);
        ShouldBeCounted("/v5/market/tickers", null, "error");
    }

    // Such as the export of the metrics themselves.
    [Test]
    public async Task Request_to_another_host_is_not_counted()
    {
        using StubHttpHandler stub = new StubHttpHandler().Respond(HttpStatusCode.OK);

        await SendAsync(stub, "http://192.168.8.142:9090/api/v1/otlp/v1/metrics");

        _requests.GetMeasurementSnapshot().ShouldBeEmpty();
    }

    // The end-to-end stub: the configured address is Bybit, the live one is then another host.
    [Test]
    public async Task Configured_address_is_counted_by_scheme_host_and_port()
    {
        var settings = new BybitSettings("http://127.0.0.1:5296");
        using StubHttpHandler stub = new StubHttpHandler()
            .RespondJson("""{"retCode":0}""")
            .Respond(HttpStatusCode.OK)
            .Respond(HttpStatusCode.OK)
            .Respond(HttpStatusCode.OK);

        await SendAsync(stub, "http://127.0.0.1:5296/v5/market/instruments-info?category=spot", settings);
        await SendAsync(stub, "http://127.0.0.1:5297/v5/market/time", settings);
        await SendAsync(stub, "https://127.0.0.1:5296/v5/market/time", settings);
        await SendAsync(stub, "https://api.bybit.com/v5/market/time", settings);

        ShouldBeCounted("/v5/market/instruments-info", 200, "ok");
    }

    // Decision 4: the gauges give the last limit and the lowest remainder since the previous collection.
    [Test]
    public async Task Limit_headers_reach_the_gauges()
    {
        using var limits = new MetricCollector<int>(Meters, "ProfitFox.Bybit", "profitfox.bybit.rest.limit");
        using var remaining = new MetricCollector<int>(Meters, "ProfitFox.Bybit", "profitfox.bybit.rest.limit_remaining");
        using var stub = new StubHttpHandler();

        foreach (string left in new[] { "9", "4", "7" })
        {
            stub.Respond(() =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"retCode":0}""") };
                response.Headers.Add("X-Bapi-Limit", "10");
                response.Headers.Add("X-Bapi-Limit-Status", left);

                return response;
            });
            await SendAsync(stub, "https://api.bybit.com/v5/order/create");
        }

        remaining.RecordObservableInstruments();
        limits.RecordObservableInstruments();

        limits.LastMeasurement.ShouldNotBeNull().Value.ShouldBe(10);
        limits.LastMeasurement.Tags.ShouldBe(new Dictionary<string, object?> { ["bybit.endpoint"] = "/v5/order/create" });
        remaining.LastMeasurement.ShouldNotBeNull().Value.ShouldBe(4);
        remaining.LastMeasurement.Tags.ShouldBe(new Dictionary<string, object?> { ["bybit.endpoint"] = "/v5/order/create" });
    }

    // A network answer is read once; the SDK reads it as a stream after the handler has looked for the code.
    [Test]
    public async Task Body_is_read_whole_after_the_handler()
    {
        const string json = """{"retCode":0,"retMsg":"OK","result":{"list":[]},"retExtInfo":{},"time":0}""";
        using StubHttpHandler stub = new StubHttpHandler().Respond(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ForwardOnlyStream(Encoding.UTF8.GetBytes(json)))
        });

        using HttpResponseMessage response = await SendAsync(stub, "https://api.bybit.com/v5/market/kline?category=linear&symbol=BTCUSDT&interval=1");

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        (await reader.ReadToEndAsync()).ShouldBe(json);
    }

    private async Task<HttpResponseMessage> SendAsync(StubHttpHandler stub, string url, BybitSettings? settings = null)
    {
        using var invoker = new HttpMessageInvoker(new BybitRestMetricsHandler(Meters, settings ?? new BybitSettings(), _headroom) { InnerHandler = stub }, disposeHandler: false);

        return await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), CancellationToken.None);
    }

    private void ShouldBeCounted(string endpoint, int? status, string outcome)
    {
        CollectedMeasurement<long> measurement = _requests.GetMeasurementSnapshot().ShouldHaveSingleItem();
        measurement.Value.ShouldBe(1);
        var tags = new Dictionary<string, object?> { ["bybit.endpoint"] = endpoint, ["bybit.outcome"] = outcome };

        if (status is not null)
        {
            tags["http.response.status_code"] = status;
        }

        measurement.Tags.ShouldBe(tags, ignoreOrder: true);
    }

    private sealed class ForwardOnlyStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override bool CanSeek => false;
    }
}