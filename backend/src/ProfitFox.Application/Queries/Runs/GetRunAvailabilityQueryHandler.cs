using MediatR;

using ProfitFox.Application.Contracts.Runs;
using ProfitFox.Domain.Entities.Runs;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ledger;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;

namespace ProfitFox.Application.Queries.Runs;

/// <summary>
///     The queue and the ledger are read once for all the hypotheses of the list.
/// </summary>
internal sealed class GetRunAvailabilityQueryHandler(IRunRepository runs, ILedgerRepository ledger)
    : IRequestHandler<GetRunAvailabilityQuery, IReadOnlyDictionary<string, IReadOnlyList<RunAvailabilityInternal>>>
{
    private static readonly Period[] AllPeriods = [Period.Exploration, Period.Holdout, Period.NewData];
    private static readonly bool[] Kinds = [false, true];

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<RunAvailabilityInternal>>> Handle(GetRunAvailabilityQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Run> active = await runs.ActiveAsync(cancellationToken);
        IReadOnlyList<LedgerEntry> entries = await ledger.EntriesAsync(cancellationToken);
        return request.Hypotheses.ToDictionary(
            item => item.Id,
            item => item.Error is { } error
                ? Refused(error)
                : Availability(item.Id, item.Version, entries, active));
    }

    private static IReadOnlyList<RunAvailabilityInternal> Refused(string reason)
    {
        return [.. AllPeriods.SelectMany(period => Kinds.Select(parity => new RunAvailabilityInternal(Periods.Name(period), RunKinds.Name(parity), CanRun: false, reason, RunId: null)))];
    }

    private static IReadOnlyList<RunAvailabilityInternal> Availability(
        string hypothesis,
        int version,
        IReadOnlyList<LedgerEntry> entries,
        IReadOnlyList<Run> active)
    {
        List<RunAvailabilityInternal> all = [];
        foreach (Period period in AllPeriods)
        {
            string name = Periods.Name(period);
            foreach (bool parity in Kinds)
            {
                string kind = RunKinds.Name(parity);
                Run? standing = active.FirstOrDefault(run => run.IsOf(hypothesis, version, name, parity));
                if (standing is not null)
                {
                    string state = standing.IsQueued ? "waits in the queue" : "is going";
                    all.Add(new RunAvailabilityInternal(name, kind, CanRun: false, $"{hypothesis} version {version} on {name} already {state} (run {standing.Id}).", standing.Id));
                    continue;
                }

                try
                {
                    LedgerRules.CheckCanRun(entries, hypothesis, version, name, parity);
                    all.Add(new RunAvailabilityInternal(name, kind, CanRun: true, Reason: null, RunId: null));
                }
                catch (LedgerException exception)
                {
                    all.Add(new RunAvailabilityInternal(name, kind, CanRun: false, exception.Message, exception.RunId));
                }
            }
        }

        return all;
    }
}
