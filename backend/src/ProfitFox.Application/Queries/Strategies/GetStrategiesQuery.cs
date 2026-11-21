using MediatR;

using ProfitFox.Application.Contracts.Strategies;

namespace ProfitFox.Application.Queries.Strategies;

/// <summary>
///     The saved strategies in name order (spec strategy-constructor «Список стратегий»); one that does not read stays in
///     the list with its error.
/// </summary>
public sealed record GetStrategiesQuery : IRequest<IReadOnlyList<StrategyEntryInternal>>;
