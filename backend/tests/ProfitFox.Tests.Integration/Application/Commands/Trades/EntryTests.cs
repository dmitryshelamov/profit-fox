using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Commands.Trades.OpenTrade;
using ProfitFox.Application.Contracts.Trades;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Trades;
using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Accounts;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Tests.Integration.Application.Commands.Trades;

/// <summary>
///     Change take-idea-trades, tasks 6.1 and 6.2 (design.md, decisions 2 and 7): the count of an entry and the entry on data
///     read anew from the base and from <see cref="Exchange.FakeBybit" />; the entry writes the trade with its order and sends
///     nothing to the exchange.
/// </summary>
[NonParallelizable]
public sealed class EntryTests
{
    private TradeBench _bench = null!;

    [SetUp]
    public async Task Start()
    {
        _bench = await TradeBench.Start();
    }

    [TearDown]
    public async Task Stop()
    {
        await _bench.DisposeAsync();
    }

    // Spec «Расчёт лонга».
    [Test]
    public async Task The_count_of_a_long_of_100_USDT_passes_every_check_with_the_free_money_of_Bybit()
    {
        Idea idea = await _bench.Idea();

        EntryPreviewInternal preview = await _bench.Sender.Send(new PreviewEntryQuery(idea.Id, null, null));

        Assert.Multiple(() =>
        {
            Assert.That(preview.Calculation.Passed, Is.True, string.Join("; ", preview.Calculation.Checks.Where(check => !check.Passed).Select(check => check.Text)));
            Assert.That((preview.Calculation.Quantity, preview.Calculation.Amount, preview.Calculation.Leverage), Is.EqualTo((0.4m, 100m, 3)));
            Assert.That((preview.AccountId, preview.AccountName, preview.Free), Is.EqualTo(((Guid?)_bench.Main.Id, "Основной", (decimal?)1250.40m)));
            Assert.That(_bench.Bybit.Calls.Select(call => call.Call), Does.Contain("wallet-balance").And.Contain("query-api").And.Contain("account-info"));
            Assert.That(_bench.Bybit.Calls.Select(call => call.Call), Does.Not.Contain("order-create").And.Not.Contain("set-leverage"));
        });
    }

    // Spec «Проверка не прошла».
    [Test]
    public async Task A_check_that_fails_is_marked_and_the_count_is_still_whole()
    {
        Idea idea = await _bench.Idea();

        EntryPreviewInternal preview = await _bench.Sender.Send(new PreviewEntryQuery(idea.Id, null, 250m));

        Assert.That(preview.Calculation.Checks.Single(check => !check.Passed), Is.EqualTo(new EntryCheck("amount", false, "Сумма больше предела 200 USDT")));
        Assert.That(preview.Calculation.Quantity, Is.EqualTo(1m));
    }

    // Spec trade-risk «Ключу включили вывод».
    [Test]
    public async Task A_key_that_may_withdraw_now_is_refused()
    {
        Idea idea = await _bench.Idea();
        _bench.Bybit.Key(TradeBench.MainKey).KeyInfo = new ExchangeKeyInfo(104857321, false, true, ["Withdraw"], ["Order", "Position"]);

        EntryPreviewInternal preview = await _bench.Sender.Send(new PreviewEntryQuery(idea.Id, null, null));

        Assert.That(preview.Calculation.Checks.Single(check => check.Code == "key"), Is.EqualTo(new EntryCheck("key", false, "У ключа аккаунта есть право вывода")));
    }

    // Spec «Торговый Worker не работает».
    [Test]
    public async Task Without_a_trading_Worker_the_entry_is_refused()
    {
        await _bench.Services.GetRequiredService<ITraderLeaseRepository>().ReleaseAsync("trader-test", CancellationToken.None);
        Idea idea = await _bench.Idea();

        TradeEntryRefusedException? refused = Assert.ThrowsAsync<TradeEntryRefusedException>(() => _bench.Sender.Send(new OpenTradeCommand(idea.Id, null, null)));

        Assert.That(refused!.Failed, Is.EqualTo(new[] { new EntryCheck("trader", false, "Торговый Worker не работает") }));
    }

    // Spec «Кросс-маржа».
    [Test]
    public async Task An_account_of_the_cross_margin_is_refused_and_its_mode_is_kept()
    {
        Idea idea = await _bench.Idea();
        _bench.Bybit.Key(TradeBench.MainKey).MarginMode = "REGULAR_MARGIN";

        EntryPreviewInternal preview = await _bench.Sender.Send(new PreviewEntryQuery(idea.Id, null, null));

        Assert.That(preview.Calculation.Checks.Single(check => check.Code == "margin").Text, Is.EqualTo("Аккаунт в режиме кросс-маржи, переключите его на Bybit на изолированную маржу"));
        Assert.That((await _bench.Services.GetRequiredService<IExchangeAccountRepository>().FindAsync(_bench.Main.Id, CancellationToken.None))!.MarginMode, Is.EqualTo("REGULAR_MARGIN"));
    }

    // Spec «Лонг по идее».
    [Test]
    public async Task A_long_is_written_with_its_idea_strategy_version_timeframe_account_and_entry_order_and_nothing_goes_to_Bybit()
    {
        Idea idea = await _bench.Idea();

        Trade trade = await _bench.Sender.Send(new OpenTradeCommand(idea.Id, null, null));

        TradeBook book = (await _bench.Services.GetRequiredService<ITradeRepository>().HoldingAsync(CancellationToken.None)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(book.Trade.Id, Is.EqualTo(trade.Id));
            Assert.That((book.Trade.IdeaId, book.Trade.Strategy, book.Trade.Version, book.Trade.Timeframe), Is.EqualTo((idea.Id, "breakout-long", 7, TimeSpan.FromHours(1))));
            Assert.That((book.Trade.AccountId, book.Trade.AccountName, book.Trade.Side, book.Trade.State), Is.EqualTo(((Guid?)_bench.Main.Id, "Основной", TradeSide.Long, TradeState.Pending)));
            Assert.That((book.Trade.Quantity, book.Trade.Stop, book.Trade.StopSource, book.Trade.Target, book.Trade.Leverage), Is.EqualTo((0.4m, 231.38m, StopSource.Idea, (decimal?)245.54m, 3)));
            Assert.That((book.Trade.EntryWait, book.Trade.StopAttempts, book.Trade.Until), Is.EqualTo((TimeSpan.FromSeconds(30), 3, idea.Until)));
            Assert.That(book.Orders.Single(), Is.EqualTo(book.Orders.Single() with { Number = 1, Purpose = TradeOrderPurpose.Entry, Status = TradeOrderStatus.Pending, Quantity = 0.4m }));
            Assert.That(trade.CreatedAt, Is.EqualTo(book.Trade.CreatedAt), "the moment is answered as it is kept");
            Assert.That(_bench.Bybit.Calls.Select(call => call.Call), Does.Not.Contain("order-create"));
        });
    }

    // Spec «Шорт по идее».
    [Test]
    public async Task A_short_is_written_as_a_sale()
    {
        Idea idea = await _bench.Idea("XRPUSDT", EntrySide.Short, 1.4051, null, null);

        Trade trade = await _bench.Sender.Send(new OpenTradeCommand(idea.Id, null, null));

        Assert.That((trade.Side, trade.Quantity, trade.Stop, trade.StopSource, trade.Target), Is.EqualTo((TradeSide.Short, 70m, 1.4549m, StopSource.Limit, (decimal?)null)));
    }

    // Spec «Цена ушла после расчёта».
    [Test]
    public async Task A_price_that_passed_the_stop_after_the_count_refuses_the_entry_and_writes_no_order()
    {
        Idea idea = await _bench.Idea();
        Assert.That((await _bench.Sender.Send(new PreviewEntryQuery(idea.Id, null, null))).Calculation.Passed, Is.True);
        _bench.Bybit.Price("SOLUSDT", 231.10m);

        TradeEntryRefusedException? refused = Assert.ThrowsAsync<TradeEntryRefusedException>(() => _bench.Sender.Send(new OpenTradeCommand(idea.Id, null, null)));

        Assert.That(refused!.Failed.Single().Text, Is.EqualTo("Цена 231.1 уже ниже стопа идеи 231.38"));
        Assert.That((await _bench.Services.GetRequiredService<ITradeRepository>().CountsAsync(CancellationToken.None)).Open, Is.Zero);
    }

    // Spec «Идея закрылась».
    [Test]
    public async Task An_idea_closed_while_the_dialog_was_open_is_refused()
    {
        Idea idea = await _bench.Idea();
        idea.State = IdeaState.Closed;
        idea.ExitTime = DateTimeOffset.UtcNow;
        idea.Result = -0.01;
        await _bench.Services.GetRequiredService<IIdeaRepository>().SaveAsync(idea, CancellationToken.None);

        TradeEntryRefusedException? refused = Assert.ThrowsAsync<TradeEntryRefusedException>(() => _bench.Sender.Send(new OpenTradeCommand(idea.Id, null, null)));

        Assert.That(refused!.Failed.Select(check => check.Text), Does.Contain("Идея закрыта"));
    }

    // Spec trade-risk «Свежий стенд».
    [Test]
    public async Task On_a_fresh_bench_the_entry_names_the_limits_to_set()
    {
        await _bench.DisposeAsync();
        _bench = await TradeBench.Start(limits: false);
        Idea idea = await _bench.Idea();

        TradeEntryRefusedException? refused = Assert.ThrowsAsync<TradeEntryRefusedException>(() => _bench.Sender.Send(new OpenTradeCommand(idea.Id, null, 100m)));

        Assert.That(refused!.Failed[0].Text, Does.StartWith("Задайте пределы в разделе «Риск»"));
    }

    // Spec trade-risk «Вход при запрете».
    [Test]
    public async Task The_ban_of_the_emergency_stop_refuses_the_entry()
    {
        Idea idea = await _bench.Idea();
        await _bench.Services.GetRequiredService<ITradeHaltRepository>().HaltAsync("trader-test", 52.10m, 50m, DateTimeOffset.UtcNow, CancellationToken.None);

        TradeEntryRefusedException? refused = Assert.ThrowsAsync<TradeEntryRefusedException>(() => _bench.Sender.Send(new OpenTradeCommand(idea.Id, null, null)));

        Assert.That(refused!.Failed, Is.EqualTo(new[] { new EntryCheck("halt", false, "Вход запрещён стоп-краном") }));
    }

    // Spec «Другой аккаунт» and «Вторая идея по той же паре».
    [Test]
    public async Task The_same_pair_on_another_account_enters_and_on_the_same_account_does_not()
    {
        ExchangeAccount ideas = await TradeBench.AddAccount(_bench.Services, "Суб-счёт идей", "key-ideas", 104850011);
        Idea first = await _bench.Idea();
        Idea second = await _bench.Idea();
        await _bench.Sender.Send(new OpenTradeCommand(first.Id, null, null));

        TradeEntryRefusedException? refused = Assert.ThrowsAsync<TradeEntryRefusedException>(() => _bench.Sender.Send(new OpenTradeCommand(second.Id, null, null)));
        Trade other = await _bench.Sender.Send(new OpenTradeCommand(second.Id, ideas.Id, null));

        Assert.That(refused!.Failed, Is.EqualTo(new[] { new EntryCheck("pairTrade", false, "По паре уже есть сделка") }));
        Assert.That(other.AccountName, Is.EqualTo("Суб-счёт идей"));
    }

    // Spec trade-risk «Открытая сделка не меняется».
    [Test]
    public async Task New_settings_leave_the_trade_already_there_as_it_was()
    {
        Idea idea = await _bench.Idea();
        Trade trade = await _bench.Sender.Send(new OpenTradeCommand(idea.Id, null, null));

        await _bench.Services.GetRequiredService<ITradeSettingsRepository>().SaveAsync(TradeBench.Limits with { Leverage = 5, StopAttempts = 5, MaxLossPercent = 2m }, CancellationToken.None);

        Trade kept = (await _bench.Services.GetRequiredService<ITradeRepository>().FindAsync(trade.Id, CancellationToken.None))!;
        Assert.That((kept.Leverage, kept.Stop, kept.StopAttempts), Is.EqualTo((3, 231.38m, 3)));
    }
}
