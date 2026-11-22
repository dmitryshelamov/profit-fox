using MediatR;

using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Application.Queries.Backtests;

/// <summary>
///     The pairs of the set <see cref="Set" /> of a backtest with their numbers (spec strategy-backtest «Результат по
///     парам»); null when the backtest has no such set.
/// </summary>
public sealed record GetBacktestPairsQuery(string Id, int Set) : IRequest<IReadOnlyList<PairSummary>?>;
