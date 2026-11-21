using MediatR;

using ProfitFox.Application.Contracts.Hypotheses;
using ProfitFox.Application.Contracts.Runs;

namespace ProfitFox.Application.Queries.Runs;

/// <summary>
///     For each hypothesis of the list, by its id, whether its version can be run now on each period in each kind (spec
///     hypothesis-bench), for the buttons of the page. The reasons are those a run would be refused with when it is queued; a
///     hypothesis whose text has errors is refused with them.
/// </summary>
public sealed record GetRunAvailabilityQuery(IReadOnlyList<HypothesisInternal> Hypotheses)
    : IRequest<IReadOnlyDictionary<string, IReadOnlyList<RunAvailabilityInternal>>>;
