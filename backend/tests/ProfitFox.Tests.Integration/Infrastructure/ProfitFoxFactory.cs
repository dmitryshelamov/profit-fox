using Bybit.Net.Clients;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Options;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Presentation;

using Serilog.Core;

namespace ProfitFox.Tests.Integration.Infrastructure;

public class ProfitFoxFactory(string connectionString) : WebApplicationFactory<Program>
{
    public string ConnectionString => connectionString;

    public FakeBybitHandler Bybit { get; } = new();

    /// <summary>The app's clock; it moves only when a test advances it.</summary>
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.Parse("2024-03-10T12:30:00Z"));

    /// <summary>False keeps created jobs out of the background service, so they stay <c>Pending</c>.</summary>
    public bool RunImports { get; init; } = true;

    /// <summary>The host environment; unset keeps the factory default, Development.</summary>
    public string? Environment { get; init; }

    /// <summary>Receives every event the app's logger writes.</summary>
    public ILogEventSink? LogSink { get; init; }

    public static async Task<ProfitFoxFactory> CreateAsync() =>
        new(await IntegrationEnvironment.CreateDatabaseAsync());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:MarketData", connectionString);
        if (Environment is not null)
        {
            builder.UseEnvironment(Environment);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            // Bybit.Net configures its own primary handler, so replace the whole client: no test may reach the real Bybit.
            services.AddSingleton<IBybitRestClient>(_ => new BybitRestClient(
                new HttpClient(Bybit, disposeHandler: false),
                NullLoggerFactory.Instance,
                Options.Create(new BybitRestOptions { RateLimiterEnabled = false })));
            if (!RunImports)
            {
                services.AddSingleton(Substitute.For<ICandleImportQueue>());
            }

            if (LogSink is not null)
            {
                services.AddSingleton(LogSink);
            }
        });
    }
}
