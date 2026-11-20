using System.Text.Json.Serialization;
using ProfitFox.Bench.Application.Hypotheses;
using ProfitFox.Bench.Application.MarketData;
using ProfitFox.Bench.Application.Preview;
using ProfitFox.Bench.Application.Runs;
using ProfitFox.Bench.Application.Signals;
using ProfitFox.Bench.Infrastructure.Persistence;
using ProfitFox.Bench.Presentation.Hypotheses;

namespace ProfitFox.Bench.Presentation;

/// <summary>
///     The services of the bench API apart from the market-data service: the database of the bench and the use cases.
///     <see cref="Program" /> adds the market-data service (<c>IHistorySource</c>, <c>IInstrumentCatalog</c>), the tests
///     add a fake one. The controllers are found in this assembly, also when a test hosts it.
/// </summary>
public static class BenchPresentation
{
    public static IServiceCollection AddBenchPresentation(this IServiceCollection services, string connectionString)
    {
        services.AddBenchDatabase(connectionString);
        services.AddSingleton<HypothesisCatalog>();
        services.AddSingleton<SignalLibrary>();
        services.AddSingleton<RunScheduler>();
        services.AddSingleton<RunBoard>();
        services.AddSingleton<PreviewHistoryCache>();
        services.AddSingleton<SignalPreview>();
        services.AddRouting(options => options.LowercaseUrls = true);
        services.AddControllers(options => options.InputFormatters.Insert(0, new PlainTextInputFormatter()))
            .AddApplicationPart(typeof(BenchPresentation).Assembly)
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });
        services.AddOpenApi();
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
        return services;
    }

    /// <summary>
    ///     Migrates the database of the bench; a new database stays empty, and hypotheses are added through the API. A failure
    ///     stops the start.
    /// </summary>
    public static void PrepareBench(this WebApplication app)
    {
        app.Services.MigrateBench();
    }

    /// <summary>
    ///     Maps the routes of the bench: <c>/api</c>, <c>/health</c> (200 while the database answers) and, when the service has a
    ///     <c>wwwroot</c> folder, the built frontend with its pages falling back to <c>index.html</c>. The fallback leaves
    ///     <c>/api</c> and <c>/health</c> alone, so an unknown route there stays a 404.
    /// </summary>
    public static void MapBench(this WebApplication app)
    {
        bool hasFrontend = app.Environment.WebRootPath is { Length: > 0 } webRoot && Directory.Exists(webRoot);
        if (hasFrontend)
        {
            // Routing is put before the files by hand: the implicit one would pick the fallback endpoint first, and the files would never be served.
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseRouting();
        }

        app.MapControllers();
        app.MapHealthChecks("/health");
        if (hasFrontend)
        {
            app.MapFallbackToFile("{*path:regex(^(?!api(/|$)|health$).*$)}", "index.html");
        }
    }
}
