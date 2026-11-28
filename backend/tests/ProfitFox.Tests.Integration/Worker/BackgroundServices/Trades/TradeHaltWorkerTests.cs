using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Application.Commands.Trades.CloseTrade;
using ProfitFox.Application.Commands.Trades.LiftHalt;
using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Integration.Application.Commands.Trades;

namespace ProfitFox.Tests.Integration.Worker.BackgroundServices.Trades;

/// <summary>
///     Change take-idea-trades, task 7.9 (design.md, decision 11; spec trade-risk «Убыток за день», «Стоп-кран», «Снятие
///     запрета»): the results of the open trades by the stream of the prices — by REST while it is lost —, the emergency stop
///     and its lift, on <see cref="Exchange.FakeBybit" />. The limit of the loss of a day is one dollar, so the prices move by a
///     few cents and the stops of the trades stay out of the way.
/// </summary>
[NonParallelizable]
public sealed class TradeHaltWorkerTests
{
    private TraderBench _bench = null!;

    [SetUp]
    public async Task Start()
    {
        _bench = await TraderBench.Start(maxDayLoss: 1m);
    }

    [TearDown]
    public async Task Stop()
    {
        await _bench.DisposeAsync();
    }

    // Spec «Порог пройден»: both open trades close by the market with «стоп-кран», the entry is banned, the moment, the loss
    // and the limit are written.
    [Test]
    public async Task A_loss_of_the_day_at_the_limit_closes_every_open_trade_and_bans_the_entry()
    {
        await _bench.Worker();
        Trade sol = await _bench.Opened();
        Trade xrp = await _bench.Enter(await _bench.Idea("XRPUSDT", EntrySide.Short, 1.4051, null, null));
        await _bench.Until(xrp.Id, TradeState.Open);

        _bench.Bybit.Price("SOLUSDT", 235m);
        _bench.Bybit.Price("XRPUSDT", 1.43m);

        Assert.That((await _bench.Until(sol.Id, TradeState.Closed)).CloseReason, Is.EqualTo(TradeCloseReason.Halt));
        Assert.That((await _bench.Until(xrp.Id, TradeState.Closed)).CloseReason, Is.EqualTo(TradeCloseReason.Halt));
        TradeHalt halt = await _bench.Halt();
        Assert.That((halt.Halted, halt.Limit), Is.EqualTo((true, (decimal?)1m)));
        Assert.That(halt.Loss, Is.GreaterThanOrEqualTo(1m));
        TradeEntryRefusedException? refused = Assert.ThrowsAsync<TradeEntryRefusedException>(async () => await _bench.Enter(await _bench.Idea()));
        Assert.That(refused!.Failed.Select(check => check.Text), Does.Contain("Вход запрещён стоп-краном"));
    }

    // Spec «Стенд не работал»: the stop goes off at the first count after the start.
    [Test]
    public async Task A_loss_past_the_limit_while_no_Worker_worked_sets_the_stop_off_after_the_start()
    {
        WorkerHost worker = await _bench.Worker();
        Trade trade = await _bench.Opened();
        await _bench.Stop(worker);
        _bench.Bybit.Price("SOLUSDT", 233m);

        await _bench.Worker("trader-2");

        Assert.That((await _bench.Until(trade.Id, TradeState.Closed)).CloseReason, Is.EqualTo(TradeCloseReason.Halt));
        Assert.That((await _bench.Halt()).Halted, Is.True);
    }

    // Spec «Сделка в „Ошибке“»: the stop does not close it.
    [Test]
    public async Task The_emergency_stop_leaves_a_trade_in_error()
    {
        await _bench.Worker();
        Trade error = await _bench.Opened();
        _bench.Bybit.ByHand(TradeBench.MainKey, "SOLUSDT", TradeSide.Long, 0.1m);
        await _bench.Until(error.Id, TradeState.Error);
        Trade xrp = await _bench.Enter(await _bench.Idea("XRPUSDT", EntrySide.Short, 1.4051, null, null));
        await _bench.Until(xrp.Id, TradeState.Open);

        _bench.Bybit.Price("XRPUSDT", 1.44m);

        Assert.That((await _bench.Until(xrp.Id, TradeState.Closed)).CloseReason, Is.EqualTo(TradeCloseReason.Halt));
        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.That((await _bench.Read(error.Id)).State, Is.EqualTo(TradeState.Error));
    }

    // Spec «Запрет снят»: once every trade closed, the lift allows the entry and the loss of the day counts from the lift.
    [Test]
    public async Task A_lifted_ban_allows_the_entry_and_the_loss_counts_from_the_lift()
    {
        await _bench.Worker();
        Trade trade = await _bench.Opened();
        _bench.Bybit.Price("SOLUSDT", 233m);
        await _bench.Until(trade.Id, TradeState.Closed);
        _bench.Bybit.Price("SOLUSDT", 237.40m);

        TradeHalt lifted = await _bench.Sender.Send(new LiftHaltCommand());

        Assert.That((lifted.Halted, lifted.LiftedAt), Is.EqualTo((false, lifted.LiftedAt)));
        (IReadOnlyList<(DateTimeOffset ClosedAt, decimal Result)> closed, _) = await _bench.Trades.DayAsync(DayLoss.Start(DateTimeOffset.UtcNow, lifted.LiftedAt), CancellationToken.None);
        Assert.That(closed, Is.Empty, "the loss of the day is nought after the lift");
        Trade next = await _bench.Enter(await _bench.Idea());
        await _bench.Until(next.Id, TradeState.Open);
        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.That((await _bench.Halt()).Halted, Is.False, "the trades closed before the lift do not set it off again");
    }

    // Spec «Ещё закрываются».
    [Test]
    public async Task The_ban_is_not_lifted_while_a_trade_closes()
    {
        WorkerHost worker = await _bench.Worker();
        Trade trade = await _bench.Opened();
        await _bench.Stop(worker);
        await _bench.Pretend();
        await _bench.Sender.Send(new CloseTradeCommand(trade.Id));
        await _bench.Api.Services.GetRequiredService<ITradeHaltRepository>().HaltAsync("nobody", 52.10m, 50m, DateTimeOffset.UtcNow, CancellationToken.None);

        TradeConflictException? refused = Assert.ThrowsAsync<TradeConflictException>(() => _bench.Sender.Send(new LiftHaltCommand()));

        Assert.That(refused!.Message, Does.StartWith("Дождитесь закрытия сделок"));
        Assert.That((await _bench.Halt()).Halted, Is.True);
    }

    // Spec «Запрет переживает сутки».
    [Test]
    public async Task A_ban_of_yesterday_still_holds_today()
    {
        await _bench.Worker();
        await using (NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("INSERT INTO trade_halt (id, halted_at, loss, limit_value) VALUES (1, now() - interval '1 day', 52.10, 50)");
        }

        await Task.Delay(TimeSpan.FromSeconds(0.3));

        Assert.That((await _bench.Halt()).Halted, Is.True);
        Assert.ThrowsAsync<TradeEntryRefusedException>(async () => await _bench.Enter(await _bench.Idea()));
    }

    // The result of an open trade comes by the stream of the prices, and by the prices of REST while that stream is lost.
    [Test]
    public async Task The_result_now_follows_the_stream_of_the_prices_and_REST_while_it_is_lost()
    {
        await _bench.Api.Services.GetRequiredService<ITradeSettingsRepository>().SaveAsync(TradeBench.Limits with { MaxDayLoss = 50m, ExpiryMargin = TimeSpan.Zero }, CancellationToken.None);
        await _bench.Worker();
        Trade trade = await _bench.Opened();
        await Poll.Until("the stream of the prices of the pair is open", () => Task.FromResult(_bench.Bybit.PriceSymbols().SequenceEqual(["SOLUSDT"])));

        _bench.Bybit.Price("SOLUSDT", 238.40m);
        await Poll.Until("the result by the stream", async () => (await _bench.Read(trade.Id)).Unrealised == 0.4m);

        _bench.Bybit.PricesLost(true);
        int polled = _bench.CallsOf("tickers");
        _bench.Bybit.Price("SOLUSDT", 236.40m);
        await Poll.Until("the result by REST", async () => (await _bench.Read(trade.Id)).Unrealised == -0.4m);
        Assert.That(_bench.CallsOf("tickers"), Is.GreaterThan(polled));
    }

    // The result of the open trades is written at most once in MarkInterval: here 3 seconds, as in production.
    [Test]
    public async Task The_result_now_is_written_at_most_once_in_three_seconds()
    {
        await _bench.Api.Services.GetRequiredService<ITradeSettingsRepository>().SaveAsync(TradeBench.Limits with { MaxDayLoss = 50m, ExpiryMargin = TimeSpan.Zero }, CancellationToken.None);
        await _bench.Worker(trading: WorkerHost.Trading(mark: "00:00:03"));
        Trade trade = await _bench.Opened();
        var marks = new List<DateTimeOffset>();

        for (int step = 0; step < 40; step++)
        {
            _bench.Bybit.Price("SOLUSDT", 237.40m + step * 0.01m);
            if ((await _bench.Read(trade.Id)).MarkedAt is { } marked && !marks.Contains(marked))
            {
                marks.Add(marked);
            }

            await Task.Delay(TimeSpan.FromSeconds(0.1));
        }

        Assert.That(marks, Has.Count.InRange(1, 2));
        if (marks.Count == 2)
        {
            Assert.That(marks[1] - marks[0], Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(3)));
        }
    }
}
