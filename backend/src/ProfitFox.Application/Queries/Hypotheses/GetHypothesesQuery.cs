using MediatR;

using ProfitFox.Application.Contracts.Hypotheses;

namespace ProfitFox.Application.Queries.Hypotheses;

/// <summary>
///     The latest version of every hypothesis for the page (spec signal-preview «Список гипотез»), in the order of the ids. A
///     hypothesis whose text has errors stays in the list with them.
/// </summary>
public sealed record GetHypothesesQuery : IRequest<IReadOnlyList<HypothesisInternal>>;
