using ProfitFox.Infrastructure;

using Serilog;

namespace ProfitFox.TradeFlow;

/// <summary>
///     Collects the public trades of every pair of the scanner into the buys and sells of each minute; one collector holds the trade flow at a
///     time. The API migrates the database.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // Logs failures before the host logger exists; AddSerilog replaces it once the host is built.
        Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();

        try
        {
            Configure(builder);
            await builder.Build().RunAsync();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Host terminated unexpectedly.");

            throw;
        }
    }

    /// <summary>The services of the collector; a test host replaces some of them afterwards.</summary>
    public static void Configure(HostApplicationBuilder builder)
    {
        // The settings file on the server, mounted into the container; it overrides every other source. Without it the collector runs on
        // its own settings.
        builder.Configuration.AddJsonFile("config/trade-flow.json", optional: true, reloadOnChange: false);

        // The static logger stays the one Main logs startup failures with; a collector in the process of a test leaves the API's alone.
        builder.Services.AddSerilog(
            (services, logger) => logger
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services),
            preserveStaticLogger: true);
        InfrastructureRegistrar.ConfigureTradeFlow(builder.Configuration, builder.Services);
    }
}