using MediatR;

using ProfitFox.Application.Backtests.Queries.GetBacktest;

namespace ProfitFox.Application.Backtests.Queries.GetBotBacktests;

/// <summary>Backtests of the bot, newest first, each with whether the bot changed since it; <c>null</c> when there is no such bot.</summary>
public sealed record GetBotBacktestsQuery(Guid BotId) : IRequest<IReadOnlyList<GetBacktestResult>?>;