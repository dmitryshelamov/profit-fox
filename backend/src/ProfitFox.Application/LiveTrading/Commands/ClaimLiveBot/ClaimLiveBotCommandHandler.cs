using MediatR;

using ProfitFox.Application.Workers;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.LiveTrading;

namespace ProfitFox.Application.LiveTrading.Commands.ClaimLiveBot;

public sealed class ClaimLiveBotCommandHandler(ILiveBotRepository repository, WorkerSettings settings, WorkerIdentity worker, TimeProvider time)
    : IRequestHandler<ClaimLiveBotCommand, LeasedBot?>
{
    public Task<LeasedBot?> Handle(ClaimLiveBotCommand request, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();

        return repository.ClaimAsync(worker.Id, now, now + settings.LeaseDuration, cancellationToken);
    }
}