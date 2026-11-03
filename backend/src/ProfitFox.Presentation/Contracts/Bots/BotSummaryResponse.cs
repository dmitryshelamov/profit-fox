using System.Globalization;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Bots.Queries.GetBots;
using ProfitFox.Application.LiveTrading;
using ProfitFox.Application.MarketData;
using ProfitFox.Presentation.Contracts.Backtests;

namespace ProfitFox.Presentation.Contracts.Bots;

public sealed record BotSummaryResponse(Guid Id, string Name, Guid StrategyId, string Category, string Symbol, string Interval, string Balance, LastBacktestResponse? LastBacktest, BotAccountResponse? ExchangeAccount, string Status, string? StatusReason, bool CloseRequested)
{
    public static BotSummaryResponse Of(BotListItem item)
    {
        return new BotSummaryResponse(
            item.Bot.Id,
            item.Bot.Name,
            item.Bot.StrategyId,
            item.Bot.Instrument.Category.ToCode(),
            item.Bot.Instrument.Symbol,
            item.Bot.Interval.ToCode(),
            item.Bot.Balance.ToString(CultureInfo.InvariantCulture),
            item.LastBacktest is { } backtest ? new LastBacktestResponse(backtest.Id, backtest.Status.ToCode(), ProgressResponse.Of(backtest)) : null,
            BotAccountResponse.Of(item.Account),
            item.Live.Status.ToCode(),
            item.Live.StopReason ?? item.Live.SessionError,
            item.Live.CloseRequested);
    }
}

/// <summary>The newest backtest of a bot in the list of bots.</summary>
public sealed record LastBacktestResponse(Guid Id, string Status, ProgressResponse Progress);