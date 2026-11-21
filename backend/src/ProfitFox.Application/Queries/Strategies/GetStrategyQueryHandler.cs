using MediatR;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Application.Contracts.Strategies.Converters;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Queries.Strategies;

internal sealed class GetStrategyQueryHandler(IStrategyRepository strategies, ISignalRepository signals) : IRequestHandler<GetStrategyQuery, StrategyInternal>
{
    /// <exception cref="StrategyLibraryException">There is no such strategy, or it does not read.</exception>
    public async Task<StrategyInternal> Handle(GetStrategyQuery request, CancellationToken cancellationToken)
    {
        StrategyDocument strategy = await StrategyChecks.ReadAsync(strategies, request.Name, cancellationToken);
        return strategy.ToInternal(await StrategyChecks.UsesAsync(signals, strategy.Graph, cancellationToken));
    }
}
