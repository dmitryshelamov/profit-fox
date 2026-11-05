using System.Globalization;

using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.LiveTrading;

namespace ProfitFox.Domain.Services.LiveTrading;

/// <summary>
///     Compares the books of the bots of one account and instrument with the exchange. It only names what differs: fixing is
///     never guessed, the bots stop.
/// </summary>
public static class Reconciliation
{
    /// <param name="category"></param>
    /// <param name="quantityStep">A difference below one step of the instrument is rounding, not a difference.</param>
    /// <param name="bots"></param>
    /// <param name="exchange"></param>
    /// <param name="trades">The trades by ideas on the same instrument: their positions count with the positions of the bots.</param>
    /// <returns>What differs, empty when the books match.</returns>
    public static IReadOnlyList<string> Check(MarketCategory category, decimal quantityStep, IReadOnlyList<BotHolding> bots, ExchangeHoldings exchange, IReadOnlyList<TradeHolding>? trades = null)
    {
        var differences = new List<string>();
        trades ??= [];
        string holders = trades.Count == 0 ? "bots" : "bots and the idea trades";

        if (category == MarketCategory.Linear)
        {
            decimal held = bots.Sum(bot => Signed(bot.Direction, bot.Quantity)) + trades.Sum(trade => Signed(trade.Direction, trade.Quantity));

            if (Math.Abs(exchange.Position - held) >= quantityStep)
            {
                differences.Add($"Exchange position {Text(exchange.Position)} does not match the positions of the {holders} {Text(held)}.");
            }
        }
        else
        {
            decimal held = bots.Sum(bot => bot.Quantity) + trades.Sum(trade => trade.Quantity);

            // Other coins of the same kind may lie on the account; only fewer than the bots hold is a difference.
            if (held - exchange.Position >= quantityStep)
            {
                differences.Add($"Exchange holds {Text(exchange.Position)} of the coin, less than the positions of the {holders} {Text(held)}.");
            }
        }

        foreach (BotHolding bot in bots)
        {
            foreach (int number in bot.ActiveOrders)
            {
                var id = new ClientOrderId(bot.BotId, number);

                if (!exchange.OpenOrders.Contains(id) && !exchange.FinishedOrders.Contains(id))
                {
                    differences.Add($"Order {id.Code} of the bot is not on the exchange.");
                }
            }

            foreach (ClientOrderId open in exchange.OpenOrders.Where(open => open.BotId == bot.BotId && !bot.ActiveOrders.Contains(open.Number)))
            {
                differences.Add($"Order {open.Code} of the bot is unknown.");
            }
        }

        foreach (TradeHolding trade in trades)
        {
            foreach (int number in trade.ActiveOrders)
            {
                var id = new IdeaTradeOrderId(trade.TradeId, number);

                if (exchange.OpenTradeOrders?.Contains(id) != true && exchange.FinishedTradeOrders?.Contains(id) != true)
                {
                    differences.Add($"Order {id.Code} of the idea trade is not on the exchange.");
                }
            }

            foreach (IdeaTradeOrderId open in exchange.OpenTradeOrders?.Where(open => open.TradeId == trade.TradeId && !trade.ActiveOrders.Contains(open.Number)) ?? [])
            {
                differences.Add($"Order {open.Code} of the idea trade is unknown.");
            }
        }

        return differences;
    }

    private static decimal Signed(StrategyDirection direction, decimal quantity)
    {
        return direction == StrategyDirection.Long ? quantity : -quantity;
    }

    private static string Text(decimal value)
    {
        return value.ToString("0.############################", CultureInfo.InvariantCulture);
    }
}