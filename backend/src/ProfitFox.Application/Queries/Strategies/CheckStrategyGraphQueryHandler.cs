using MediatR;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Queries.Strategies;

internal sealed class CheckStrategyGraphQueryHandler(ISignalRepository signals) : IRequestHandler<CheckStrategyGraphQuery, StrategyCheckInternal>
{
    public async Task<StrategyCheckInternal> Handle(CheckStrategyGraphQuery request, CancellationToken cancellationToken)
    {
        StrategyGraphCheck check = await StrategyChecks.CheckAsync(signals, request.Graph, cancellationToken);
        return new StrategyCheckInternal(check.Errors, check.Signals);
    }
}
