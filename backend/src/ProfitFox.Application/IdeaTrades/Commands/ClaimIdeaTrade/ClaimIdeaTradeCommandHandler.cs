using MediatR;

using ProfitFox.Application.LiveTrading;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Application.IdeaTrades.Commands.ClaimIdeaTrade;

public sealed class ClaimIdeaTradeCommandHandler(IIdeaTradeRepository repository, TradingSettings settings, WorkerIdentity worker, TimeProvider time)
    : IRequestHandler<ClaimIdeaTradeCommand, LeasedIdeaTrade?>
{
    public Task<LeasedIdeaTrade?> Handle(ClaimIdeaTradeCommand request, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();

        return repository.ClaimAsync(worker.Id, now, now + settings.LeaseDuration, cancellationToken);
    }
}