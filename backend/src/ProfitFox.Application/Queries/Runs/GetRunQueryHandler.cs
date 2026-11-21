using MediatR;

using ProfitFox.Application.Contracts.Runs;
using ProfitFox.Application.Contracts.Runs.Converters;
using ProfitFox.Domain.Entities.Runs;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ledger;

namespace ProfitFox.Application.Queries.Runs;

internal sealed class GetRunQueryHandler(IRunRepository runs, ILedgerRepository ledger) : IRequestHandler<GetRunQuery, RunDetailsInternal?>
{
    public async Task<RunDetailsInternal?> Handle(GetRunQuery request, CancellationToken cancellationToken)
    {
        Run? run = await runs.FindAsync(request.RunId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        LedgerResult? result = await ledger.FindAsync(request.RunId, cancellationToken);
        return new RunDetailsInternal(run.ToInternal(), result?.Entry.ToInternal(), result?.Report);
    }
}
