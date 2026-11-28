using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Trades;

namespace ProfitFox.Tests.Unit.Domain.Services.Trades;

/// <summary>
///     Change take-idea-trades, task 3.4: what a fill is to a trade and why the trade closed (design.md, decision 5), and the
///     outcome (decision 6; spec idea-trades «Причина закрытия», «Итог сделки»).
/// </summary>
public sealed class TradeResultTests
{
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 11, 28, 14, 0, 0, TimeSpan.Zero);

    // Spec «Сработал стоп на бирже».
    [Test]
    public void The_fill_of_the_stop_of_the_position_is_by_the_stop()
    {
        Assert.That(Kind(Fill(TradeSide.Short, stop: ExchangeStopType.StopLoss)), Is.EqualTo((FillKind.Exit, (TradeCloseReason?)TradeCloseReason.Stop)));
        Assert.That(Kind(Fill(TradeSide.Short, stop: ExchangeStopType.TakeProfit)), Is.EqualTo((FillKind.Exit, (TradeCloseReason?)TradeCloseReason.Target)));
    }

    // Spec «Ликвидация».
    [Test]
    public void A_liquidation_and_an_auto_deleveraging_are_named()
    {
        Assert.That(Kind(Fill(TradeSide.Short, type: ExchangeFillType.Liquidation)), Is.EqualTo((FillKind.Exit, (TradeCloseReason?)TradeCloseReason.Liquidation)));
        Assert.That(Kind(Fill(TradeSide.Short, type: ExchangeFillType.Adl)), Is.EqualTo((FillKind.Exit, (TradeCloseReason?)TradeCloseReason.Adl)));
    }

    // Spec «Владелец закрыл на Bybit».
    [Test]
    public void A_fill_against_the_trade_from_no_order_of_the_bench_is_on_the_exchange()
    {
        Assert.That(Kind(Fill(TradeSide.Short, link: "web-123")), Is.EqualTo((FillKind.Exit, (TradeCloseReason?)TradeCloseReason.Exchange)));
    }

    [Test]
    public void A_fill_with_the_trade_from_no_order_of_the_bench_makes_the_position_bigger()
    {
        Assert.That(Kind(Fill(TradeSide.Long)), Is.EqualTo((FillKind.Increase, (TradeCloseReason?)null)));
    }

    [Test]
    public void The_fills_of_the_orders_of_the_bench_are_its_entry_and_its_close_with_the_reason()
    {
        Trade trade = TradeGenerator.Long();
        TradeOrder[] orders = [TradeGenerator.Order(trade, 1, TradeOrderPurpose.Entry), TradeGenerator.Order(trade, 2, TradeOrderPurpose.Close, TradeCloseReason.Time)];
        Assert.That(FillKinds.Of(Fill(TradeSide.Long, link: orders[0].LinkId), trade, orders), Is.EqualTo((FillKind.Entry, (TradeCloseReason?)null)));
        Assert.That(FillKinds.Of(Fill(TradeSide.Short, link: orders[1].LinkId), trade, orders), Is.EqualTo((FillKind.Exit, (TradeCloseReason?)TradeCloseReason.Time)));
    }

    [Test]
    public void An_order_of_another_trade_of_the_bench_is_not_this_one()
    {
        Assert.That(Kind(Fill(TradeSide.Short, link: TradeOrderId.Format(Guid.NewGuid(), 2))), Is.EqualTo((FillKind.Exit, (TradeCloseReason?)TradeCloseReason.Exchange)));
    }

    // The order of the rules: a liquidation is told by its kind before anything else, and the order of the bench before the
    // mark of the stop.
    [Test]
    public void The_first_rule_that_holds_decides()
    {
        Trade trade = TradeGenerator.Long();
        TradeOrder[] orders = [TradeGenerator.Order(trade, 2, TradeOrderPurpose.Close, TradeCloseReason.Manual)];
        Assert.That(
            FillKinds.Of(Fill(TradeSide.Short, type: ExchangeFillType.Liquidation, stop: ExchangeStopType.StopLoss, link: orders[0].LinkId), trade, orders).Reason,
            Is.EqualTo(TradeCloseReason.Liquidation));
        Assert.That(FillKinds.Of(Fill(TradeSide.Short, stop: ExchangeStopType.StopLoss, link: orders[0].LinkId), trade, orders).Reason, Is.EqualTo(TradeCloseReason.Manual));
        Assert.That(FillKinds.Of(Fill(TradeSide.Short, type: ExchangeFillType.Funding, stop: ExchangeStopType.StopLoss), trade, orders).Kind, Is.EqualTo(FillKind.Funding));
    }

    // Spec «Прибыльный лонг».
    [Test]
    public void A_long_bought_at_237_40_and_sold_at_245_54_with_fees_of_0_10_makes_3_16()
    {
        var result = TradeResult.Of(TradeSide.Long, [Kept("e1", TradeSide.Long, 237.40m, 0.4m, 0.05m, FillKind.Entry), Kept("x1", TradeSide.Short, 245.54m, 0.4m, 0.05m, FillKind.Exit, TradeCloseReason.Target, 1)]);
        Assert.Multiple(() =>
        {
            Assert.That(decimal.Round(result.Result, 2), Is.EqualTo(3.16m));
            Assert.That(result.Held, Is.EqualTo(0m));
            Assert.That(result.Fees, Is.EqualTo(0.10m));
            Assert.That(result.EntryPrice, Is.EqualTo(237.40m));
            Assert.That(result.ExitPrice, Is.EqualTo(245.54m));
            Assert.That(result.LastExit!.Reason, Is.EqualTo(TradeCloseReason.Target));
        });
    }

    // Spec «Выплата фандинга».
    [Test]
    public void Funding_paid_to_a_short_makes_the_outcome_bigger_by_it()
    {
        TradeFill[] fills = [Kept("e1", TradeSide.Short, 1.4126m, 70m, 0.05m, FillKind.Entry), Kept("x1", TradeSide.Long, 1.40m, 70m, 0.05m, FillKind.Exit, TradeCloseReason.Time, 2)];
        decimal without = TradeResult.Of(TradeSide.Short, fills).Result;
        var with = TradeResult.Of(TradeSide.Short, [.. fills, Kept("f1", TradeSide.Short, 1.41m, 70m, -0.05m, FillKind.Funding, null, 1)]);
        Assert.That(with.Result - without, Is.EqualTo(0.05m));
        Assert.That(with.Funding, Is.EqualTo(0.05m));
        Assert.That(with.Fees, Is.EqualTo(0.10m));
    }

    // Spec «Ликвидация»: the outcome counts the lost margin.
    [Test]
    public void A_liquidated_short_loses_its_margin()
    {
        var result = TradeResult.Of(TradeSide.Short, [Kept("e1", TradeSide.Short, 100m, 1.5m, 0.08m, FillKind.Entry), Kept("b1", TradeSide.Long, 110m, 1.5m, 0.09m, FillKind.Exit, TradeCloseReason.Liquidation, 1)]);
        Assert.That(result.Result, Is.EqualTo(-15.17m));
    }

    [Test]
    public void A_part_closed_leaves_the_rest_held_and_counts_what_was_closed()
    {
        var result = TradeResult.Of(TradeSide.Long, [Kept("e1", TradeSide.Long, 100m, 2m, 0m, FillKind.Entry), Kept("x1", TradeSide.Short, 110m, 1m, 0m, FillKind.Exit, TradeCloseReason.Exchange, 1)]);
        Assert.That((result.Held, result.Result, result.Unrealised(TradeSide.Long, 105m)), Is.EqualTo((1m, 10m, 5m)));
    }

    // 12.18 (N4): the entry from the snapshot of the position counts until the fills of the entry come, then gives way.
    [Test]
    public void The_entry_from_a_snapshot_gives_way_to_the_fills_of_the_entry()
    {
        var trade = Guid.NewGuid();
        var snapshot = new TradeFill(TradeResult.SnapshotId(trade), trade, TradeSide.Long, 237.40m, 0.4m, 0m, FillKind.Entry, null, At);

        var alone = TradeResult.Of(TradeSide.Long, [snapshot]);
        var real = TradeResult.Of(TradeSide.Long, [snapshot, Kept("e1", TradeSide.Long, 237.42m, 0.4m, 0.05m, FillKind.Entry, null, 1)]);

        Assert.That((alone.Entered, alone.EntryPrice), Is.EqualTo((0.4m, (decimal?)237.40m)));
        Assert.That((real.Entered, real.EntryPrice, real.Fees), Is.EqualTo((0.4m, (decimal?)237.42m, 0.05m)));
    }

    private static (FillKind Kind, TradeCloseReason? Reason) Kind(ExchangeFill fill)
    {
        return FillKinds.Of(fill, TradeGenerator.Long(), []);
    }

    private static ExchangeFill Fill(TradeSide side, ExchangeFillType type = ExchangeFillType.Trade, ExchangeStopType stop = ExchangeStopType.None, string? link = null)
    {
        return new ExchangeFill("e", "SOLUSDT", link, side, 100m, 1m, 0.05m, type, stop, At);
    }

    private static TradeFill Kept(string id, TradeSide side, decimal price, decimal quantity, decimal fee, FillKind kind, TradeCloseReason? reason = null, int minutes = 0)
    {
        return new TradeFill(id, Guid.Empty, side, price, quantity, fee, kind, reason, At.AddMinutes(minutes));
    }
}
