using Dapper;

using Npgsql;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.LiveTrading;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.LiveTrading;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres.Transactions;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class LiveBotRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:00:00Z");
    private static readonly Guid OwnerA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid OwnerB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    private NpgsqlDataSource _dataSource = null!;
    private PostgresTransactionExecutor _executor = null!;
    private LiveBotRepository _repository = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
        var accessor = new PostgresTransactionAccessor();
        _executor = new PostgresTransactionExecutor(_dataSource, accessor);
        _repository = new LiveBotRepository(_dataSource, accessor);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Bot_that_never_started_reads_as_stopped()
    {
        Guid bot = await AddBotAsync();

        (await _repository.GetAsync(bot, CancellationToken.None)).ShouldBeEquivalentTo(LiveBot.New(bot));
        (await _repository.GetAllAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    [Test]
    public async Task Locked_bot_is_created_and_saved()
    {
        Guid bot = await AddBotAsync();
        LiveBot started = LiveBot.New(bot).Start(1, new Dictionary<string, NodeValue> { ["fast"] = new NumberValue(10m) }) with { LastOrderNumber = 3, LastBarAt = Now };

        await _executor.ExecuteAsync(
            async cancellationToken =>
            {
                (await _repository.LockAsync(bot, Now, cancellationToken)).ShouldBeEquivalentTo(LiveBot.New(bot));
                await _repository.SaveAsync(started, Now, cancellationToken);

                return 0;
            },
            CancellationToken.None);

        (await _repository.GetAsync(bot, CancellationToken.None)).ShouldBeEquivalentTo(started);
        (await _repository.GetAllAsync(CancellationToken.None)).Keys.ShouldBe([bot]);
    }

    [Test]
    public async Task Unknown_bot_is_not_locked()
    {
        LiveBot? locked = await _executor.ExecuteAsync(cancellationToken => _repository.LockAsync(Guid.NewGuid(), Now, cancellationToken), CancellationToken.None);

        locked.ShouldBeNull();
    }

    [Test]
    public async Task Only_a_kept_bot_is_claimed()
    {
        Guid stopped = await AddBotAsync();
        await SaveAsync(LiveBot.New(stopped));

        (await _repository.ClaimAsync(OwnerA, Now, Now.AddSeconds(30), CancellationToken.None)).ShouldBeNull();

        await SaveAsync(LiveBot.New(stopped) with { CloseRequested = true });
        (await _repository.ClaimAsync(OwnerA, Now, Now.AddSeconds(30), CancellationToken.None))!.Bot.BotId.ShouldBe(stopped);
    }

    // Scenario "Два торгующих Worker'а": a bot with a live lease is nobody else's.
    [Test]
    public async Task Leased_bot_is_not_claimed_twice()
    {
        Guid bot = await AddRunningBotAsync();

        LeasedBot first = (await _repository.ClaimAsync(OwnerA, Now, Now.AddSeconds(30), CancellationToken.None))!;

        first.Bot.Status.ShouldBe(LiveBotStatus.Running);
        (await _repository.ClaimAsync(OwnerB, Now.AddSeconds(10), Now.AddSeconds(40), CancellationToken.None)).ShouldBeNull();
        (await _repository.RenewAllAsync(OwnerA, [(bot, first.Epoch)], Now.AddSeconds(50), CancellationToken.None)).ShouldHaveSingleItem().Bot.Status.ShouldBe(LiveBotStatus.Running);
    }

    // Scenario "Аренда перехвачена": the stalled take is fenced off even when its Worker takes the bot again.
    [Test]
    public async Task Claim_after_expiry_fences_the_previous_take()
    {
        Guid bot = await AddRunningBotAsync();
        LeasedBot first = (await _repository.ClaimAsync(OwnerA, Now, Now.AddSeconds(30), CancellationToken.None))!;

        LeasedBot second = (await _repository.ClaimAsync(OwnerA, Now.AddSeconds(31), Now.AddSeconds(61), CancellationToken.None))!;

        second.Epoch.ShouldBeGreaterThan(first.Epoch);
        (await _repository.RenewAllAsync(OwnerA, [(bot, first.Epoch)], Now.AddSeconds(70), CancellationToken.None)).ShouldBeEmpty();
        LiveBot? fenced = await _executor.ExecuteAsync(cancellationToken => _repository.LockLeasedAsync(bot, OwnerA, first.Epoch, cancellationToken), CancellationToken.None);
        fenced.ShouldBeNull();
        LiveBot? held = await _executor.ExecuteAsync(cancellationToken => _repository.LockLeasedAsync(bot, OwnerA, second.Epoch, cancellationToken), CancellationToken.None);
        held!.BotId.ShouldBe(bot);
    }

    [Test]
    public async Task Released_bot_is_free_at_once()
    {
        Guid bot = await AddRunningBotAsync();
        LeasedBot first = (await _repository.ClaimAsync(OwnerA, Now, Now.AddSeconds(30), CancellationToken.None))!;

        await _repository.ReleaseAsync(bot, OwnerA, first.Epoch, CancellationToken.None);

        (await _repository.ClaimAsync(OwnerB, Now.AddSeconds(1), Now.AddSeconds(31), CancellationToken.None))!.Bot.BotId.ShouldBe(bot);
    }

    // Requirement "Продление аренд ботов": one query renews every bot of the Worker that this take still holds.
    [Test]
    public async Task Renewal_of_all_bots_skips_a_bot_another_worker_took()
    {
        await AddTwoRunningBotsAsync();
        LeasedBot held = (await _repository.ClaimAsync(OwnerA, Now, Now.AddSeconds(60), CancellationToken.None))!;
        LeasedBot lost = (await _repository.ClaimAsync(OwnerA, Now, Now.AddSeconds(30), CancellationToken.None))!;
        (await _repository.ClaimAsync(OwnerB, Now.AddSeconds(31), Now.AddSeconds(300), CancellationToken.None))!.Bot.BotId.ShouldBe(lost.Bot.BotId);

        IReadOnlyList<LeasedBot> renewed = await _repository.RenewAllAsync(
            OwnerA, [(held.Bot.BotId, held.Epoch), (lost.Bot.BotId, lost.Epoch)], Now.AddSeconds(90), CancellationToken.None);

        renewed.Select(bot => (bot.Bot.BotId, bot.Epoch)).ShouldBe([(held.Bot.BotId, held.Epoch)]);
        (await _repository.ClaimAsync(OwnerB, Now.AddSeconds(80), Now.AddSeconds(110), CancellationToken.None)).ShouldBeNull();
    }

    // Requirement "Команды владельца доходят до бота": the Worker reads which of its bots changed since it looked last.
    [Test]
    public async Task Changed_bots_of_the_worker_are_read_with_the_time_of_their_change()
    {
        await AddTwoRunningBotsAsync();
        LeasedBot mine = (await _repository.ClaimAsync(OwnerA, Now, Now.AddSeconds(30), CancellationToken.None))!;
        LeasedBot other = (await _repository.ClaimAsync(OwnerB, Now, Now.AddSeconds(30), CancellationToken.None))!;
        await SaveAsync(mine.Bot.Stop(), Now.AddSeconds(10));
        await SaveAsync(other.Bot.Stop(), Now.AddSeconds(10));

        IReadOnlyList<ChangedLiveBot> changed = await _repository.GetChangedAsync(OwnerA, Now.AddSeconds(5), CancellationToken.None);

        changed.Select(bot => (bot.Bot.BotId, bot.Bot.Status, bot.Epoch, bot.UpdatedAt)).ShouldBe([(mine.Bot.BotId, LiveBotStatus.Stopping, mine.Epoch, Now.AddSeconds(10))]);
        (await _repository.GetChangedAsync(OwnerA, Now.AddSeconds(10), CancellationToken.None)).ShouldBeEmpty();
    }

    // Requirement "Отпускание аренд при остановке": only the takes the Worker still holds are given up.
    [Test]
    public async Task Release_of_all_bots_gives_up_only_the_takes_still_held()
    {
        await AddTwoRunningBotsAsync();
        LeasedBot held = (await _repository.ClaimAsync(OwnerA, Now, Now.AddSeconds(300), CancellationToken.None))!;
        LeasedBot stale = (await _repository.ClaimAsync(OwnerA, Now, Now.AddSeconds(30), CancellationToken.None))!;
        LeasedBot again = (await _repository.ClaimAsync(OwnerA, Now.AddSeconds(31), Now.AddSeconds(300), CancellationToken.None))!;
        again.Bot.BotId.ShouldBe(stale.Bot.BotId);

        await _repository.ReleaseAllAsync(OwnerA, [(held.Bot.BotId, held.Epoch), (stale.Bot.BotId, stale.Epoch)], CancellationToken.None);

        (await _repository.ClaimAsync(OwnerB, Now.AddSeconds(32), Now.AddSeconds(300), CancellationToken.None))!.Bot.BotId.ShouldBe(held.Bot.BotId);
        (await _repository.ClaimAsync(OwnerB, Now.AddSeconds(32), Now.AddSeconds(300), CancellationToken.None)).ShouldBeNull();
    }

    // An owner's command locks the bot row and then the live bot; a write of the session that adds a position must not hold the
    // live bot while it waits for the bot row of its position.
    [Test]
    public async Task Owner_command_waits_for_a_session_write_that_adds_a_position()
    {
        Guid bot = await AddRunningBotAsync();
        LeasedBot leased = (await _repository.ClaimAsync(OwnerA, Now, Now.AddSeconds(30), CancellationToken.None))!;
        var ownerAccessor = new PostgresTransactionAccessor();
        var ownerRepository = new LiveBotRepository(_dataSource, ownerAccessor);
        var sessionLocked = new TaskCompletionSource();

        Task session = _executor.ExecuteAsync(
            async cancellationToken =>
            {
                (await _repository.LockLeasedAsync(bot, OwnerA, leased.Epoch, cancellationToken)).ShouldNotBeNull();
                sessionLocked.SetResult();
                await WaitForLockWaitAsync();
                await _repository.SavePositionAsync(new LivePosition(bot, 1, true, false, 1m, 100m, 100m, 0m, 0m, 100m, Now, 0, null), cancellationToken);

                return 0;
            },
            CancellationToken.None);
        await sessionLocked.Task;
        Task owner = new PostgresTransactionExecutor(_dataSource, ownerAccessor).ExecuteAsync(
            async cancellationToken =>
            {
                LiveBot locked = (await ownerRepository.LockAsync(bot, Now, cancellationToken))!;
                await ownerRepository.SaveAsync(locked.Stop(), Now, cancellationToken);

                return 0;
            },
            CancellationToken.None);

        await Task.WhenAll(session, owner);

        (await _repository.GetAsync(bot, CancellationToken.None)).Status.ShouldBe(LiveBotStatus.Stopping);
        (await _repository.GetPositionsAsync(bot, CancellationToken.None)).ShouldHaveSingleItem().Number.ShouldBe(1);
    }

    [Test]
    public async Task Positions_orders_and_fills_are_kept()
    {
        Guid bot = await AddRunningBotAsync();
        var closed = new LivePosition(bot, 1, false, false, 0m, 0m, 0m, 5m, 0.2m, 100m, Now, 0, Now.AddHours(1));
        var current = new LivePosition(bot, 2, true, false, 1m, 100m, 100m, 0m, 0.1m, 100m, Now.AddHours(2), 1, null);
        LiveOrder filled = Order(bot, 1, 1) with { Status = LiveOrderStatus.Filled, FilledQuantity = 1m };
        LiveOrder active = Order(bot, 2, 2) with { Status = LiveOrderStatus.Active, ExchangeOrderId = "x-2" };
        LiveOrder pending = Order(bot, 3, 2);

        await _executor.ExecuteAsync(
            async cancellationToken =>
            {
                await _repository.SavePositionAsync(closed, cancellationToken);
                await _repository.SavePositionAsync(current with { Quantity = 0.5m }, cancellationToken);
                await _repository.SavePositionAsync(current, cancellationToken);
                await _repository.SaveOrderAsync(pending, cancellationToken);
                await _repository.SaveOrderAsync(filled, cancellationToken);
                await _repository.SaveOrderAsync(active, cancellationToken);
                (await _repository.AddFillAsync(new LiveFill("e-1", bot, 1, 1m, 100m, 0.1m, FeeAsset.Quote, Now), cancellationToken)).ShouldBeTrue();
                (await _repository.GetCurrentPositionAsync(bot, cancellationToken)).ShouldBe(current);
                (await _repository.GetPastPnlAsync(bot, cancellationToken)).ShouldBe(5m);
                (await _repository.GetUnfinishedOrdersAsync(bot, cancellationToken)).ShouldBe([active, pending]);

                return 0;
            },
            CancellationToken.None);

        (await _repository.GetPositionsAsync(bot, CancellationToken.None)).ShouldBe([current, closed]);
        (await _repository.GetOrdersAsync(bot, CancellationToken.None)).ShouldBe([filled, active, pending]);
        (await _repository.GetFillsAsync(bot, CancellationToken.None)).ShouldBe([new LiveFill("e-1", bot, 1, 1m, 100m, 0.1m, FeeAsset.Quote, Now)]);
    }

    // Scenario "Повтор исполнения": the exchange's id of a fill is written once.
    [Test]
    public async Task Repeated_fill_is_not_added()
    {
        Guid bot = await AddRunningBotAsync();
        var fill = new LiveFill("e-1", bot, 1, 0.5m, 100m, 0m, FeeAsset.Quote, Now);

        bool second = await _executor.ExecuteAsync(
            async cancellationToken =>
            {
                await _repository.SavePositionAsync(new LivePosition(bot, 1, false, false, 0m, 0m, 0m, 0m, 0m, null, null, 0, null), cancellationToken);
                await _repository.SaveOrderAsync(Order(bot, 1, 1), cancellationToken);
                await _repository.AddFillAsync(fill, cancellationToken);

                return await _repository.AddFillAsync(fill with { Quantity = 0.7m }, cancellationToken);
            },
            CancellationToken.None);

        second.ShouldBeFalse();
        (await _repository.GetFillsAsync(bot, CancellationToken.None)).ShouldBe([fill]);
    }

    [Test]
    public async Task Writes_need_a_unit_of_work()
    {
        Guid bot = await AddRunningBotAsync();

        await Should.ThrowAsync<InvalidOperationException>(() => _repository.SaveAsync(LiveBot.New(bot), Now, CancellationToken.None));
    }

    [TestCase("live_bots", "status")]
    [TestCase("live_orders", "status")]
    public async Task Tables_take_only_known_codes(string table, string column)
    {
        Guid bot = await AddRunningBotAsync();
        await _executor.ExecuteAsync(
            async cancellationToken =>
            {
                await _repository.SavePositionAsync(new LivePosition(bot, 1, false, false, 0m, 0m, 0m, 0m, 0m, null, null, 0, null), cancellationToken);
                await _repository.SaveOrderAsync(Order(bot, 1, 1), cancellationToken);

                return 0;
            },
            CancellationToken.None);
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();

        PostgresException exception = await Should.ThrowAsync<PostgresException>(() => connection.ExecuteAsync($"UPDATE {table} SET {column} = 'unknown'"));
        exception.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    private static LiveOrder Order(Guid bot, int number, int position)
    {
        return new LiveOrder(bot, number, position, "exit", OrderSide.Sell, OrderType.Limit, 101m, 1m, null, 0m, LiveOrderStatus.PendingSubmit, 0m, null, null, 0, null, Now, Now);
    }

    private async Task<Guid> AddRunningBotAsync()
    {
        Guid bot = await AddBotAsync();
        await SaveAsync(LiveBot.New(bot).Start(1, new Dictionary<string, NodeValue>()));

        return bot;
    }

    private async Task AddTwoRunningBotsAsync()
    {
        var library = new StrategyLibrary(0, [], []);
        Strategy ema = library.AddStrategy(Guid.NewGuid(), "EMA", StrategyJson.ParseStrategy(StrategyFixtures.Element("ema")), Now);
        Guid[] bots =
        [
            .. Enumerable.Range(1, 2).Select(number => library.AddBot(
                Guid.NewGuid(), $"EMA BTC {number}", ema.Id, new Dictionary<string, NodeValue>(), new Instrument(MarketCategory.Linear, "BTCUSDT"), Interval.OneHour, 1000m, Now).Id)
        ];
        await new StrategyLibraryRepository(_dataSource).SaveAsync(library.Change, 0, CancellationToken.None);

        foreach (Guid bot in bots)
        {
            await SaveAsync(LiveBot.New(bot).Start(1, new Dictionary<string, NodeValue>()));
        }
    }

    private Task SaveAsync(LiveBot bot, DateTimeOffset? at = null)
    {
        return _executor.ExecuteAsync(
            async cancellationToken =>
            {
                await _repository.LockAsync(bot.BotId, Now, cancellationToken);
                await _repository.SaveAsync(bot, at ?? Now, cancellationToken);

                return 0;
            },
            CancellationToken.None);
    }

    // Until another transaction of the test database waits for a row lock.
    private async Task WaitForLockWaitAsync()
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();

        while (!await connection.ExecuteScalarAsync<bool>(
                   "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock')"))
        {
            await Task.Delay(10);
        }
    }

    private async Task<Guid> AddBotAsync()
    {
        var library = new StrategyLibrary(0, [], []);
        Strategy ema = library.AddStrategy(Guid.NewGuid(), "EMA", StrategyJson.ParseStrategy(StrategyFixtures.Element("ema")), Now);
        Bot bot = library.AddBot(Guid.NewGuid(), "EMA BTC", ema.Id, new Dictionary<string, NodeValue>(), new Instrument(MarketCategory.Linear, "BTCUSDT"), Interval.OneHour, 1000m, Now);
        await new StrategyLibraryRepository(_dataSource).SaveAsync(library.Change, 0, CancellationToken.None);

        return bot.Id;
    }
}