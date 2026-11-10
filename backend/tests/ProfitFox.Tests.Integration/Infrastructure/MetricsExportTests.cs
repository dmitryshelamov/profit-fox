using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

using OpenTelemetry.Metrics;

using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.TradeFlow;
using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Infrastructure.Exchanges.Bybit;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>Requirement "Экспорт метрик по OTLP": with an address a service pushes its metrics there every export period, without one nowhere.</summary>
public class MetricsExportTests
{
    private const string MetricsPath = "/api/v1/otlp/v1/metrics";

    private ProfitFoxFactory _factory = null!;

    [SetUp]
    public async Task SetUp()
    {
        // The API migrates the database the Worker runs on.
        _factory = ProfitFoxFactory.Create(workers: 0);
        _factory.CreateClient().Dispose();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _factory.DisposeAsync();
    }

    // Scenario "Адрес задан": within two export periods of 1 s.
    [Test]
    public async Task Worker_pushes_its_bybit_requests()
    {
        using var receiver = new OtlpReceiver();
        await using WorkerHost worker = StartWorker(receiver.Endpoint);

        await InspectKeyAsync(worker);

        (await receiver.WaitAsync(body => Has(body, "profitfox.bybit.rest.requests") && Has(body, "worker"), TimeSpan.FromSeconds(2))).ShouldBeTrue();
        receiver.ContentTypes.ShouldAllBe(type => type == "application/x-protobuf");
    }

    // Requirement "Счётчик ликвидаций": the meter of the scanner is pushed with the others.
    [Test]
    public async Task Service_pushes_the_liquidations_of_the_scanner()
    {
        using var receiver = new OtlpReceiver();
        await using WorkerHost worker = StartWorker(receiver.Endpoint);

        new ScannerMetrics(worker.Services.GetRequiredService<IMeterFactory>()).Liquidated(new Dictionary<LiquidationSide, int> { [LiquidationSide.Long] = 1 });

        (await receiver.WaitAsync(body => Has(body, "profitfox.scanner.liquidations"), TimeSpan.FromSeconds(2))).ShouldBeTrue();
    }

    // Requirement "Счётчик сделок потока": the collector pushes its counter under the name of its service, trade-flow.
    [Test]
    public async Task Collector_pushes_its_trades_as_the_trade_flow_service()
    {
        using var receiver = new OtlpReceiver();
        await using FakeBybitSocketServer sockets = await FakeBybitSocketServer.StartAsync();
        await using FakeBybitArchiveServer archive = await FakeBybitArchiveServer.StartAsync();
        await using var collector = new TradeFlowHost(
            IntegrationEnvironment.ConnectionString,
            sockets,
            archive,
            TimeProvider.System,
            new Dictionary<string, string?> { ["Metrics:OtlpEndpoint"] = receiver.Endpoint, ["Metrics:ExportInterval"] = "00:00:01" });
        await collector.StartAsync();

        collector.Services.GetRequiredService<TradeFlowMetrics>().Traded(MarketCategory.Linear);

        (await receiver.WaitAsync(body => Has(body, "profitfox.tradeflow.trades") && Has(body, "trade-flow"), TimeSpan.FromSeconds(2))).ShouldBeTrue();
    }

    // Scenario "Адрес не задан": no setting, or an empty one.
    [TestCase(null)]
    [TestCase("")]
    public async Task Without_an_address_nothing_is_pushed(string? endpoint)
    {
        using var receiver = new OtlpReceiver();
        await using WorkerHost worker = StartWorker(endpoint);

        await InspectKeyAsync(worker);

        (await receiver.WaitAsync(_ => true, TimeSpan.FromSeconds(2))).ShouldBeFalse();
        worker.Services.GetService<MeterProvider>().ShouldBeNull();
    }

    // Scenario "Приёмник недоступен": a failed export changes nothing for the requests to Bybit.
    [Test]
    public async Task Unreachable_receiver_leaves_bybit_requests_as_they_are()
    {
        string closed = $"http://127.0.0.1:{FreePort()}{MetricsPath}";
        await using WorkerHost worker = StartWorker(closed);

        await InspectKeyAsync(worker);
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        (await InspectKeyAsync(worker)).Uid.ShouldBe(100);
    }

    // The account client keeps the handlers of the factory; only its primary handler is the fake.
    private WorkerHost StartWorker(string? endpoint)
    {
        Dictionary<string, string?> settings = endpoint is null ? [] : new Dictionary<string, string?>
        {
            ["Metrics:OtlpEndpoint"] = endpoint,
            ["Metrics:ExportInterval"] = "00:00:01"
        };

        return _factory.StartWorker(
            services => services.AddHttpClient<IExchangeKeyInspector, BybitAccountClient>().ConfigurePrimaryHttpMessageHandler(() => new FakeBybitHandler()),
            workerSettings: settings);
    }

    private static Task<ExchangeKeyInfo> InspectKeyAsync(WorkerHost worker)
    {
        return worker.Services.GetRequiredService<IExchangeKeyInspector>().InspectAsync("api-key", "secret", CancellationToken.None);
    }

    private static bool Has(byte[] body, string text)
    {
        return body.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)) >= 0;
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    /// <summary>A local stand-in of the OTLP receiver of Prometheus: it keeps the body and the content type of every POST.</summary>
    private sealed class OtlpReceiver : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly ConcurrentQueue<byte[]> _bodies = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<string> _contentTypes = new ConcurrentQueue<string>();
        private readonly Task _serving;

        public OtlpReceiver()
        {
            var address = $"http://127.0.0.1:{FreePort()}";
            _listener.Prefixes.Add(address + "/");
            _listener.Start();
            Endpoint = address + MetricsPath;
            _serving = ServeAsync();
        }

        public string Endpoint { get; }

        public IEnumerable<string> ContentTypes => _contentTypes;

        public async Task<bool> WaitAsync(Func<byte[], bool> match, TimeSpan timeout)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;

            while (DateTimeOffset.UtcNow < deadline)
            {
                if (_bodies.Any(match))
                {
                    return true;
                }

                await Task.Delay(50);
            }

            return _bodies.Any(match);
        }

        public void Dispose()
        {
            _listener.Stop();
            _serving.GetAwaiter().GetResult();
            _listener.Close();
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                using var body = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(body);

                if (context.Request.Url!.AbsolutePath == MetricsPath)
                {
                    _bodies.Enqueue(body.ToArray());
                    _contentTypes.Enqueue(context.Request.ContentType ?? "");
                }

                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.Close();
            }
        }
    }
}