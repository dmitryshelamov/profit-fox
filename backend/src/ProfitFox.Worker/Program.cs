using ProfitFox.Infrastructure;

using Serilog;

namespace ProfitFox.Worker;

/// <summary>Takes candle imports and backtest work from the database; any number of Workers share it. The API migrates the database.</summary>
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

    /// <summary>The services of the Worker; a test host replaces some of them afterwards.</summary>
    public static void Configure(HostApplicationBuilder builder)
    {
        // The static logger stays the one Main logs startup failures with; a Worker in the process of a test leaves the API's alone.
        builder.Services.AddSerilog(
            (services, logger) => logger
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services),
            preserveStaticLogger: true);
        InfrastructureRegistrar.ConfigureWorker(builder.Configuration, builder.Services);
    }
}