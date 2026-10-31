using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;

using NSubstitute;

using ProfitFox.Tests.Integration.Infrastructure;

using Serilog.Core;
using Serilog.Events;

using Shouldly;

namespace ProfitFox.Tests.Integration.Logging;

public class ServiceLoggingTests
{
    // Scenario "Событие в Production". Tests run one at a time, so redirecting the process-wide console is safe.
    [Test]
    public async Task Production_writes_each_log_event_as_one_json_line()
    {
        var console = new StringWriter();
        TextWriter original = Console.Out;
        Console.SetOut(console);
        try
        {
            await using (var factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Environment = "Production" })
            {
                using HttpClient client = factory.CreateClient();
                await client.GetAsync("/no-such-route");
            }
        }
        finally
        {
            Console.SetOut(original);
        }

        string[] lines = console.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines.ShouldNotBeEmpty();
        foreach (string line in lines)
        {
            using var logEvent = JsonDocument.Parse(line);
            logEvent.RootElement.TryGetProperty("@t", out _).ShouldBeTrue(line);
            logEvent.RootElement.TryGetProperty("@mt", out _).ShouldBeTrue(line);
        }
    }

    // Scenario "Успешный запрос".
    [Test]
    public async Task Each_http_request_is_logged_once_with_method_path_status_and_elapsed_time()
    {
        ILogEventSink sink = Substitute.For<ILogEventSink>();
        // Disposing the factory waits for the request pipeline, so the request event is written before the assertions.
        await using (var factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { LogSink = sink })
        {
            using HttpClient client = factory.CreateClient();
            HttpResponseMessage response = await client.GetAsync(
                "/api/market-data/candles?category=linear&symbol=BTCUSDT&interval=60&from=2024-01-01T00:00:00Z&to=2024-01-02T00:00:00Z");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Every event inside a request carries RequestPath from the request scope; only the request event has StatusCode.
        LogEvent request = Events(sink)
            .Where(e => e.Properties.ContainsKey("StatusCode")
                && e.Properties.TryGetValue("RequestPath", out LogEventPropertyValue? path)
                && path.ToString() == "\"/api/market-data/candles\"")
            .ShouldHaveSingleItem();
        request.Properties["RequestMethod"].ToString().ShouldBe("\"GET\"");
        request.Properties["StatusCode"].ToString().ShouldBe("200");
        request.Properties.ShouldContainKey("Elapsed");
    }

    // Scenario "Миграции на чистой базе не пишут SQL".
    [Test]
    public async Task Migrations_on_a_fresh_database_log_nothing_below_warning()
    {
        ILogEventSink sink = Substitute.For<ILogEventSink>();
        await using (var factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { LogSink = sink })
        {
            factory.CreateClient().Dispose();
        }

        Events(sink)
            .Where(e => e.Level < LogEventLevel.Warning
                && e.Properties.TryGetValue("SourceContext", out LogEventPropertyValue? source)
                && source.ToString().StartsWith("\"FluentMigrator", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    // Scenario "База недоступна при миграции". Port 1 refuses connections at once, so the migration fails fast.
    [Test]
    public async Task Startup_failure_is_logged_as_fatal_before_the_process_stops()
    {
        ILogEventSink sink = Substitute.For<ILogEventSink>();
        await using var factory = new ProfitFoxFactory("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1")
        {
            LogSink = sink,
        };

        Should.Throw<Exception>(() => factory.CreateClient());

        LogEvent fatal = Events(sink).Where(e => e.Level == LogEventLevel.Fatal).ShouldHaveSingleItem();
        fatal.Exception.ShouldNotBeNull();
    }

    // Scenario "База недоступна при миграции", for a failure inside Build: the host logger does not exist yet,
    // so only the console shows the event.
    [Test]
    public async Task Failure_while_building_the_host_is_logged_as_fatal()
    {
        var console = new StringWriter();
        TextWriter original = Console.Out;
        Console.SetOut(console);
        try
        {
            await using var factory = new FailingBuildFactory(await IntegrationEnvironment.CreateDatabaseAsync());
            Should.Throw<Exception>(() => factory.CreateClient());
        }
        finally
        {
            Console.SetOut(original);
        }

        console.ToString().ShouldContain(FailingBuildFactory.Message);
    }

    private static IEnumerable<LogEvent> Events(ILogEventSink sink) =>
        sink.ReceivedCalls().Select(call => (LogEvent)call.GetArguments()[0]!);

    private sealed class FailingBuildFactory(string connectionString) : ProfitFoxFactory(connectionString)
    {
        public const string Message = "Service registration failed in a test.";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(_ => throw new InvalidOperationException(Message));
        }
    }
}
