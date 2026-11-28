using ProfitFox.Application.Contracts.Trades;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Presentation.Contracts.Trades.Converters;

/// <summary>
///     The trades, the count of an entry and the risk module as the API sends them.
/// </summary>
public static class TradeResponses
{
    public static TradeResponse Of(Trade trade, double? ideaResult = null)
    {
        return new TradeResponse(
            trade.Id,
            trade.IdeaId,
            trade.Symbol,
            TradeNames.Of(trade.Side),
            trade.Strategy,
            trade.Version,
            Timeframes.Name(trade.Timeframe),
            trade.IdeaMoment.ToUnixTimeMilliseconds(),
            trade.Until.ToUnixTimeMilliseconds(),
            trade.IdeaEntry,
            trade.AccountId,
            trade.AccountName,
            trade.Amount,
            trade.Leverage,
            trade.ClickPrice,
            trade.Quantity,
            trade.Numbers.Held,
            trade.Stop,
            TradeNames.Of(trade.StopSource),
            trade.Target,
            TradeNames.Of(trade.State),
            trade.Reason,
            trade.CloseReason is { } reason ? TradeNames.Of(reason) : null,
            trade.Numbers.EntryPrice,
            trade.Numbers.ExitPrice,
            trade.Numbers.Fees,
            trade.Numbers.Funding,
            trade.Result,
            trade.Result is { } result && trade.Amount > 0 ? result / trade.Amount : null,
            trade.Unrealised,
            trade.MarkedAt?.ToUnixTimeMilliseconds(),
            trade.CreatedAt.ToUnixTimeMilliseconds(),
            trade.OpenedAt?.ToUnixTimeMilliseconds(),
            trade.ClosedAt?.ToUnixTimeMilliseconds(),
            ideaResult);
    }

    public static EntryPreviewResponse Of(EntryPreviewInternal preview)
    {
        EntryCalculation count = preview.Calculation;
        return new EntryPreviewResponse(
            TradeNames.Of(count.Side),
            count.Price,
            count.Amount,
            count.Leverage,
            count.Quantity,
            count.Value,
            count.Margin,
            count.IdeaEntry,
            count.IdeaStop,
            count.IdeaTarget,
            count.Stop,
            count.StopSource is { } source ? TradeNames.Of(source) : null,
            count.LimitStop,
            count.StopDistance,
            count.Target,
            count.TargetDistance,
            count.LossAtStop,
            count.ProfitAtTarget,
            count.Liquidation,
            count.Needed,
            count.Until.ToUnixTimeMilliseconds(),
            preview.AccountId,
            preview.AccountName,
            preview.Free,
            [.. count.Checks.Select(Of)],
            count.Passed);
    }

    public static EntryCheckResponse Of(EntryCheck check)
    {
        return new EntryCheckResponse(check.Code, check.Passed, check.Text);
    }

    public static TradeSettingsResponse Of(TradeSettings settings)
    {
        return new TradeSettingsResponse(
            settings.DefaultAccountId,
            settings.DefaultAmount,
            settings.Leverage,
            settings.MaxAmount,
            settings.MaxOpenTrades,
            settings.MaxTotalAmount,
            settings.MaxLossPercent,
            settings.MaxDayLoss,
            (int)settings.ExpiryMargin.TotalMinutes,
            (int)settings.EntryWait.TotalSeconds,
            settings.StopAttempts,
            settings.SavedAt?.ToUnixTimeMilliseconds());
    }

    public static TradeSettings Of(TradeSettingsRequest request)
    {
        return new TradeSettings
        {
            DefaultAccountId = request.DefaultAccountId,
            DefaultAmount = request.DefaultAmount,
            Leverage = request.Leverage,
            MaxAmount = request.MaxAmount,
            MaxOpenTrades = request.MaxOpenTrades,
            MaxTotalAmount = request.MaxTotalAmount,
            MaxLossPercent = request.MaxLossPercent,
            MaxDayLoss = request.MaxDayLoss,
            ExpiryMargin = request.ExpiryMarginMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : TradeSettings.DefaultExpiryMargin,
            EntryWait = request.EntryWaitSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : TradeSettings.DefaultEntryWait,
            StopAttempts = request.StopAttempts ?? TradeSettings.DefaultStopAttempts
        };
    }

    public static HaltResponse Of(TradeHalt halt, IReadOnlyList<string> notClosed)
    {
        return new HaltResponse(halt.HaltedAt?.ToUnixTimeMilliseconds(), halt.Loss, halt.Limit, notClosed, halt.LiftedAt?.ToUnixTimeMilliseconds());
    }

    public static RiskStateResponse Of(RiskStateInternal state)
    {
        return new RiskStateResponse(
            state.Missing.Count == 0,
            state.Missing,
            decimal.Round(state.Day.Loss, 2),
            decimal.Round(state.Day.Sum, 2),
            state.Settings.MaxDayLoss,
            state.Day.From.ToUnixTimeMilliseconds(),
            state.OpenTrades,
            state.Settings.MaxOpenTrades,
            state.OpenAmount,
            state.Settings.MaxTotalAmount,
            Of(state.Halt, state.NotClosed),
            state.TraderAlive,
            state.Trader,
            state.Accounts,
            state.Streamed,
            [.. state.Polled.Select(account => new PolledAccountResponse(account.Id, account.Name, account.Since.ToUnixTimeMilliseconds()))]);
    }
}
