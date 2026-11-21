using MediatR;

using ProfitFox.Domain.Entities.Runs.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Commands.Runs.CancelRun;

internal sealed class CancelRunCommandHandler(IRunRepository runs) : IRequestHandler<CancelRunCommand, CancelOutcome>
{
    public Task<CancelOutcome> Handle(CancelRunCommand request, CancellationToken cancellationToken)
    {
        return runs.CancelAsync(request.RunId, cancellationToken);
    }
}
