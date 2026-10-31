using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

using ProfitFox.Infrastructure;
using ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Extensions;

using Serilog;

namespace ProfitFox.Presentation;

public class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // Logs failures before the host logger exists; AddSerilog replaces it once the host is built.
        // A plain logger, not CreateBootstrapLogger: a frozen reloadable logger breaks the second host in one process (tests).
        Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();

        try
        {
            // Add services to the container.

            builder.Services.AddSerilog((services, logger) => logger
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services));

            // Validation errors are keyed by JSON names (`from`), the same names the API accepts.
            builder.Services.AddControllers(options =>
                options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()));

            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            InfrastructureRegistrar.Configure(builder.Configuration, builder.Services);

            WebApplication app = builder.Build();

            app.Services.MigrateToLatestVersion();

            // Configure the HTTP request pipeline.
            app.UseSerilogRequestLogging();

            app.MapOpenApi();
            app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "ProfitFox API"));

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            await app.RunAsync();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Host terminated unexpectedly.");
            throw;
        }
    }
}