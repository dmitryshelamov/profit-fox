using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Trades;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Trades;

/// <summary>
///     Change take-idea-trades, task 4.3 (design.md, decisions 2 and 11): the lease of the trading Worker fences its writes,
///     and the emergency stop goes off in one transaction with the close orders and the cancel of the entries not sent.
/// </summary>
[NonParallelizable]
public sealed class TraderLeaseAndHaltTests
{
    private ITraderLeaseRepository _lease = null!;
    private ITradeRepository _trades = null!;
    private ITradeHaltRepository _halt = null!;

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
        _lease = BenchServices.Get<ITraderLeaseRepository>();
        _trades = BenchServices.Get<ITradeRepository>();
        _halt = BenchServices.Get<ITradeHaltRepository>();
    }

    [Test]
    public async Task The_lease_is_taken_by_one_Worker_and_renewed_by_it()
    {
        Assert.That(await _lease.TakeAsync("w-1", TimeSpan.FromSeconds(30), CancellationToken.None), Is.True);
        Assert.That(await _lease.TakeAsync("w-2", TimeSpan.FromSeconds(30), CancellationToken.None), Is.False);
        Assert.That(await _lease.TakeAsync("w-1", TimeSpan.FromSeconds(30), CancellationToken.None), Is.True);
        TraderState state = (await _lease.StateAsync(CancellationToken.None))!;
        Assert.That((state.Worker, state.Alive), Is.EqualTo(("w-1", true)));

        await _lease.ReleaseAsync("w-1", CancellationToken.None);

        Assert.That((await _lease.StateAsync(CancellationToken.None))!.Alive, Is.False);
        Assert.That(await _lease.TakeAsync("w-2", TimeSpan.FromSeconds(30), CancellationToken.None), Is.True);
    }

    [Test]
    public async Task A_write_under_a_lease_of_another_Worker_or_one_run_out_throws_and_writes_nothing()
    {
        ExchangeAccount account = await TradeStore.Account();
        Trade trade = (await TradeStore.Open(TradeGenerator.Long(account.Id)))!;
        Func<TradeBook, bool> cancel = book =>
        {
            book.Trade.Cancel("нет", DateTimeOffset.UtcNow);
            return true;
        };

        Assert.ThrowsAsync<LeaseLostException>(() => _trades.ChangeAsync(trade.Id, "w-1", cancel, null, CancellationToken.None), "nobody holds it");
        await _lease.TakeAsync("w-2", TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.ThrowsAsync<LeaseLostException>(() => _trades.ChangeAsync(trade.Id, "w-1", cancel, null, CancellationToken.None), "another holds it");
        await _lease.TakeAsync("w-1", TimeSpan.FromMilliseconds(1), CancellationToken.None);
        await _lease.ReleaseAsync("w-2", CancellationToken.None);
        await _lease.TakeAsync("w-1", TimeSpan.FromMilliseconds(1), CancellationToken.None);
        await Task.Delay(20);
        Assert.ThrowsAsync<LeaseLostException>(() => _trades.ChangeAsync(trade.Id, "w-1", cancel, null, CancellationToken.None), "it ran out");
        Assert.ThrowsAsync<LeaseLostException>(() => _halt.HaltAsync("w-1", 52m, 50m, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.ThrowsAsync<LeaseLostException>(() => _trades.MarkAsync("w-1", new Dictionary<Guid, decimal> { [trade.Id] = 1m }, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.That((await _trades.FindAsync(trade.Id, CancellationToken.None))!.State, Is.EqualTo(TradeState.Pending));
        Assert.That((await _halt.GetAsync(CancellationToken.None)).Halted, Is.False);

        await _lease.TakeAsync("w-1", TimeSpan.FromSeconds(30), CancellationToken.None);
        await _trades.ChangeAsync(trade.Id, "w-1", cancel, null, CancellationToken.None);
        Assert.That((await _trades.FindAsync(trade.Id, CancellationToken.None))!.State, Is.EqualTo(TradeState.Cancelled));
    }

    // Spec trade-risk «Порог пройден»: both open trades close by the market with «стоп-кран», the entry is banned, the moment,
    // 52.10 and 50 are written; an entry not sent is cancelled, one sent stays, a trade in error is not touched.
    [Test]
    public async Task The_emergency_stop_closes_the_open_trades_cancels_the_entries_not_sent_and_holds_once()
    {
        await _lease.TakeAsync("w-1", TimeSpan.FromSeconds(30), CancellationToken.None);
        ExchangeAccount account = await TradeStore.Account();
        Trade sol = await Opened(account, "SOLUSDT");
        Trade xrp = await Opened(account, "XRPUSDT");
        Trade waiting = (await TradeStore.Open(TradeGenerator.Long(account.Id, symbol: "ARBUSDT")))!;
        Trade sent = (await TradeStore.Open(TradeGenerator.Long(account.Id, symbol: "OPUSDT")))!;
        await _trades.ChangeAsync(sent.Id, null, book =>
        {
            book.Put(book.Orders[0] with { Status = TradeOrderStatus.Sent });
            return true;
        }, null, CancellationToken.None);
        Trade error = await Opened(account, "DOGEUSDT");
        await _trades.ChangeAsync(error.Id, null, book =>
        {
            book.Trade.Fail("позиция на бирже больше сделки");
            return true;
        }, null, CancellationToken.None);
        var at = new DateTimeOffset(2026, 11, 28, 15, 42, 0, TimeSpan.Zero);

        Assert.That(await _halt.HaltAsync("w-1", 52.10m, 50m, at, CancellationToken.None), Is.True);

        Assert.That(await _halt.GetAsync(CancellationToken.None), Is.EqualTo(new TradeHalt(at, 52.10m, 50m, null)));
        foreach (Trade trade in new[] { sol, xrp })
        {
            TradeBook book = (await _trades.HoldingAsync(CancellationToken.None)).Single(book => book.Trade.Id == trade.Id);
            Assert.That(book.Trade.State, Is.EqualTo(TradeState.Closing));
            Assert.That(book.Orders.Single(order => order.Purpose == TradeOrderPurpose.Close), Is.EqualTo(new TradeOrder(trade.Id, 2, TradeOrderPurpose.Close, TradeCloseReason.Halt, 0.4m, TradeOrderStatus.Pending, 0, at, null, null, at)));
        }

        Trade cancelled = (await _trades.FindAsync(waiting.Id, CancellationToken.None))!;
        Assert.That((cancelled.State, cancelled.Reason), Is.EqualTo((TradeState.Cancelled, "стоп-кран")));
        Assert.That((await _trades.FindAsync(sent.Id, CancellationToken.None))!.State, Is.EqualTo(TradeState.Pending));
        Assert.That((await _trades.FindAsync(error.Id, CancellationToken.None))!.State, Is.EqualTo(TradeState.Error));
        Assert.That(await _halt.HaltAsync("w-1", 60m, 50m, at.AddMinutes(1), CancellationToken.None), Is.False, "it goes off once");
        Assert.That((await _halt.GetAsync(CancellationToken.None)).Loss, Is.EqualTo(52.10m));
    }

    // Spec trade-risk «Ещё закрываются», «Запрет снят».
    [Test]
    public async Task The_ban_is_lifted_only_when_no_trade_closes()
    {
        await _lease.TakeAsync("w-1", TimeSpan.FromSeconds(30), CancellationToken.None);
        ExchangeAccount account = await TradeStore.Account();
        Trade trade = await Opened(account, "SOLUSDT");
        await _halt.HaltAsync("w-1", 52.10m, 50m, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.That(await _halt.LiftAsync(DateTimeOffset.UtcNow, CancellationToken.None), Is.False);
        Assert.That((await _halt.GetAsync(CancellationToken.None)).Halted, Is.True);

        await using (NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("UPDATE trades SET state = 'closed' WHERE id = @Id", new { trade.Id });
        }

        var lifted = new DateTimeOffset(2026, 11, 28, 16, 0, 0, TimeSpan.Zero);
        Assert.That(await _halt.LiftAsync(lifted, CancellationToken.None), Is.True);
        Assert.That(await _halt.GetAsync(CancellationToken.None), Is.EqualTo(new TradeHalt(null, null, null, lifted)));
    }

    // 12.2 (finding 2): the stop cancels at once only the entries never tried; one tried is left to the Worker.
    [Test]
    public async Task The_emergency_stop_leaves_an_entry_tried_before_to_the_Worker()
    {
        await _lease.TakeAsync("w-1", TimeSpan.FromSeconds(30), CancellationToken.None);
        ExchangeAccount account = await TradeStore.Account();
        Trade tried = (await TradeStore.Open(TradeGenerator.Long(account.Id, symbol: "ARBUSDT")))!;
        await _trades.ChangeAsync(tried.Id, null, book =>
        {
            book.Put(book.Orders[0] with { Attempts = 1 });
            return true;
        }, null, CancellationToken.None);

        await _halt.HaltAsync("w-1", 52.10m, 50m, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.That((await _trades.FindAsync(tried.Id, CancellationToken.None))!.State, Is.EqualTo(TradeState.Pending));
    }

    // 12.7 (finding 7): the stop takes the advisory lock of the entry, so it waits for an entry being written.
    [Test]
    public async Task The_emergency_stop_waits_for_the_lock_of_the_entry()
    {
        await _lease.TakeAsync("w-1", TimeSpan.FromSeconds(30), CancellationToken.None);
        await using NpgsqlConnection entry = await BenchDatabase.DataSource.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await entry.BeginTransactionAsync();
        await entry.ExecuteAsync("SELECT pg_advisory_xact_lock(@Key)", new { Key = ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Trades.TraderFence.EntryLock }, transaction);

        Task<bool> halt = _halt.HaltAsync("w-1", 52.10m, 50m, DateTimeOffset.UtcNow, CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.That(halt.IsCompleted, Is.False, "the stop waits for the entry");
        // 12.19 (N5): while it waits, it holds no lock of the lease: the lease is renewed.
        Task<bool> renewal = _lease.TakeAsync("w-1", TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.That(await Task.WhenAny(renewal, Task.Delay(TimeSpan.FromSeconds(2))), Is.SameAs(renewal), "the renewal of the lease does not wait for the stop");
        Assert.That(await renewal, Is.True);

        await transaction.CommitAsync();
        Assert.That(await halt, Is.True);
    }

    // 12.10 (finding 10): how an account is read and its cursor are written under the lease of the trading Worker.
    [Test]
    public async Task The_feed_and_the_cursor_of_an_account_are_written_under_the_lease()
    {
        ExchangeAccount account = await TradeStore.Account();
        IExchangeAccountRepository accounts = BenchServices.Get<IExchangeAccountRepository>();

        Assert.ThrowsAsync<LeaseLostException>(() => accounts.SetFeedAsync(account.Id, "poll", DateTimeOffset.UtcNow, "w-1", CancellationToken.None));
        Assert.ThrowsAsync<LeaseLostException>(() => accounts.SetFillsCursorAsync(account.Id, DateTimeOffset.UtcNow, "w-1", CancellationToken.None));
        await _lease.TakeAsync("w-1", TimeSpan.FromSeconds(30), CancellationToken.None);
        await accounts.SetFeedAsync(account.Id, "poll", DateTimeOffset.UtcNow, "w-1", CancellationToken.None);

        Assert.That((await accounts.FindAsync(account.Id, CancellationToken.None))!.Feed, Is.EqualTo("poll"));
    }

    // 12.22 (R3): the removal of an account is one statement under the lock of the entry: a trade that holds a pair keeps it,
    // and the removal waits for an entry being written.
    [Test]
    public async Task An_account_is_removed_only_without_a_holding_trade_and_under_the_lock_of_the_entry()
    {
        IExchangeAccountRepository accounts = BenchServices.Get<IExchangeAccountRepository>();
        ExchangeAccount trading = await TradeStore.Account();
        ExchangeAccount idle = await TradeStore.Account("Суб", 104850011, "key-idle");
        await TradeStore.Open(TradeGenerator.Long(trading.Id));

        Assert.That(await accounts.DeleteIdleAsync(trading.Id, CancellationToken.None), Is.Null);
        Assert.That(await accounts.FindAsync(trading.Id, CancellationToken.None), Is.Not.Null);
        Assert.That(await accounts.DeleteIdleAsync(Guid.NewGuid(), CancellationToken.None), Is.False);

        await using NpgsqlConnection entry = await BenchDatabase.DataSource.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await entry.BeginTransactionAsync();
        await entry.ExecuteAsync("SELECT pg_advisory_xact_lock(@Key)", new { Key = ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Trades.TraderFence.EntryLock }, transaction);
        Task<bool?> removal = accounts.DeleteIdleAsync(idle.Id, CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.That(removal.IsCompleted, Is.False, "the removal waits for the entry");
        await transaction.CommitAsync();
        Assert.That(await removal, Is.True);
    }

    private async Task<Trade> Opened(ExchangeAccount account, string symbol)
    {
        Trade trade = (await TradeStore.Open(TradeGenerator.Long(account.Id, symbol: symbol)))!;
        await _trades.ChangeAsync(trade.Id, null, book =>
        {
            book.Fills.Add(new TradeFill($"e-{symbol}", trade.Id, TradeSide.Long, 100m, 0.4m, 0m, FillKind.Entry, null, DateTimeOffset.UtcNow));
            book.Trade.Apply(book.Fills);
            book.Put(book.Orders[0] with { Status = TradeOrderStatus.Filled });
            return true;
        }, null, CancellationToken.None);
        return trade;
    }
}
