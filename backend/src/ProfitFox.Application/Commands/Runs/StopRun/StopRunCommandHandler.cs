using MediatR;

using ProfitFox.Application.Commands.Runs.Base;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Hypotheses;

namespace ProfitFox.Application.Commands.Runs.StopRun;

internal sealed class StopRunCommandHandler(IHypothesisRepository hypotheses, ILedgerRepository ledger) : IRequestHandler<StopRunCommand>
{
    /// <exception cref="LeaseLostException">The Worker does not hold the lease of the run; nothing is written.</exception>
    public async Task Handle(StopRunCommand request, CancellationToken cancellationToken)
    {
        Hypothesis hypothesis = await RunHypotheses.ReadAsync(hypotheses, request.Hypothesis, request.Version, cancellationToken);
        await RunLines.CloseAsync(ledger, hypothesis, RunLines.Line(request, hypothesis, Periods.Name(request.Period)), request.Status, request.Reason, request.Assignment);
    }
}
