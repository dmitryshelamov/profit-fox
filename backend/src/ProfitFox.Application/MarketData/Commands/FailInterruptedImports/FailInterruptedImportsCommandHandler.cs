using MediatR;

using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.MarketData.Commands.FailInterruptedImports;

public sealed class FailInterruptedImportsCommandHandler(ICandleImportJobRepository jobs, TimeProvider time)
    : IRequestHandler<FailInterruptedImportsCommand>
{
    public const string InterruptedByRestart = "Interrupted by service restart.";

    public Task Handle(FailInterruptedImportsCommand request, CancellationToken cancellationToken)
    {
        return jobs.FailUnfinishedAsync(InterruptedByRestart, time.GetUtcNow(), cancellationToken);
    }
}