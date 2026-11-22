using System.Text.Json;

using MediatR;

using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Application.Queries.Strategies;

/// <summary>
///     Checks a graph as a strategy of <see cref="Kind" /> without any history (spec strategy-constructor «Проверка графа
///     стратегии без истории», «Вид в списке и в редакторе»): a graph with errors is an ordinary answer, not a refusal.
/// </summary>
public sealed record CheckStrategyGraphQuery(JsonElement Graph, StrategyKind Kind = StrategyKind.Bot) : IRequest<StrategyCheckInternal>;
