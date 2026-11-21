using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Application.Contracts.Strategies;

/// <summary>
///     A graph checked as a strategy without history (spec strategy-constructor «Проверка графа стратегии без истории»):
///     every error, and what each of its signal nodes points at.
/// </summary>
public sealed record StrategyCheckInternal(IReadOnlyList<GraphError> Errors, IReadOnlyList<SignalUseInternal> Signals);
