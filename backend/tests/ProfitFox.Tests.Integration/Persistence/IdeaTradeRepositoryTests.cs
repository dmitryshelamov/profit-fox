using System.Text;

using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades;
using ProfitFox.Infrastructure.Persistence.Postgres.Transactions;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class IdeaTradeRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-05T12:00:00Z");
    private static readonly Guid OwnerA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid OwnerB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    private NpgsqlDataSource _dataSource = null!;
    private PostgresTransactionExecutor _executor = null!;
    private IdeaTradeRepository _repository = null!;
    private Guid _account;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = MarketDataDatabase.Open();
        var accessor = new PostgresTransactionAccessor();
        _executor = new PostgresTransactionExecutor(_dataSource, accessor);
        _repository = new IdeaTradeRepository(_dataSource, accessor);
        var account = new ExchangeAccount(Guid.NewGuid(), "Main", "key", 100, new HashSet<MarketCategory> { MarketCategory.Spot, MarketCategory.Linear }, Now);
        await new ExchangeAccountRepository(_dataSource).AddAsync(account, new EncryptedSecret([1], Encoding.UTF8.GetBytes("cipher"), [2]), CancellationToken.None);
        _account = account.Id;
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    // Scenario "Сделка в списке": the whole trade survives a round trip, the newest first.
    [Test]
    public async Task Trade_is_saved_with_its_entry_and_read_back()
    {
        IdeaTrade older = await CreateAsync(Now);
        IdeaTrade newer = await CreateAsync(Now.AddMinutes(1), type: IdeaType.Pullback);

        (await _repository.GetAsync(older.Id, CancellationToken.None)).ShouldBeEquivalentTo(older);
        (await _repository.GetAsync(newer.Id, CancellationToken.None)).ShouldBeEquivalentTo(newer);
        (await _repository.GetAllAsync(CancellationToken.None)).Select(trade => trade.Id).ShouldBe([newer.Id, older.Id]);
        (await _repository.GetOrdersAsync(newer.Id, CancellationToken.None)).ShouldHaveSingleItem().Role.ShouldBe(IdeaTradeOrderRole.Entry);
        (await _repository.GetAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
    }

    // Task 2.6: the trade of an auto keeps the auto it was made for through the saves of a Worker; a manual one has none.
    [Test]
    public async Task Trade_of_an_auto_keeps_its_auto_through_a_save_of_a_Worker()
    {
        var auto = Guid.NewGuid();
        IdeaTrade trade = await CreateAsync(Now, autoId: auto);
        IdeaTrade manual = await CreateAsync(Now.AddMinutes(1));

        await SaveAsync(trade.EntryGrown(new LegTotals(0.47m, 49.82m, 0.02m), Now.AddMinutes(1)));

        (await _repository.GetAsync(trade.Id, CancellationToken.None))!.AutoId.ShouldBe(auto);
        (await _repository.GetAsync(manual.Id, CancellationToken.None))!.AutoId.ShouldBeNull();
        (await _repository.GetOpenAsync(CancellationToken.None)).Select(open => open.AutoId).ShouldBe([auto, null]);
    }

    [Test]
    public async Task Only_trades_that_wait_or_are_open_are_open()
    {
        IdeaTrade waiting = await CreateAsync(Now);
        IdeaTrade done = await CreateAsync(Now.AddMinutes(1));
        await SaveAsync(done.EntryEnded(Now.AddMinutes(2)));

        (await _repository.GetOpenAsync(CancellationToken.None)).Select(trade => trade.Id).ShouldBe([waiting.Id]);
    }

    [Test]
    public async Task Saved_trade_keeps_what_the_exchange_counted()
    {
        IdeaTrade trade = await CreateAsync(Now);
        IdeaTrade changed = (trade.EntryGrown(new LegTotals(0.47m, 49.82m, 0.02m), Now.AddMinutes(1)) with { LastOrderNumber = 2 })
            .ExitGrown(IdeaTradeLeg.TakeProfit, new LegTotals(0.47m, 54m, 0.03m), Now.AddMinutes(2));

        await SaveAsync(changed);

        (await _repository.GetAsync(trade.Id, CancellationToken.None)).ShouldBeEquivalentTo(changed);
    }

    // Decisions 1 and 2: the Worker's save writes neither the promise nor the outcome of the idea; the scanner writes the outcome, and the save keeps it.
    [Test]
    public async Task Save_of_a_Worker_keeps_the_promise_and_the_outcome_of_the_idea_and_writes_the_time_it_opened()
    {
        var promise = new IdeaPromise(IdeaBadge.Up, 0.31m, 0.14m, 412, 0.33m, 1.84m);
        IdeaTrade trade = await CreateAsync(Now, promise: promise);
        (await _repository.GetAsync(trade.Id, CancellationToken.None))!.Promise.ShouldBe(promise);
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                "UPDATE idea_trades SET idea_status = 'target', idea_status_at = @At, idea_result = 1.84 WHERE id = @Id",
                new { At = Now.AddHours(2), trade.Id });
        }

        await SaveAsync((trade with { Promise = null }).EntryGrown(new LegTotals(0.47m, 49.82m, 0.02m), Now.AddMinutes(1)));

        IdeaTrade read = (await _repository.GetAsync(trade.Id, CancellationToken.None))!;
        (read.Promise, read.IdeaOutcome, read.OpenedAt).ShouldBe((promise, new TradedIdeaOutcome(IdeaStatus.Target, Now.AddHours(2), 1.84m), Now.AddMinutes(1)));
    }

    // A promise with no bucket has the break-even and the reward to risk only; an idea that expired without an entry has an outcome with no result.
    [Test]
    public async Task Promise_without_a_bucket_and_outcome_without_a_result_are_read_back()
    {
        var promise = new IdeaPromise(null, null, null, null, null, 1.2m);
        IdeaTrade trade = await CreateAsync(Now, promise: promise);
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("UPDATE idea_trades SET idea_status = 'expired', idea_status_at = @At WHERE id = @Id", new { At = Now.AddHours(1), trade.Id });
        }

        IdeaTrade read = (await _repository.GetAsync(trade.Id, CancellationToken.None))!;

        (read.Promise, read.IdeaOutcome).ShouldBe((promise, new TradedIdeaOutcome(IdeaStatus.Expired, Now.AddHours(1), null)));
    }

    // Scenario "Два торгующих Worker'а": a trade with a live lease is nobody else's.
    [Test]
    public async Task Leased_trade_is_not_claimed_twice()
    {
        IdeaTrade trade = await CreateAsync(Now);

        LeasedIdeaTrade first = (await _repository.ClaimAsync(OwnerA, Now, Now.AddSeconds(30), CancellationToken.None))!;

        first.Trade.Id.ShouldBe(trade.Id);
        (await _repository.ClaimAsync(OwnerB, Now.AddSeconds(10), Now.AddSeconds(40), CancellationToken.None)).ShouldBeNull();
        (await _repository.RenewAllAsync(OwnerA, [(trade.Id, first.Epoch)], Now.AddSeconds(50), CancellationToken.None)).ShouldHaveSingleItem().Trade.Id.ShouldBe(trade.Id);

        LeasedIdeaTrade second = (await _repository.ClaimAsync(OwnerB, Now.AddSeconds(60), Now.AddSeconds(90), CancellationToken.None))!;

        second.Epoch.ShouldBeGreaterThan(first.Epoch);
        (await _repository.RenewAllAsync(OwnerA, [(trade.Id, first.Epoch)], Now.AddSeconds(100), CancellationToken.None)).ShouldBeEmpty();
    }

    [Test]
    public async Task Trade_that_is_over_is_not_claimed()
    {
        IdeaTrade trade = await CreateAsync(Now);
        await SaveAsync(trade.EntryEnded(Now));

        (await _repository.ClaimAsync(OwnerA, Now, Now.AddSeconds(30), CancellationToken.None)).ShouldBeNull();
    }

    // A failed trade whose entry still stands on the exchange is watched until the entry is gone.
    [Test]
    public async Task Failed_trade_is_claimed_while_an_order_of_it_stands_and_not_after()
    {
        IdeaTrade trade = await CreateAsync(Now);
        await SaveAsync(trade.Fail("Bybit refused to cancel", Now));

        (await _repository.ClaimAsync(OwnerA, Now, Now.AddSeconds(30), CancellationToken.None))!.Trade.Id.ShouldBe(trade.Id);

        IdeaTradeOrder entry = (await _repository.GetOrdersAsync(trade.Id, CancellationToken.None)).Single();
        await _executor.ExecuteAsync(
            async token =>
            {
                await _repository.SaveOrderAsync(entry.Cancelled(), token);

                return 0;
            },
            CancellationToken.None);

        (await _repository.ClaimAsync(OwnerB, Now.AddSeconds(60), Now.AddSeconds(90), CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Released_trade_is_claimed_again_and_changes_are_told_to_the_holder()
    {
        IdeaTrade trade = await CreateAsync(Now);
        LeasedIdeaTrade held = (await _repository.ClaimAsync(OwnerA, Now, Now.AddSeconds(30), CancellationToken.None))!;
        await SaveAsync(trade.RequestCancel(), Now.AddSeconds(5));

        (await _repository.GetChangedAsync(OwnerA, Now, CancellationToken.None)).ShouldHaveSingleItem().Trade.CancelRequested.ShouldBeTrue();
        (await _repository.GetChangedAsync(OwnerB, Now, CancellationToken.None)).ShouldBeEmpty();

        await _repository.ReleaseAllAsync(OwnerB, [(trade.Id, held.Epoch)], CancellationToken.None);
        (await _repository.ClaimAsync(OwnerB, Now, Now.AddSeconds(30), CancellationToken.None)).ShouldBeNull();

        await _repository.ReleaseAsync(trade.Id, OwnerA, held.Epoch, CancellationToken.None);
        (await _repository.ClaimAsync(OwnerB, Now, Now.AddSeconds(30), CancellationToken.None))!.Trade.Id.ShouldBe(trade.Id);
    }

    [Test]
    public async Task Row_is_locked_for_the_owner_and_epoch_of_the_take_only()
    {
        IdeaTrade trade = await CreateAsync(Now);
        LeasedIdeaTrade held = (await _repository.ClaimAsync(OwnerA, Now, Now.AddSeconds(30), CancellationToken.None))!;

        (await Locked(trade.Id, OwnerA, held.Epoch)).ShouldNotBeNull();
        (await Locked(trade.Id, OwnerB, held.Epoch)).ShouldBeNull();
        (await Locked(trade.Id, OwnerA, held.Epoch + 1)).ShouldBeNull();
        (await _executor.ExecuteAsync(token => _repository.LockAsync(trade.Id, token), CancellationToken.None))!.Id.ShouldBe(trade.Id);
        (await _executor.ExecuteAsync(token => _repository.LockAsync(Guid.NewGuid(), token), CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Orders_children_and_fills_are_kept_with_the_trade()
    {
        IdeaTrade trade = await CreateAsync(Now);
        IdeaTradeOrder entry = (await _repository.GetOrdersAsync(trade.Id, CancellationToken.None)).Single().Taken("bybit-1").Grown(0.47m, 49.82m, 0.02m, filled: true);
        var close = IdeaTradeOrder.NewClose(trade.Id, 2, OrderSide.Sell, 0.47m, Now);
        var child = new IdeaTradeChild(trade.Id, "bybit-2", IdeaTradeLeg.StopLoss, 0m, 0m, 0m, false);
        var fill = new IdeaTradeFill("exec-1", trade.Id, "bybit-1", 0.47m, 106m, 0.02m, FeeAsset.Quote, Now);

        await _executor.ExecuteAsync(
            async token =>
            {
                await _repository.SaveOrderAsync(entry, token);
                await _repository.SaveOrderAsync(close, token);
                await _repository.SaveChildAsync(child, token);
                await _repository.SaveChildAsync(child with { FilledQuantity = 0.47m, FilledValue = 47m, Fee = 0.03m, Finished = true }, token);
                (await _repository.AddFillAsync(fill, token)).ShouldBeTrue();
                (await _repository.AddFillAsync(fill, token)).ShouldBeFalse();

                return 0;
            },
            CancellationToken.None);

        (await _repository.GetOrdersAsync(trade.Id, CancellationToken.None)).ShouldBe([entry, close]);
        (await _executor.ExecuteAsync(token => _repository.GetUnfinishedOrdersAsync(trade.Id, token), CancellationToken.None)).ShouldBe([close]);
        (await _repository.GetChildrenAsync(trade.Id, CancellationToken.None)).ShouldHaveSingleItem().ShouldBeEquivalentTo(child with { FilledQuantity = 0.47m, FilledValue = 47m, Fee = 0.03m, Finished = true });
        (await _repository.GetFillsAsync(trade.Id, CancellationToken.None)).ShouldHaveSingleItem().ShouldBeEquivalentTo(fill);
        (await _repository.IsChildAsync("bybit-2", CancellationToken.None)).ShouldBeTrue();
        (await _repository.IsChildAsync("bybit-1", CancellationToken.None)).ShouldBeFalse();
    }

    private async Task<IdeaTrade> CreateAsync(DateTimeOffset at, IdeaType type = IdeaType.TwoR, IdeaPromise? promise = null, Guid? autoId = null)
    {
        var trade = IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(at, type: type), _account, 106m, new IdeaTradeSize(0.47m, 49.82m, 0.2m, 0.368m, 1.84m), at, promise, autoId);
        var entry = IdeaTradeOrder.NewEntry(trade.Id, 1, OrderSide.Buy, OrderType.Market, TimeInForce.GoodTilCancelled, null, 0.47m, new Protection(trade.Target, trade.Stop), at);
        await _executor.ExecuteAsync(
            async token =>
            {
                await _repository.CreateAsync(trade, entry, at, token);

                return 0;
            },
            CancellationToken.None);

        return trade;
    }

    private Task SaveAsync(IdeaTrade trade, DateTimeOffset? at = null)
    {
        return _executor.ExecuteAsync(
            async token =>
            {
                await _repository.SaveAsync(trade, at ?? Now, token);

                return 0;
            },
            CancellationToken.None);
    }

    private Task<IdeaTrade?> Locked(Guid trade, Guid owner, long epoch)
    {
        return _executor.ExecuteAsync(token => _repository.LockLeasedAsync(trade, owner, epoch, token), CancellationToken.None);
    }
}