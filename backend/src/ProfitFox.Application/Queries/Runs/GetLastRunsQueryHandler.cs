using MediatR;

using ProfitFox.Application.Contracts.Runs;
using ProfitFox.Application.Contracts.Runs.Converters;
using ProfitFox.Domain.Entities.Runs;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.Ledger;

namespace ProfitFox.Application.Queries.Runs;

/// <summary>
///     The queue and the ledger are read once for all the hypotheses of the list.
/// </summary>
internal sealed class GetLastRunsQueryHandler(IRunRepository runs, ILedgerRepository ledger)
    : IRequestHandler<GetLastRunsQuery, IReadOnlyDictionary<string, LastRunInternal>>
{
    public async Task<IReadOnlyDictionary<string, LastRunInternal>> Handle(GetLastRunsQuery request, CancellationToken cancellationToken)
    {
        Dictionary<string, LastRunInternal> last = [];
        if (request.Hypotheses.Count == 0)
        {
            return last;
        }

        IReadOnlyList<Run> active = await runs.ActiveAsync(cancellationToken);
        HypothesisVersion[] versions = [.. request.Hypotheses.Select(item => new HypothesisVersion(item.Id, item.Version))];
        IReadOnlyList<FinishedRun> finished = await ledger.LastAsync(versions, cancellationToken);
        foreach (HypothesisVersion version in versions)
        {
            if (active.FirstOrDefault(run => run.Hypothesis == version.Id && run.Version == version.Version) is { } standing)
            {
                last[version.Id] = new LastRunInternal(standing.ToInternal(), Finished: null);
            }
            else if (finished.FirstOrDefault(line => line.Entry.Hypothesis == version.Id && line.Entry.Version == version.Version) is { } ended)
            {
                last[version.Id] = new LastRunInternal(Active: null, ended.ToInternal());
            }
        }

        return last;
    }
}
