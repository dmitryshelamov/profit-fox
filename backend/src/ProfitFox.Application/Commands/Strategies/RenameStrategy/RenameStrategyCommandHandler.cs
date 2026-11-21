using MediatR;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Application.Contracts.Strategies.Converters;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Signals;

namespace ProfitFox.Application.Commands.Strategies.RenameStrategy;

internal sealed class RenameStrategyCommandHandler(IStrategyRepository strategies, ISignalRepository signals)
    : IRequestHandler<RenameStrategyCommand, StrategyInternal>
{
    /// <exception cref="StrategyLibraryException">There is no such strategy, or the new name is not allowed or is taken.</exception>
    public async Task<StrategyInternal> Handle(RenameStrategyCommand request, CancellationToken cancellationToken)
    {
        StrategyDocument strategy = await StrategyChecks.ReadAsync(strategies, request.Name, cancellationToken);
        if (!SignalNames.IsValid(request.To))
        {
            throw new StrategyLibraryException(StrategyProblem.Invalid, $"{request.To} is not a strategy name", [StrategyChecks.NameError(request.To)]);
        }

        if (request.To != request.Name)
        {
            if (await strategies.ExistsAsync(request.To, cancellationToken))
            {
                throw StrategyChecks.Taken(request.To);
            }

            await strategies.RenameAsync(request.Name, request.To, cancellationToken);
            strategy.Rename(request.To);
        }

        return strategy.ToInternal(await StrategyChecks.UsesAsync(signals, strategy.Graph, cancellationToken));
    }
}
