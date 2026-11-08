using ProfitFox.Application.Ideas;
using ProfitFox.Application.IdeaTrades;
using ProfitFox.Application.LiveTrading;
using ProfitFox.Application.MarketData;
using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Converters;

internal static class IdeaTradeDbConverter
{
    /// <param name="trade"></param>
    /// <param name="steppedUntil">The cursor of a paper trade, none for a real one.</param>
    internal static IdeaTradeDb FromDomain(IdeaTrade trade, DateTimeOffset? steppedUntil = null)
    {
        return new IdeaTradeDb
        {
            Id = trade.Id,
            IdeaId = trade.IdeaId,
            AccountId = trade.AccountId,
            Category = trade.Instrument.Category.ToCode(),
            Symbol = trade.Instrument.Symbol,
            Setup = trade.Setup.ToCode(),
            Interval = trade.Interval.ToCode(),
            Type = trade.Type.ToCode(),
            Side = trade.Side.ToCode(),
            EntryPrice = trade.EntryPrice,
            Stop = trade.Stop,
            Target = trade.Target,
            Quantity = trade.Quantity,
            Amount = trade.Amount,
            Risk = trade.Risk,
            EntryDeadline = trade.EntryDeadline,
            Status = trade.Status.ToCode(),
            StatusAt = trade.StatusAt,
            Reason = trade.Reason,
            CancelRequested = trade.CancelRequested,
            CloseRequested = trade.CloseRequested,
            EntryQuantity = trade.Entry.Quantity,
            EntryValue = trade.Entry.Value,
            EntryFee = trade.Entry.Fee,
            TakeProfitQuantity = trade.TakeProfit.Quantity,
            TakeProfitValue = trade.TakeProfit.Value,
            TakeProfitFee = trade.TakeProfit.Fee,
            StopLossQuantity = trade.StopLoss.Quantity,
            StopLossValue = trade.StopLoss.Value,
            StopLossFee = trade.StopLoss.Fee,
            ManualQuantity = trade.Manual.Quantity,
            ManualValue = trade.Manual.Value,
            ManualFee = trade.Manual.Fee,
            ExpiryQuantity = trade.Expiry.Quantity,
            ExpiryValue = trade.Expiry.Value,
            ExpiryFee = trade.Expiry.Fee,
            DayDrop = trade.Features.DayDrop,
            BtcDrop = trade.Features.BtcDrop,
            HighFunding = trade.Features.HighFunding,
            Burst = trade.Features.Burst,
            LastOrderNumber = trade.LastOrderNumber,
            CreatedAt = trade.CreatedAt,
            PromisedBadge = trade.Promise?.Badge?.ToCode(),
            PromisedProbability = trade.Promise?.Probability,
            PromisedExpectancy = trade.Promise?.Expectancy,
            PromisedCases = trade.Promise?.Cases,
            BreakEven = trade.Promise?.BreakEven,
            NetRiskReward = trade.Promise?.NetRiskReward,
            OpenedAt = trade.OpenedAt,
            AutoId = trade.AutoId,
            Leverage = trade.Leverage,
            SteppedUntil = steppedUntil
        };
    }

    /// <summary>A real trade reads no columns of the expiry, so its leg of the expiry is always none.</summary>
    internal static IdeaTrade ToDomain(IdeaTradeDb trade)
    {
        return new IdeaTrade(
            trade.Id,
            trade.IdeaId,
            trade.AccountId,
            new Instrument(MarketDataCodes.TryParseCategory(trade.Category, out MarketCategory category) ? category : throw Unknown("category", trade.Category), trade.Symbol),
            IdeaCodes.TryParseSetup(trade.Setup, out IdeaSetup setup) ? setup : throw Unknown("setup", trade.Setup),
            MarketDataCodes.TryParseInterval(trade.Interval, out Interval interval) ? interval : throw Unknown("interval", trade.Interval),
            IdeaCodes.TryParseType(trade.Type, out IdeaType type) ? type : throw Unknown("type", trade.Type),
            IdeaCodes.TryParseSide(trade.Side, out IdeaSide side) ? side : throw Unknown("side", trade.Side),
            trade.EntryPrice,
            trade.Stop,
            trade.Target,
            trade.Quantity,
            trade.Amount,
            trade.Risk,
            trade.EntryDeadline,
            IdeaTradeCodes.TryParseStatus(trade.Status, out IdeaTradeStatus status) ? status : throw Unknown("status", trade.Status),
            trade.StatusAt,
            trade.Reason,
            trade.CancelRequested,
            trade.CloseRequested,
            new LegTotals(trade.EntryQuantity, trade.EntryValue, trade.EntryFee),
            new LegTotals(trade.TakeProfitQuantity, trade.TakeProfitValue, trade.TakeProfitFee),
            new LegTotals(trade.StopLossQuantity, trade.StopLossValue, trade.StopLossFee),
            new LegTotals(trade.ManualQuantity, trade.ManualValue, trade.ManualFee),
            trade.LastOrderNumber,
            trade.CreatedAt,
            trade.NetRiskReward is { } netRiskReward
                ? new IdeaPromise(
                    trade.PromisedBadge is { } badge ? IdeaCodes.ParseBadge(badge) : null,
                    trade.PromisedProbability,
                    trade.PromisedExpectancy,
                    trade.PromisedCases,
                    trade.BreakEven,
                    netRiskReward)
                : null,
            trade.OpenedAt,
            trade.IdeaStatus is { } ideaStatus ? new TradedIdeaOutcome(IdeaCodes.ParseStatus(ideaStatus), trade.IdeaStatusAt!.Value, trade.IdeaResult, trade.IdeaExitPrice) : null,
            trade.AutoId,
            trade.Leverage)
        {
            Expiry = new LegTotals(trade.ExpiryQuantity, trade.ExpiryValue, trade.ExpiryFee),
            Features = new IdeaFeatures(trade.DayDrop, trade.BtcDrop, trade.HighFunding, trade.Burst)
        };
    }

    internal static IdeaTradeOrderDb FromDomain(IdeaTradeOrder order)
    {
        return new IdeaTradeOrderDb
        {
            TradeId = order.TradeId,
            Number = order.Number,
            Role = order.Role.ToCode(),
            Side = StrategyCodes.ToCode(order.Side),
            Type = StrategyCodes.ToCode(order.Type),
            TimeInForce = order.TimeInForce.ToCode(),
            Price = order.Price,
            Quantity = order.Quantity,
            TakeProfit = order.Protection?.TakeProfit,
            StopLoss = order.Protection?.StopLoss,
            ReduceOnly = order.ReduceOnly,
            Status = order.Status.ToCode(),
            FilledQuantity = order.FilledQuantity,
            FilledValue = order.FilledValue,
            Fee = order.Fee,
            ExchangeOrderId = order.ExchangeOrderId,
            RejectReason = order.RejectReason,
            Attempts = order.Attempts,
            NextAttemptAt = order.NextAttemptAt,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt
        };
    }

    internal static IdeaTradeOrder ToDomain(IdeaTradeOrderDb order)
    {
        return new IdeaTradeOrder(
            order.TradeId,
            order.Number,
            IdeaTradeCodes.TryParseRole(order.Role, out IdeaTradeOrderRole role) ? role : throw Unknown("order role", order.Role),
            StrategyCodes.TryParse(order.Side, out OrderSide side) ? side : throw Unknown("side", order.Side),
            StrategyCodes.TryParse(order.Type, out OrderType type) ? type : throw Unknown("type", order.Type),
            IdeaTradeCodes.TryParseTimeInForce(order.TimeInForce, out TimeInForce timeInForce) ? timeInForce : throw Unknown("time in force", order.TimeInForce),
            order.Price,
            order.Quantity,
            order.TakeProfit is { } takeProfit && order.StopLoss is { } stopLoss ? new Protection(takeProfit, stopLoss) : null,
            order.ReduceOnly,
            LiveTradingCodes.TryParseOrderStatus(order.Status, out LiveOrderStatus status) ? status : throw Unknown("order status", order.Status),
            order.FilledQuantity,
            order.FilledValue,
            order.Fee,
            order.ExchangeOrderId,
            order.RejectReason,
            order.Attempts,
            order.NextAttemptAt,
            order.CreatedAt,
            order.UpdatedAt);
    }

    internal static IdeaTradeChildDb FromDomain(IdeaTradeChild child)
    {
        return new IdeaTradeChildDb
        {
            TradeId = child.TradeId,
            ExchangeOrderId = child.ExchangeOrderId,
            Leg = child.Leg.ToCode(),
            FilledQuantity = child.FilledQuantity,
            FilledValue = child.FilledValue,
            Fee = child.Fee,
            Finished = child.Finished
        };
    }

    internal static IdeaTradeChild ToDomain(IdeaTradeChildDb child)
    {
        return new IdeaTradeChild(
            child.TradeId,
            child.ExchangeOrderId,
            IdeaTradeCodes.TryParseLeg(child.Leg, out IdeaTradeLeg leg) ? leg : throw Unknown("leg", child.Leg),
            child.FilledQuantity,
            child.FilledValue,
            child.Fee,
            child.Finished);
    }

    internal static IdeaTradeFillDb FromDomain(IdeaTradeFill fill)
    {
        return new IdeaTradeFillDb
        {
            ExecId = fill.ExecId,
            TradeId = fill.TradeId,
            ExchangeOrderId = fill.ExchangeOrderId,
            Quantity = fill.Quantity,
            Price = fill.Price,
            Fee = fill.Fee,
            FeeAsset = fill.FeeAsset.ToCode(),
            ExecutedAt = fill.ExecutedAt
        };
    }

    internal static IdeaTradeFill ToDomain(IdeaTradeFillDb fill)
    {
        return new IdeaTradeFill(
            fill.ExecId,
            fill.TradeId,
            fill.ExchangeOrderId,
            fill.Quantity,
            fill.Price,
            fill.Fee,
            LiveTradingCodes.TryParseFeeAsset(fill.FeeAsset, out FeeAsset asset) ? asset : throw Unknown("fee asset", fill.FeeAsset),
            fill.ExecutedAt);
    }

    internal static IdeaTradingSettings ToDomain(IdeaTradingSettingsDb settings)
    {
        return new IdeaTradingSettings(settings.AccountId, settings.Amount, settings.MaxAmount, settings.MaxOpenTrades, settings.MaxTotalRisk, settings.MinRiskReward, settings.DailyLossLimit, settings.MaxSameDirection, settings.AutosBudget, settings.Leverage);
    }

    private static InvalidOperationException Unknown(string what, string code)
    {
        return new InvalidOperationException($"An idea trade row has an unknown {what} '{code}'.");
    }
}