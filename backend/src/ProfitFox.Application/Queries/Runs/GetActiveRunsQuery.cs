using MediatR;

using ProfitFox.Application.Contracts.Runs;

namespace ProfitFox.Application.Queries.Runs;

/// <summary>
///     The runs that wait and go, the one queued first first.
/// </summary>
public sealed record GetActiveRunsQuery : IRequest<IReadOnlyList<RunInternal>>;
