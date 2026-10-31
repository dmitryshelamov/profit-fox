using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.MarketData.Commands.CreateCandleImport;

namespace ProfitFox.Infrastructure.Mediatr;

internal static class MediatrRegistrar
{
    internal static void Configure(IServiceCollection services) =>
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<CreateCandleImportCommand>());
}
