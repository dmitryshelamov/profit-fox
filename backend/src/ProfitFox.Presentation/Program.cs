using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using ProfitFox.Infrastructure;
using ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Extensions;

namespace ProfitFox.Presentation;

public class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // Add services to the container.

        // Validation errors are keyed by JSON names (`from`), the same names the API accepts.
        builder.Services.AddControllers(options =>
            options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()));

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        InfrastructureRegistrar.Configure(builder.Configuration, builder.Services);

        WebApplication app = builder.Build();

        app.Services.MigrateToLatestVersion();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();

        app.UseAuthorization();

        app.MapControllers();

        await app.RunAsync();
    }
}