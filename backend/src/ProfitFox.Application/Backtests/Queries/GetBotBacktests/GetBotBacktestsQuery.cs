using MediatR;

using ProfitFox.Domain.Entities.Backtests;

namespace ProfitFox.Application.Backtests.Queries.GetBotBacktests;

/// <summary>Backtests of the bot, newest first; <c>null</c> when there is no such bot.</summary>
public sealed record GetBotBacktestsQuery(Guid BotId) : IRequest<IReadOnlyList<Backtest>?>;