using MediatR;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Application.Contracts.Strategies.Converters;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Application.Commands.Strategies.ReplaceStrategy;

internal sealed class ReplaceStrategyCommandHandler(IStrategyRepository strategies, ISignalRepository signals)
    : IRequestHandler<ReplaceStrategyCommand, StrategyInternal>
{
    /// <exception cref="StrategyLibraryException">There is no such strategy, the kind is another, or the graph is refused.</exception>
    public async Task<StrategyInternal> Handle(ReplaceStrategyCommand request, CancellationToken cancellationToken)
    {
        if (!SignalNames.IsValid(request.Name) || await strategies.ReadAsync(request.Name, cancellationToken) is not { } saved)
        {
            throw StrategyChecks.NotFound(request.Name);
        }

        // A strategy that does not read keeps the kind the store has for it; the store refuses another.
        StrategyKind? kind = saved.Strategy?.Kind ?? request.Kind;
        if (request.Kind is { } asked && kind is { } current && asked != current)
        {
            throw new StrategyLibraryException(StrategyProblem.Invalid, $"the kind of {request.Name} does not change", [StrategyKinds.Changed(request.Name, current)]);
        }

        (StrategyDocument strategy, IReadOnlyList<SignalUseInternal> uses) =
            await StrategyChecks.CheckedAsync(signals, request.Name, request.Description, request.Graph, request.Layout, kind, cancellationToken);
        return (await strategies.ReplaceAsync(strategy, cancellationToken)).ToInternal(uses);
    }
}
