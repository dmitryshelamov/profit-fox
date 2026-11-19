using System.Text.Json.Serialization;
using ProfitFox.Bench.Application.Hypotheses;
using ProfitFox.Bench.Application.MarketData;
using ProfitFox.Bench.Application.Preview;
using ProfitFox.Bench.Application.Signals;
using ProfitFox.Bench.Infrastructure.Hypotheses;
using ProfitFox.Bench.Infrastructure.Signals;

namespace ProfitFox.Bench.Presentation;

/// <summary>
///     The services of the bench API apart from the market-data service: the files of the bench folder and the use cases.
///     <see cref="Program" /> adds the market-data service (<c>IHistorySource</c>, <c>IInstrumentCatalog</c>), the tests
///     add a fake one. The controllers are found in this assembly, also when a test hosts it.
/// </summary>
public static class BenchPresentation
{
    public static IServiceCollection AddBenchPresentation(this IServiceCollection services, BenchFolder folder)
    {
        services.AddSingleton(folder);
        services.AddSingleton(new HypothesisCatalog(new HypothesisFiles(Path.Combine(folder.Path, "hypotheses"))));
        services.AddSingleton(new SignalLibrary(new SignalFiles(Path.Combine(folder.Path, "signals"))));
        services.AddSingleton<PreviewHistoryCache>();
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
