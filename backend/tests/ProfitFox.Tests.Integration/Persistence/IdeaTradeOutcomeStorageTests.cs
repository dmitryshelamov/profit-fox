using System.Text;

using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades;
using ProfitFox.Infrastructure.Persistence.Postgres.Transactions;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Decision 2: the scanner writes how the idea of a trade ended on the trade, in the transaction of the minute, and the trade keeps it.</summary>
public class IdeaTradeOutcomeStorageTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-05T12:00:00Z");

    private NpgsqlDataSource _dataSource = null!;
    private IdeaRepository _ideas = null!;
    private IdeaTradeRepository _trades = null!;
    private PostgresTransactionExecutor _executor = null!;
    private Guid _account;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
        var accessor = new PostgresTransactionAccessor();
        _executor = new PostgresTransactionExecutor(_dataSource, accessor);
        _trades = new IdeaTradeRepository(_dataSource, accessor);
        _ideas = new IdeaRepository(_dataSource);
        var account = new ExchangeAccount(Guid.NewGuid(), "Main", "key", 100, new HashSet<MarketCategory> { MarketCategory.Linear }, Now);
        await new ExchangeAccountRepository(_dataSource).AddAsync(account, new EncryptedSecret([1], Encoding.UTF8.GetBytes("cipher"), [2]), CancellationToken.None);
        _account = account.Id;
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    // Scenario "Идея дошла до цели": the idea closes in the transaction of the minute and the trade gets its outcome without a new updated_at, so no Worker rereads it.
    [Test]
    public async Task Closing_idea_writes_its_outcome_on_its_trade_without_touching_the_updated_time()
    {
        (Idea idea, IdeaTrade trade) = await TradeOfIdeaAsync();
        DateTimeOffset before = await UpdatedAtAsync(trade.Id);
        Idea target = Closed(idea, IdeaStatus.Target, 115m);
        var outcome = new TradedIdeaOutcome(IdeaStatus.Target, Now.AddHours(2), 1.84m);

        await _ideas.SaveAsync([], [target], [new ClosedIdea(idea.Id, outcome)], CancellationToken.None);

        (await _trades.GetAsync(trade.Id, CancellationToken.None))!.IdeaOutcome.ShouldBe(outcome);
        (await UpdatedAtAsync(trade.Id)).ShouldBe(before);
    }

    [Test]
    public async Task Outcome_of_a_trade_is_written_once_and_other_trades_are_left_alone()
    {
        (Idea idea, IdeaTrade trade) = await TradeOfIdeaAsync();
        (_, IdeaTrade other) = await TradeOfIdeaAsync(IdeaSetup.Drop);
        var first = new TradedIdeaOutcome(IdeaStatus.Target, Now.AddHours(2), 1.84m);

        await _ideas.SaveAsync([], [], [new ClosedIdea(idea.Id, first)], CancellationToken.None);
        await _ideas.SaveAsync([], [], [new ClosedIdea(idea.Id, new TradedIdeaOutcome(IdeaStatus.Stop, Now.AddHours(3), -1.06m))], CancellationToken.None);

        (await _trades.GetAsync(trade.Id, CancellationToken.None))!.IdeaOutcome.ShouldBe(first);
        (await _trades.GetAsync(other.Id, CancellationToken.None))!.IdeaOutcome.ShouldBeNull();
    }

    // Scenario "Идею удалили".
    [Test]
    public async Task Outcome_stays_on_the_trade_when_the_clean_up_deletes_the_idea()
    {
        (Idea idea, IdeaTrade trade) = await TradeOfIdeaAsync();
        var outcome = new TradedIdeaOutcome(IdeaStatus.Expired, Now.AddHours(2), null);
        await _ideas.SaveAsync([], [Closed(idea, IdeaStatus.Expired, 106m)], [new ClosedIdea(idea.Id, outcome)], CancellationToken.None);

        (await _ideas.DeleteClosedBeforeAsync(Now.AddDays(1), CancellationToken.None)).ShouldBe(1);

        (await _trades.GetAsync(trade.Id, CancellationToken.None))!.IdeaOutcome.ShouldBe(outcome);
    }

    // Scenario "Сделка до выкатки": the closed ideas that still hold a trade without an outcome, and only those.
    [Test]
    public async Task Catch_up_finds_the_closed_ideas_of_trades_without_an_outcome()
    {
        (Idea waiting, _) = await TradeOfIdeaAsync();
        (Idea closed, _) = await TradeOfIdeaAsync(IdeaSetup.Drop);
        (Idea done, _) = await TradeOfIdeaAsync(IdeaSetup.EarlyDrop);
        Idea closedAlone = IdeaGenerator.Generate(Now, IdeaStatus.Stop, setup: IdeaSetup.Squeeze, statusAt: Now.AddHours(1));
        await _ideas.SaveAsync([closedAlone], [], CancellationToken.None);
        Idea target = Closed(closed, IdeaStatus.Target, 115m);
        await _ideas.SaveAsync([], [target, Closed(done, IdeaStatus.Stop, 100m)], [new ClosedIdea(done.Id, new TradedIdeaOutcome(IdeaStatus.Stop, Now.AddHours(2), -1.06m))], CancellationToken.None);

        (await _ideas.GetClosedOfTradesWithoutOutcomeAsync(CancellationToken.None)).ShouldBe([target]);
        waiting.IsOpen.ShouldBeTrue();
    }

    private async Task<(Idea Idea, IdeaTrade Trade)> TradeOfIdeaAsync(IdeaSetup setup = IdeaSetup.Impulse)
    {
        Idea idea = IdeaGenerator.Generate(Now, setup: setup);
        await _ideas.SaveAsync([idea], [], CancellationToken.None);
        var trade = IdeaTrade.Create(Guid.NewGuid(), idea, _account, 106m, new IdeaTradeSize(0.47m, 49.82m, 0.2m, 0.368m, 1.84m), Now);
        var entry = IdeaTradeOrder.NewEntry(trade.Id, 1, OrderSide.Buy, OrderType.Market, TimeInForce.GoodTilCancelled, null, 0.47m, new Protection(trade.Target, trade.Stop), Now);
        await _executor.ExecuteAsync(
            async token =>
            {
                await _trades.CreateAsync(trade, entry, Now, token);

                return 0;
            },
            CancellationToken.None);

        return (idea, trade);
    }

    private static Idea Closed(Idea idea, IdeaStatus status, decimal exit)
    {
        return idea with { Track = idea.Track with { Status = status, StatusAt = Now.AddHours(2), EnteredAt = Now, ExitPrice = exit } };
    }

    private async Task<DateTimeOffset> UpdatedAtAsync(Guid trade)
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();

        return new DateTimeOffset(await connection.QuerySingleAsync<DateTime>("SELECT updated_at FROM idea_trades WHERE id = @trade", new { trade }), TimeSpan.Zero);
    }
}