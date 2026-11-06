using System.Text;

using Npgsql;

using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;
using ProfitFox.Infrastructure.Persistence.Postgres.Transactions;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.IdeaAutos;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Decision 6: the requests of the autos with money go one at a time by the minute, the place of the auto and the order of the idea.</summary>
public class IdeaAutoEntryRepositoryTests
{
    private static readonly DateTimeOffset Minute = DateTimeOffset.Parse("2026-11-07T13:41:00Z");
    private static readonly Guid Owner = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private NpgsqlDataSource _dataSource = null!;
    private IdeaRepository _ideas = null!;
    private IdeaAutoEntryRepository _entries = null!;
    private IdeaAutoRefusalRepository _refusals = null!;
    private PostgresTransactionExecutor _executor = null!;
    private Guid _account;
    private IdeaAuto _auto = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
        var accessor = new PostgresTransactionAccessor();
        _executor = new PostgresTransactionExecutor(_dataSource, accessor);
        _ideas = new IdeaRepository(_dataSource);
        _entries = new IdeaAutoEntryRepository(_dataSource, accessor);
        _refusals = new IdeaAutoRefusalRepository(_dataSource, accessor);
        var account = new ExchangeAccount(Guid.NewGuid(), "Main", "key", 100, new HashSet<MarketCategory> { MarketCategory.Linear }, Minute);
        await new ExchangeAccountRepository(_dataSource).AddAsync(account, new EncryptedSecret([1], Encoding.UTF8.GetBytes("cipher"), [2]), CancellationToken.None);
        _account = account.Id;
        _auto = await new IdeaAutoRepository(_dataSource, new PostgresTransactionAccessor()).CreateAsync(IdeaAutoGenerator.Generate(_account, mode: IdeaAutoMode.Live), Minute, CancellationToken.None);
        Assert.That(await new ScannerStateRepository(_dataSource).ClaimAsync(Owner, Minute, Minute.AddHours(1), CancellationToken.None));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Next_request_is_the_earliest_by_the_minute_the_place_the_order_and_the_id()
    {
        Idea[] ideas = [.. Enumerable.Range(0, 5).Select(index => IdeaGenerator.Generate(Minute, setup: (IdeaSetup)index))];
        IdeaAutoEntry later = Entry(ideas[0], rank: 0, fired: 0, minute: Minute.AddMinutes(1));
        IdeaAutoEntry lower = Entry(ideas[1], rank: 1, fired: 0, minute: Minute);
        IdeaAutoEntry second = Entry(ideas[2], rank: 0, fired: 1, minute: Minute);
        IdeaAutoEntry top = Entry(ideas[3], rank: 0, fired: 0, minute: Minute);

        await _ideas.SaveAsync(ideas, [], [], new AutoMinute(Owner, [], [], [later, lower, second, top], []), CancellationToken.None);

        var order = new List<Guid>();

        for (int taken = 0; taken < 4; taken++)
        {
            IdeaAutoEntry next = (await _entries.GetNextAsync(CancellationToken.None))!;
            order.Add(next.Id);
            await _executor.ExecuteAsync(async token => await DeleteAsync(next.Id, token), CancellationToken.None);
        }

        order.ShouldBe([top.Id, second.Id, lower.Id, later.Id]);
        (await _entries.GetNextAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Request_is_locked_in_a_unit_of_work_and_none_is_found_once_it_is_gone()
    {
        Idea idea = IdeaGenerator.Generate(Minute);
        IdeaAutoEntry entry = Entry(idea, 0, 0, Minute);
        await _ideas.SaveAsync([idea], [], [], new AutoMinute(Owner, [], [], [entry], []), CancellationToken.None);

        IdeaAutoEntry? locked = await _executor.ExecuteAsync(async token => await _entries.LockAsync(entry.Id, token), CancellationToken.None);
        IdeaAutoEntry? missing = await _executor.ExecuteAsync(async token => await _entries.LockAsync(Guid.NewGuid(), token), CancellationToken.None);

        locked.ShouldBe(entry);
        missing.ShouldBeNull();
    }

    // Decision 6: a second Worker does not take the requests while one holds the lock; it takes them once the first lets go.
    [Test]
    public async Task Lock_of_the_requests_is_held_by_one_Worker_at_a_time()
    {
        await using (IAsyncDisposable? first = await _entries.TryLockEntriesAsync(CancellationToken.None))
        {
            first.ShouldNotBeNull();
            (await _entries.TryLockEntriesAsync(CancellationToken.None)).ShouldBeNull();
        }

        await using IAsyncDisposable? again = await _entries.TryLockEntriesAsync(CancellationToken.None);
        again.ShouldNotBeNull();
    }

    // Decision 6: an hour later the scanner turns the requests nobody took into refusals «Не успел войти» and skips the one a Worker holds.
    [Test]
    public async Task Requests_nobody_took_become_refusals_and_a_request_in_hand_is_skipped()
    {
        Idea old = IdeaGenerator.Generate(Minute, setup: IdeaSetup.Impulse);
        Idea held = IdeaGenerator.Generate(Minute, setup: IdeaSetup.Drop);
        Idea fresh = IdeaGenerator.Generate(Minute, setup: IdeaSetup.Squeeze);
        IdeaAutoEntry oldEntry = Entry(old, 0, 0, Minute) with { CreatedAt = Minute.AddSeconds(10) };
        IdeaAutoEntry heldEntry = Entry(held, 0, 1, Minute) with { CreatedAt = Minute.AddSeconds(10) };
        IdeaAutoEntry freshEntry = Entry(fresh, 0, 2, Minute.AddHours(1)) with { CreatedAt = Minute.AddHours(1) };
        await _ideas.SaveAsync([old, held, fresh], [], [], new AutoMinute(Owner, [], [], [oldEntry, heldEntry, freshEntry], []), CancellationToken.None);
        DateTimeOffset now = Minute.AddHours(1).AddSeconds(30);

        int expired = await _executor.ExecuteAsync(
            async token =>
            {
                await _entries.LockAsync(heldEntry.Id, token);

                // Another connection works while this one holds the row.
                return await _entries.ExpireAsync(now.AddSeconds(-60), now, CancellationToken.None);
            },
            CancellationToken.None);

        expired.ShouldBe(1);
        IdeaAutoRefusal refusal = (await _refusals.GetPageAsync(Minute, null, 0, 10, CancellationToken.None)).Items.Single();
        (refusal.IdeaId, refusal.AutoId, refusal.Mode, refusal.At, refusal.Setup, refusal.Reasons.Single(), refusal.Detail).ShouldBe((old.Id, _auto.Id, IdeaAutoMode.Live, now, IdeaSetup.Impulse, IdeaTradeRefusal.Late, null));
        (await _entries.GetNextAsync(CancellationToken.None))!.Id.ShouldBe(heldEntry.Id);
    }

    private IdeaAutoEntry Entry(Idea idea, int rank, int fired, DateTimeOffset minute)
    {
        return new IdeaAutoEntry(Guid.NewGuid(), idea.Id, _auto.Id, _account, 300m, 1.2m, rank, fired, minute, minute.AddSeconds(10));
    }

    private async Task<int> DeleteAsync(Guid id, CancellationToken token)
    {
        await _entries.DeleteAsync(id, token);

        return 0;
    }
}