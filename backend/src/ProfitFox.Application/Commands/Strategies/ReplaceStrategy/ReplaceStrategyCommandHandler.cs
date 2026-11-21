using MediatR;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Application.Contracts.Strategies.Converters;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Signals;

namespace ProfitFox.Application.Commands.Strategies.ReplaceStrategy;

internal sealed class ReplaceStrategyCommandHandler(IStrategyRepository strategies, ISignalRepository signals)
    : IRequestHandler<ReplaceStrategyCommand, StrategyInternal>
{
    /// <exception cref="StrategyLibraryException">There is no such strategy, or the graph is refused.</exception>
    public async Task<StrategyInternal> Handle(ReplaceStrategyCommand request, CancellationToken cancellationToken)
    {
        if (!SignalNames.IsValid(request.Name) || !await strategies.ExistsAsync(request.Name, cancellationToken))
        {
            throw StrategyChecks.NotFound(request.Name);
        }

        (StrategyDocument strategy, IReadOnlyList<SignalUseInternal> uses) =
            await StrategyChecks.CheckedAsync(signals, request.Name, request.Description, request.Graph, request.Layout, cancellationToken);
        return (await strategies.ReplaceAsync(strategy, cancellationToken)).ToInternal(uses);
    }
}
