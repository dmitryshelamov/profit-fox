using System.Text.Json.Serialization;

using ProfitFox.Application.Services.Ideas;
using ProfitFox.Infrastructure;
using ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator;
using ProfitFox.Presentation.HealthChecks;

namespace ProfitFox.Presentation;

/// <summary>
///     The web API of the bench: the signals and strategies, the queue of backtests and the previews for the page
///     of the frontend, and the frontend itself when it is built into <c>wwwroot</c>, over the database of the bench. At start
///     it migrates the database, which starts empty. History is read from the market-data service.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Services.AddInfrastructure(builder.Configuration);

        // The thresholds of «⚡ всплеск» the list of signals shows (change add-ideas-and-market): those the Worker takes.
        builder.Services.AddSingleton(builder.Configuration.GetSection("Ideas").Get<IdeasSettings>() ?? new IdeasSettings());

        builder.Services.AddRouting(options => options.LowercaseUrls = true);
        builder.Services
            .AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        // /health answers 200 while the database answers.
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

        WebApplication app = builder.Build();

        // A new database stays empty, and signals and strategies are added through the API. A failure stops the start.
        app.Services.MigrateBench();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        // The built frontend, when the service has a wwwroot folder, with its pages falling back to index.html.
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
            // The fallback leaves /api and /health alone, so an unknown route there stays a 404.
            app.MapFallbackToFile("{*path:regex(^(?!api(/|$)|health$).*$)}", "index.html");
        }

        await app.RunAsync();
    }
}
