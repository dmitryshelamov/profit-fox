using MediatR;

using ProfitFox.Application.Commands.Runs.Base;
using ProfitFox.Application.Contracts.Runs;
using ProfitFox.Application.Contracts.Runs.Converters;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ledger;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Hypotheses;

namespace ProfitFox.Application.Commands.Runs.EnqueueRun;

/// <summary>
///     The rules of the ledger are checked when a run is queued, so a refusal comes at once and leaves no line; the Worker checks
///     them again when the run is decided.
/// </summary>
internal sealed class EnqueueRunCommandHandler(IHypothesisRepository hypotheses, ILedgerRepository ledger, IRunRepository runs)
    : IRequestHandler<EnqueueRunCommand, RunInternal>
{
    /// <exception cref="HypothesisException">The hypothesis or its version is not there, or cannot be run.</exception>
    /// <exception cref="LedgerException">The rules of the ledger refuse the run, or the same run already waits or goes; <see cref="LedgerException.RunId" /> is the run that stands in the way.</exception>
    public async Task<RunInternal> Handle(EnqueueRunCommand request, CancellationToken cancellationToken)
    {
        Hypothesis read = await RunHypotheses.ReadAsync(hypotheses, request.Hypothesis, request.Version, cancellationToken);
        string name = Periods.Name(request.Period);
        LedgerRules.CheckCanRun(await ledger.EntriesAsync(cancellationToken), read.Id, read.Version, name, request.Parity);
        return (await runs.EnqueueAsync(Guid.NewGuid().ToString("N"), read.Id, read.Version, name, request.Parity, cancellationToken)).ToInternal();
    }
}
