using MediatR;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Application.Contracts.Strategies.Converters;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Commands.Strategies.CreateStrategy;

internal sealed class CreateStrategyCommandHandler(IStrategyRepository strategies, ISignalRepository signals)
    : IRequestHandler<CreateStrategyCommand, StrategyInternal>
{
    /// <exception cref="StrategyLibraryException">The name or the graph is refused, or the name is taken.</exception>
    public async Task<StrategyInternal> Handle(CreateStrategyCommand request, CancellationToken cancellationToken)
    {
        (StrategyDocument strategy, IReadOnlyList<SignalUseInternal> uses) =
            await StrategyChecks.CheckedAsync(signals, request.Name, request.Description, request.Graph, request.Layout, request.Kind, cancellationToken);
        if (await strategies.ExistsAsync(request.Name, cancellationToken))
        {
            throw StrategyChecks.Taken(request.Name);
        }

        return (await strategies.CreateAsync(strategy, cancellationToken)).ToInternal(uses);
    }
}
