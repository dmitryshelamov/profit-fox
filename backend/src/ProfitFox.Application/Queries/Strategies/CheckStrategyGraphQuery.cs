using System.Text.Json;

using MediatR;

using ProfitFox.Application.Contracts.Strategies;

namespace ProfitFox.Application.Queries.Strategies;

/// <summary>
///     Checks a graph as a strategy without any history (spec strategy-constructor «Проверка графа стратегии без истории»):
///     a graph with errors is an ordinary answer, not a refusal.
/// </summary>
public sealed record CheckStrategyGraphQuery(JsonElement Graph) : IRequest<StrategyCheckInternal>;
