using System.Text.Json;

using ProfitFox.Application.LiveTrading;
using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.LiveTrading.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.LiveTrading.Converters;

internal static class LiveTradingDbConverter
{
    internal static LiveBotDb FromDomain(LiveBot bot)
    {
        return new LiveBotDb
        {
            BotId = bot.BotId,
            Status = bot.Status.ToCode(),
            StopReason = bot.StopReason,
            StrategyVersion = bot.StrategyVersion,
            ParameterValues = StrategyJson.WriteValues(bot.ParameterValues).ToJsonString(),
            StartPending = bot.StartPending,
            CloseRequested = bot.CloseRequested,
            LastOrderNumber = bot.LastOrderNumber,
            LastBarAt = bot.LastBarAt,
            SessionFailures = bot.SessionFailures
        };
    }

    internal static LiveBot ToDomain(LiveBotDb bot)
    {
        using var values = JsonDocument.Parse(bot.ParameterValues);

        return new LiveBot(
            bot.BotId,
            LiveTradingCodes.TryParseStatus(bot.Status, out LiveBotStatus status) ? status : throw Unknown("status", bot.Status),
            bot.StopReason,
            bot.StrategyVersion,
            StrategyJson.ParseValues(values.RootElement),
            bot.StartPending,
            bot.CloseRequested,
            bot.LastOrderNumber,
            bot.LastBarAt,
            bot.SessionFailures);
    }

    internal static LivePositionDb FromDomain(LivePosition position)
    {
        return new LivePositionDb
        {
            BotId = position.BotId,
            Number = position.Number,
            Open = position.Open,
            Closing = position.Closing,
            Quantity = position.Quantity,
            Invested = position.Invested,
            Collateral = position.Collateral,
            RealizedPnl = position.RealizedPnl,
            Fees = position.Fees,
            LastEntryPrice = position.LastEntryPrice,
            LastEntryAt = position.LastEntryAt,
            SkippedOrders = position.SkippedOrders,
            ClosedAt = position.ClosedAt
        };
    }

    internal static LivePosition ToDomain(LivePositionDb position)
    {
        return new LivePosition(
            position.BotId,
            position.Number,
            position.Open,
            position.Closing,
            position.Quantity,
            position.Invested,
            position.Collateral,
            position.RealizedPnl,
            position.Fees,
            position.LastEntryPrice,
            position.LastEntryAt,
            position.SkippedOrders,
            position.ClosedAt);
    }

    internal static LiveOrderDb FromDomain(LiveOrder order)
    {
        return new LiveOrderDb
        {
            BotId = order.BotId,
            Number = order.Number,
            PositionNumber = order.PositionNumber,
            NodePath = order.NodePath,
            Side = StrategyCodes.ToCode(order.Side),
            Type = StrategyCodes.ToCode(order.Type),
            Price = order.Price,
            Quantity = order.Quantity,
            Amount = order.Amount,
            Reserve = order.Reserve,
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

    internal static LiveOrder ToDomain(LiveOrderDb order)
    {
        return new LiveOrder(
            order.BotId,
            order.Number,
            order.PositionNumber,
            order.NodePath,
            StrategyCodes.TryParse(order.Side, out OrderSide side) ? side : throw Unknown("side", order.Side),
            StrategyCodes.TryParse(order.Type, out OrderType type) ? type : throw Unknown("type", order.Type),
            order.Price,
            order.Quantity,
            order.Amount,
            order.Reserve,
            LiveTradingCodes.TryParseOrderStatus(order.Status, out LiveOrderStatus status) ? status : throw Unknown("order status", order.Status),
            order.FilledQuantity,
            order.ExchangeOrderId,
            order.RejectReason,
            order.Attempts,
            order.NextAttemptAt,
            order.CreatedAt,
            order.UpdatedAt,
            order.FilledValue,
            order.Fee);
    }

    internal static LiveFillDb FromDomain(LiveFill fill)
    {
        return new LiveFillDb
        {
            ExecId = fill.ExecId,
            BotId = fill.BotId,
            OrderNumber = fill.OrderNumber,
            Quantity = fill.Quantity,
            Price = fill.Price,
            Fee = fill.Fee,
            FeeAsset = fill.FeeAsset.ToCode(),
            ExecutedAt = fill.ExecutedAt
        };
    }

    internal static LiveFill ToDomain(LiveFillDb fill)
    {
        return new LiveFill(
            fill.ExecId,
            fill.BotId,
            fill.OrderNumber,
            fill.Quantity,
            fill.Price,
            fill.Fee,
            LiveTradingCodes.TryParseFeeAsset(fill.FeeAsset, out FeeAsset asset) ? asset : throw Unknown("fee asset", fill.FeeAsset),
            fill.ExecutedAt);
    }

    private static InvalidOperationException Unknown(string what, string code)
    {
        return new InvalidOperationException($"A live trading row has an unknown {what} '{code}'.");
    }
}