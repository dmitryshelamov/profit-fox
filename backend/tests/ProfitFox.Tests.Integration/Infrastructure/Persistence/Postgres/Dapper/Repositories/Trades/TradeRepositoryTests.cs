using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Trades;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Trades;

/// <summary>
///     Change take-idea-trades, task 4.2 (design.md, decisions 1, 7 and 12): the entry under the advisory lock with the limits
///     counted anew, a fill kept once by its id, the lists with pages and sorting, and the trades of a removed account.
/// </summary>
[NonParallelizable]
public sealed class TradeRepositoryTests
{
    private ITradeRepository _trades = null!;

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
        _trades = BenchServices.Get<ITradeRepository>();
    }

    [Test]
    public async Task Of_two_entries_at_once_with_a_limit_of_one_trade_one_passes()
    {
        ExchangeAccount account = await TradeStore.Account();
        Trade sol = TradeGenerator.Long(account.Id, symbol: "SOLUSDT", created: Now());
        Trade xrp = TradeGenerator.Long(account.Id, symbol: "XRPUSDT", created: Now());

        EntryRefusal?[] answers = await Task.WhenAll(
            _trades.OpenAsync(sol, TradeGenerator.Order(sol, 1, TradeOrderPurpose.Entry), 1, 1000m, CancellationToken.None),
            _trades.OpenAsync(xrp, TradeGenerator.Order(xrp, 1, TradeOrderPurpose.Entry), 1, 1000m, CancellationToken.None));

        Assert.That(answers.Count(answer => answer is null), Is.EqualTo(1));
        Assert.That(answers.Single(answer => answer is not null)!.Check, Is.EqualTo(new EntryCheck("open", false, "Открыто 1 сделок из 1")));
        Assert.That((await _trades.CountsAsync(CancellationToken.None)).Open, Is.EqualTo(1));
    }

    [Test]
    public async Task An_entry_beyond_the_amount_in_all_a_taken_pair_and_the_ban_are_refused()
    {
        ExchangeAccount account = await TradeStore.Account();
        Assert.That(await TradeStore.Open(TradeGenerator.Long(account.Id, created: Now())), Is.Not.Null);

        Trade other = TradeGenerator.Long(account.Id, symbol: "XRPUSDT", created: Now());
        Assert.That((await _trades.OpenAsync(other, TradeGenerator.Order(other, 1, TradeOrderPurpose.Entry), 5, 150m, CancellationToken.None))!.Check.Code, Is.EqualTo("total"));
        Trade same = TradeGenerator.Long(account.Id, created: Now());
        Assert.That((await _trades.OpenAsync(same, TradeGenerator.Order(same, 1, TradeOrderPurpose.Entry), 5, 1000m, CancellationToken.None))!.Check.Code, Is.EqualTo("pairTrade"));

        await using (NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("INSERT INTO trade_halt (id, halted_at, loss, limit_value) VALUES (1, now(), 52.10, 50)");
        }

        Assert.That((await _trades.OpenAsync(other, TradeGenerator.Order(other, 1, TradeOrderPurpose.Entry), 5, 1000m, CancellationToken.None))!.Check.Code, Is.EqualTo("halt"));
    }

    [Test]
    public async Task A_written_trade_reads_back_whole_with_its_entry_order()
    {
        ExchangeAccount account = await TradeStore.Account();
        Trade trade = (await TradeStore.Open(TradeGenerator.Long(account.Id, created: Now())))!;

        IReadOnlyList<TradeBook> holding = await _trades.HoldingAsync(CancellationToken.None);

        Trade read = holding.Single().Trade;
        Assert.Multiple(() =>
        {
            Assert.That(read.Id, Is.EqualTo(trade.Id));
            Assert.That((read.Symbol, read.Side, read.Amount, read.Leverage, read.Quantity, read.Stop, read.Target), Is.EqualTo((trade.Symbol, trade.Side, trade.Amount, trade.Leverage, trade.Quantity, trade.Stop, trade.Target)));
            Assert.That((read.Until, read.CreatedAt, read.EntryWait, read.StopAttempts, read.Timeframe), Is.EqualTo((trade.Until, trade.CreatedAt, trade.EntryWait, trade.StopAttempts, trade.Timeframe)));
            Assert.That(read.State, Is.EqualTo(TradeState.Pending));
            Assert.That(holding.Single().Orders.Single().LinkId, Is.EqualTo(TradeOrderId.Format(trade.Id, 1)));
        });
    }

    // A fill told twice — by the stream and by a read of REST — is kept once, and the outcome does not change.
    [Test]
    public async Task A_fill_told_again_is_kept_once_and_changes_nothing()
    {
        ExchangeAccount account = await TradeStore.Account();
        Trade trade = (await TradeStore.Open(TradeGenerator.Long(account.Id, created: Now())))!;
        TradeFill entry = Fill(trade, "e-1", TradeSide.Long, 237.40m, FillKind.Entry, null);
        TradeFill exit = Fill(trade, "x-1", TradeSide.Short, 245.54m, FillKind.Exit, TradeCloseReason.Target);

        await Apply(trade.Id, entry, exit);
        decimal? result = (await _trades.FindAsync(trade.Id, CancellationToken.None))!.Result;
        TradeBook again = (await Apply(trade.Id, entry, exit))!;

        Assert.That(again.Known, Is.EquivalentTo(new[] { "e-1", "x-1" }));
        Assert.That((await _trades.FillsAsync(trade.Id, CancellationToken.None)).Count, Is.EqualTo(2));
        Trade closed = (await _trades.FindAsync(trade.Id, CancellationToken.None))!;
        Assert.That((closed.State, closed.CloseReason, closed.Result), Is.EqualTo((TradeState.Closed, (TradeCloseReason?)TradeCloseReason.Target, result)));
        Assert.That(result, Is.EqualTo(3.256m));
    }

    // Spec idea-trades «Только закрытые сделки».
    [Test]
    public async Task The_closed_trades_of_a_removed_account_keep_its_name()
    {
        ExchangeAccount old = await TradeStore.Account("Старый");
        Trade trade = (await TradeStore.Open(TradeGenerator.Long(old.Id, accountName: "Старый", created: Now())))!;
        await _trades.ChangeAsync(trade.Id, null, book =>
        {
            book.Trade.Cancel("вход не ушёл вовремя", Now());
            return true;
        }, null, CancellationToken.None);
        Assert.That(await _trades.HoldsAnyAsync(old.Id, CancellationToken.None), Is.False);

        await BenchServices.Get<IExchangeAccountRepository>().DeleteAsync(old.Id, CancellationToken.None);

        ListPage<TradeListItem> closed = await _trades.ListAsync(new TradeListQuery(false, new ListOrder("exitTime", true, 1, 50)), CancellationToken.None);
        Assert.That(closed.Items.Select(item => (item.Trade.AccountId, item.Trade.AccountName)), Is.EqualTo(new[] { ((Guid?)null, "Старый") }));
    }

    [Test]
    public async Task A_holding_trade_of_the_account_is_told()
    {
        ExchangeAccount account = await TradeStore.Account();
        await TradeStore.Open(TradeGenerator.Long(account.Id, created: Now()));
        Assert.That(await _trades.HoldsAnyAsync(account.Id, CancellationToken.None), Is.True);
    }

    [Test]
    public async Task The_lists_go_by_pages_sorted_with_the_outcome_of_the_idea()
    {
        ExchangeAccount account = await TradeStore.Account();
        foreach (string symbol in new[] { "AUSDT", "BUSDT", "CUSDT" })
        {
            await TradeStore.Open(TradeGenerator.Long(account.Id, symbol: symbol, created: Now()));
        }

        ListPage<TradeListItem> first = await _trades.ListAsync(new TradeListQuery(true, new ListOrder("symbol", false, 1, 2)), CancellationToken.None);
        ListPage<TradeListItem> second = await _trades.ListAsync(new TradeListQuery(true, new ListOrder("symbol", false, 2, 2)), CancellationToken.None);

        Assert.That((first.Total, first.Items.Select(item => item.Trade.Symbol)), Is.EqualTo((3, new[] { "AUSDT", "BUSDT" })));
        Assert.That(second.Items.Select(item => item.Trade.Symbol), Is.EqualTo(new[] { "CUSDT" }));
        Assert.That((await _trades.ListAsync(new TradeListQuery(false, new ListOrder("exitTime", true, 1, 50)), CancellationToken.None)).Total, Is.Zero);
        foreach (string field in ProfitFox.Domain.Services.Lists.ListFields.Trades.Select(field => field.Name))
        {
            Assert.DoesNotThrowAsync(() => _trades.ListAsync(new TradeListQuery(false, new ListOrder(field, true, 1, 50)), CancellationToken.None), field);
        }
    }

    [Test]
    public async Task The_trade_of_an_idea_is_its_latest_not_cancelled()
    {
        ExchangeAccount account = await TradeStore.Account();
        var idea = Guid.NewGuid();
        Trade cancelled = (await TradeStore.Open(TradeGenerator.Long(account.Id, idea: idea, created: Now())))!;
        await _trades.ChangeAsync(cancelled.Id, null, book =>
        {
            book.Trade.Cancel("нет", Now());
            return true;
        }, null, CancellationToken.None);
        Assert.That(await _trades.ByIdeasAsync([idea], CancellationToken.None), Is.Empty);

        Trade open = (await TradeStore.Open(TradeGenerator.Long(account.Id, idea: idea, created: Now())))!;

        Assert.That((await _trades.ByIdeasAsync([idea], CancellationToken.None))[idea], Is.EqualTo((open.Id, TradeState.Pending)));
    }

    [Test]
    public async Task The_day_takes_the_outcomes_closed_from_its_start_and_the_results_of_the_holding_trades()
    {
        ExchangeAccount account = await TradeStore.Account();
        Trade trade = (await TradeStore.Open(TradeGenerator.Long(account.Id, created: Now())))!;
        await Apply(trade.Id, Fill(trade, "e-1", TradeSide.Long, 100m, FillKind.Entry, null));
        await using (NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("UPDATE trades SET unrealised = -12 WHERE id = @Id", new { trade.Id });
        }

        (IReadOnlyList<(DateTimeOffset ClosedAt, decimal Result)> closed, IReadOnlyList<decimal> open) = await _trades.DayAsync(Now().AddHours(-1), CancellationToken.None);

        Assert.That((closed.Count, open), Is.EqualTo((0, new[] { -12m })));
    }

    private Task<TradeBook?> Apply(Guid id, params TradeFill[] fills)
    {
        return _trades.ChangeAsync(id, null, book =>
        {
            foreach (TradeFill fill in fills.Where(fill => !book.Known.Contains(fill.ExecId)))
            {
                book.Fills.Add(fill);
            }

            book.Trade.Apply(book.Fills);
            return true;
        }, [.. fills.Select(fill => fill.ExecId)], CancellationToken.None);
    }

    private static TradeFill Fill(Trade trade, string id, TradeSide side, decimal price, FillKind kind, TradeCloseReason? reason)
    {
        return new TradeFill(id, trade.Id, side, price, 0.4m, 0m, kind, reason, Now().AddSeconds(kind == FillKind.Entry ? 1 : 2));
    }

    private static DateTimeOffset Now()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
