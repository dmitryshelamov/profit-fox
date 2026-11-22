using MediatR;

using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Commands.Strategies.SetStrategyIdeas;

/// <summary>
///     Turns the ideas of the saved strategy <see cref="Strategy" /> on or off on <see cref="Timeframes" /> (change
///     add-ideas-and-market, spec strategy-ideas «Включение идей у стратегии»).
/// </summary>
public sealed record SetStrategyIdeasCommand(string Strategy, bool Enabled, IReadOnlyList<string>? Timeframes) : IRequest<StrategyIdeas>;
