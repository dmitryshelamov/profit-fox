using MediatR;

using ProfitFox.Application.Contracts.Backtests;

namespace ProfitFox.Application.Queries.Backtests;

/// <summary>
///     The backtests that wait and go in the order of the queue, or without <see cref="Active" /> the latest that ended,
///     the newest first, at most <see cref="Limit" />.
/// </summary>
public sealed record GetBacktestsQuery(bool Active, int Limit) : IRequest<IReadOnlyList<BacktestInternal>>;
