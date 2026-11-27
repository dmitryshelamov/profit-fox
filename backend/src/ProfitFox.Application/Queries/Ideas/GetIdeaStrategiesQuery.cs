using MediatR;

using ProfitFox.Domain.ValueObjects.Lists;

namespace ProfitFox.Application.Queries.Ideas;

/// <summary>
///     How many open or closed ideas each strategy has (spec strategy-ideas «Число идей по стратегиям»; change
///     add-setup-strategies, design.md, decision 9): the choices, the period and the saved filter of the list but the
///     strategy, its page and its sort. <see cref="State" />, <see cref="Timeframe" />, <see cref="Side" /> and
///     <see cref="ExitReason" /> are choices of a list of values, and so is <see cref="PairKind" />, the kind of the pair
///     (change add-instrument-kind-filter); another value refuses the request.
/// </summary>
public sealed record GetIdeaStrategiesQuery(
    string? State = null,
    string? Search = null,
    string? Timeframe = null,
    string? Side = null,
    string? Label = null,
    bool OnlyPositive = false,
    bool OnlyWithStop = false,
    string? ExitReason = null,
    int? PeriodDays = null,
    Guid? Filter = null,
    string? PairKind = null,
    string? MinScore = null) : IRequest<IdeaStrategiesResult>;

/// <summary>
///     The number of all the ideas counted, a row for every strategy of a category of ideas and for every strategy of the
///     ideas counted, by name, and whether the saved filter asked for is gone.
/// </summary>
public sealed record IdeaStrategiesResult(int Total, IReadOnlyList<IdeaStrategyCount> Strategies, bool FilterGone);
