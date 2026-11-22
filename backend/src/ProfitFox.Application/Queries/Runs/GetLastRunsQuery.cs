using MediatR;

using ProfitFox.Application.Contracts.Hypotheses;
using ProfitFox.Application.Contracts.Runs;

namespace ProfitFox.Application.Queries.Runs;

/// <summary>
///     For each hypothesis of the list, by its id, the last run of its version (spec bench-run-queue «Последний прогон
///     гипотезы»): the first of its runs that wait or go, in the order of the queue, else the run that ended last. A
///     version without runs is not in the answer.
/// </summary>
public sealed record GetLastRunsQuery(IReadOnlyList<HypothesisInternal> Hypotheses)
    : IRequest<IReadOnlyDictionary<string, LastRunInternal>>;
