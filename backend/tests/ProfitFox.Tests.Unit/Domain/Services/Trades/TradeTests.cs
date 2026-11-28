using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Trades;

namespace ProfitFox.Tests.Unit.Domain.Services.Trades;

/// <summary>
///     Change take-idea-trades, task 3.1: the states of a trade and its moves (design.md, decisions 10 and 12; spec idea-trades
///     «Состояния сделки»).
/// </summary>
public sealed class TradeTests
{
    private static readonly DateTimeOffset At = TradeGenerator.Click;

    // Spec «Обычный путь».
    [Test]
    public void An_entry_filled_then_the_target_goes_pending_open_closed_by_the_target()
    {
        Trade trade = TradeGenerator.Long();
        Assert.That(trade.State, Is.EqualTo(TradeState.Pending));
        TradeFill entry = Fill("e1", TradeSide.Long, 237.40m, FillKind.Entry, null, 1);
        trade.Apply([entry]);
        Assert.That((trade.State, trade.OpenedAt), Is.EqualTo((TradeState.Open, (DateTimeOffset?)At.AddSeconds(1))));
        trade.Apply([entry, Fill("x1", TradeSide.Short, 245.54m, FillKind.Exit, TradeCloseReason.Target, 60)]);
        Assert.Multiple(() =>
        {
            Assert.That(trade.State, Is.EqualTo(TradeState.Closed));
            Assert.That(trade.CloseReason, Is.EqualTo(TradeCloseReason.Target));
            Assert.That(trade.ClosedAt, Is.EqualTo(At.AddSeconds(60)));
            Assert.That(trade.Result, Is.EqualTo(3.256m));
        });
    }

    // Spec «Вход не состоялся».
    [Test]
    public void An_entry_refused_by_Bybit_cancels_the_trade_with_its_reason()
    {
        Trade trade = TradeGenerator.Long();
        trade.Cancel("Bybit: ab not enough for new order", At);
        Assert.That((trade.State, trade.Reason, trade.Result), Is.EqualTo((TradeState.Cancelled, "Bybit: ab not enough for new order", (decimal?)null)));
    }

    [Test]
    public void A_move_the_table_does_not_have_throws()
    {
        Trade trade = TradeGenerator.Long();
        Assert.Throws<InvalidOperationException>(trade.StartClosing);
        trade.Cancel("нет", At);
        Assert.Throws<InvalidOperationException>(() => trade.Fail("нет"));
        Assert.Throws<InvalidOperationException>(() => trade.Close(TradeCloseReason.Manual, At, 0m));
    }

    [Test]
    public void A_refused_close_leaves_the_trade_open_with_the_reason()
    {
        Trade trade = Opened();
        trade.StartClosing();
        trade.CloseRefused("Bybit: reduce-only order has same side with current position");
        Assert.That((trade.State, trade.Reason), Is.EqualTo((TradeState.Open, "Bybit: reduce-only order has same side with current position")));
        trade.StartClosing();
        Assert.That(trade.State, Is.EqualTo(TradeState.Closing));
    }

    [Test]
    public void A_fill_that_makes_the_position_bigger_puts_the_trade_in_error()
    {
        Trade trade = Opened();
        trade.Apply([Fill("e1", TradeSide.Long, 237.40m, FillKind.Entry, null, 1), Fill("i1", TradeSide.Long, 238m, FillKind.Increase, null, 30)]);
        Assert.That(trade.State, Is.EqualTo(TradeState.Error));
    }

    [Test]
    public void A_trade_in_error_takes_its_numbers_and_does_not_move_by_them()
    {
        Trade trade = Opened();
        trade.Fail("позиция другой стороны");
        trade.Apply([Fill("e1", TradeSide.Long, 237.40m, FillKind.Entry, null, 1), Fill("x1", TradeSide.Short, 240m, FillKind.Exit, TradeCloseReason.Exchange, 30)]);
        Assert.That((trade.State, trade.Numbers.Held), Is.EqualTo((TradeState.Error, 0m)));
        trade.Close(TradeCloseReason.Exchange, At.AddMinutes(5), trade.Numbers.Result);
        Assert.That((trade.State, trade.CloseReason), Is.EqualTo((TradeState.Closed, (TradeCloseReason?)TradeCloseReason.Exchange)));
    }

    [Test]
    public void A_part_closed_leaves_the_trade_open()
    {
        Trade trade = Opened();
        trade.Apply([Fill("e1", TradeSide.Long, 237.40m, FillKind.Entry, null, 1), Fill("x1", TradeSide.Short, 240m, FillKind.Exit, TradeCloseReason.Exchange, 30, 0.2m)]);
        Assert.That((trade.State, trade.Numbers.Held), Is.EqualTo((TradeState.Open, 0.2m)));
    }

    [Test]
    public void The_states_that_hold_the_pair_are_pending_open_closing_and_error()
    {
        Assert.That(
            Enum.GetValues<TradeState>().Where(Trade.IsHolding),
            Is.EqualTo(new[] { TradeState.Pending, TradeState.Open, TradeState.Closing, TradeState.Error }));
    }

    private static Trade Opened()
    {
        Trade trade = TradeGenerator.Long();
        trade.Apply([Fill("e1", TradeSide.Long, 237.40m, FillKind.Entry, null, 1)]);
        return trade;
    }

    private static TradeFill Fill(string id, TradeSide side, decimal price, FillKind kind, TradeCloseReason? reason, int seconds, decimal quantity = 0.4m)
    {
        return new TradeFill(id, Guid.Empty, side, price, quantity, 0m, kind, reason, At.AddSeconds(seconds));
    }
}
