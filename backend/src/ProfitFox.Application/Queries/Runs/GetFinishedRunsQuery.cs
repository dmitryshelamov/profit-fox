using MediatR;

using ProfitFox.Application.Contracts.Runs;

namespace ProfitFox.Application.Queries.Runs;

/// <summary>
///     The latest <see cref="Limit" /> lines of the ledger, the newest first, each with the kind of its run.
/// </summary>
public sealed record GetFinishedRunsQuery(int Limit) : IRequest<IReadOnlyList<FinishedRunInternal>>;
