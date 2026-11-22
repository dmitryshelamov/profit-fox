using MediatR;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Application.Contracts.Strategies.Converters;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Signals;

namespace ProfitFox.Application.Queries.Strategies;

internal sealed class GetStrategyVersionQueryHandler(IStrategyRepository strategies, ISignalRepository signals) : IRequestHandler<GetStrategyVersionQuery, StrategyInternal>
{
    /// <exception cref="StrategyLibraryException">There is no such strategy or version, or it does not read.</exception>
    public async Task<StrategyInternal> Handle(GetStrategyVersionQuery request, CancellationToken cancellationToken)
    {
        if (!SignalNames.IsValid(request.Name) || await strategies.ReadVersionAsync(request.Name, request.Version, cancellationToken) is not { } read)
        {
            throw new StrategyLibraryException(StrategyProblem.NotFound, $"there is no version {request.Version} of the strategy {request.Name}");
        }

        if (read.Strategy is not { } strategy)
        {
            throw new StrategyLibraryException(StrategyProblem.Unreadable, string.Join("; ", read.Errors));
        }

        return strategy.ToInternal(await StrategyChecks.UsesAsync(signals, strategy.Graph, cancellationToken));
    }
}
