using System.Text.Json.Serialization;
using ProfitFox.Bench.Presentation.Preview;

namespace ProfitFox.Bench.Presentation;

/// <summary>
///     The services of the bench API apart from where history comes from: <see cref="Program" /> adds the market-data
///     service, the tests add a fake one. The controllers are found in this assembly, also when a test hosts it.
/// </summary>
public static class BenchPresentation
{
    public static IServiceCollection AddBenchPresentation(this IServiceCollection services, BenchFolder folder)
    {
        services.AddSingleton(folder);
        services.AddSingleton<SignalPreview>();
        services.AddRouting(options => options.LowercaseUrls = true);
        services.AddControllers()
            .AddApplicationPart(typeof(BenchPresentation).Assembly)
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });
        services.AddOpenApi();
        return services;
    }
}
