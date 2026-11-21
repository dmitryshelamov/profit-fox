using MediatR;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Application.Contracts.Strategies.Converters;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Queries.Strategies;

internal sealed class GetStrategiesQueryHandler(IStrategyRepository strategies) : IRequestHandler<GetStrategiesQuery, IReadOnlyList<StrategyEntryInternal>>
{
    public async Task<IReadOnlyList<StrategyEntryInternal>> Handle(GetStrategiesQuery request, CancellationToken cancellationToken)
    {
        List<StrategyEntryInternal> entries = [];
        foreach (string name in (await strategies.NamesAsync(cancellationToken)).Order(StringComparer.Ordinal))
        {
            entries.Add(await Entry(name, cancellationToken));
        }

        return entries;
    }

    private async Task<StrategyEntryInternal> Entry(string name, CancellationToken cancellationToken)
    {
        try
        {
            StrategyDocument strategy = await StrategyChecks.ReadAsync(strategies, name, cancellationToken);
            return strategy.ToEntryInternal();
        }
        catch (StrategyLibraryException exception)
        {
            return new StrategyEntryInternal(name, Description: null, Version: 0, exception.Message);
        }
    }
}
