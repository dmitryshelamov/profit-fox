using System.Text;

using Npgsql;

using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos;
using ProfitFox.Infrastructure.Persistence.Postgres.Transactions;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.IdeaAutos;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Decision 12: one refusal per idea and auto, kept 7 days, read from the newest.</summary>
public class IdeaAutoRefusalRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-07T15:00:00Z");

    private static readonly DateTimeOffset Today = new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);

    private NpgsqlDataSource _dataSource = null!;
    private PostgresTransactionExecutor _executor = null!;
    private IdeaAutoRefusalRepository _repository = null!;
    private IdeaAuto _first = null!;
    private IdeaAuto _second = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
        var accessor = new PostgresTransactionAccessor();
        _executor = new PostgresTransactionExecutor(_dataSource, accessor);
        _repository = new IdeaAutoRefusalRepository(_dataSource, accessor);
        var account = new ExchangeAccount(Guid.NewGuid(), "Main", "key", 100, new HashSet<MarketCategory> { MarketCategory.Linear }, Now);
        await new ExchangeAccountRepository(_dataSource).AddAsync(account, new EncryptedSecret([1], Encoding.UTF8.GetBytes("cipher"), [2]), CancellationToken.None);
        var autos = new IdeaAutoRepository(_dataSource);
        _first = await autos.CreateAsync(IdeaAutoGenerator.Generate(account.Id, "Импульс лонг"), Now, CancellationToken.None);
        _second = await autos.CreateAsync(IdeaAutoGenerator.Generate(account.Id, "Пробои 5м"), Now, CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    // Scenario "Одна запись": the second refusal of the same idea and auto is not written.
    [Test]
    public async Task Second_refusal_of_an_idea_and_an_auto_is_not_written()
    {
        var idea = Guid.NewGuid();

        await AddAsync(Refusal(_first, idea, Now, IdeaTradeRefusal.NotEnoughMoney));
        await AddAsync(Refusal(_first, idea, Now.AddMinutes(1), IdeaTradeRefusal.DailyStop));
        await AddAsync(Refusal(_second, idea, Now, IdeaTradeRefusal.PairTaken));

        (IReadOnlyList<IdeaAutoRefusal> items, int total) = await _repository.GetPageAsync(Now.AddDays(-1), null, 0, 10, CancellationToken.None);
        total.ShouldBe(2);
        items.Single(item => item.AutoId == _first.Id).Reasons.ShouldBe([IdeaTradeRefusal.NotEnoughMoney]);
    }

    // Scenario "Сегодня по автомату": the newest first, one auto, and a page of it.
    [Test]
    public async Task Page_is_read_from_the_newest_for_one_auto_since_a_moment()
    {
        await AddAsync(Refusal(_first, Guid.NewGuid(), Now.AddHours(-30), IdeaTradeRefusal.NotEnoughMoney));
        await AddAsync(Refusal(_first, Guid.NewGuid(), Now.AddMinutes(-30), IdeaTradeRefusal.DailyStop));
        await AddAsync(Refusal(_second, Guid.NewGuid(), Now.AddMinutes(-20), IdeaTradeRefusal.PairTaken));
        await AddAsync(Refusal(_first, Guid.NewGuid(), Now.AddMinutes(-10), IdeaTradeRefusal.RiskRewardTooLow));
        DateTimeOffset today = Today;

        (IReadOnlyList<IdeaAutoRefusal> all, int allTotal) = await _repository.GetPageAsync(today, null, 0, 10, CancellationToken.None);
        (IReadOnlyList<IdeaAutoRefusal> mine, int mineTotal) = await _repository.GetPageAsync(today, _first.Id, 0, 10, CancellationToken.None);
        (IReadOnlyList<IdeaAutoRefusal> page, int pageTotal) = await _repository.GetPageAsync(today, null, 1, 1, CancellationToken.None);

        allTotal.ShouldBe(3);
        all.Select(item => item.Reasons.Single()).ShouldBe([IdeaTradeRefusal.RiskRewardTooLow, IdeaTradeRefusal.PairTaken, IdeaTradeRefusal.DailyStop]);
        (mineTotal, mine.Select(item => item.AutoId).Distinct().Single()).ShouldBe((2, _first.Id));
        (pageTotal, page.Single().Reasons.Single()).ShouldBe((3, IdeaTradeRefusal.PairTaken));
    }

    [Test]
    public async Task Detail_and_every_reason_survive_the_round_trip()
    {
        var detail = RefusalDetail.Pair(new PairHolder(PairHolderKind.Manual, null, false, false, "Bybit · ideas", Now.AddHours(-2)));
        IdeaAutoRefusal refusal = Refusal(_first, Guid.NewGuid(), Now, IdeaTradeRefusal.PairTaken, IdeaTradeRefusal.TooManySameDirection) with { Detail = detail };

        await AddAsync(refusal);

        IdeaAutoRefusal read = (await _repository.GetPageAsync(Now.AddDays(-1), null, 0, 10, CancellationToken.None)).Items.Single();
        read.Reasons.ShouldBe([IdeaTradeRefusal.PairTaken, IdeaTradeRefusal.TooManySameDirection]);
        (read.Detail, read.Instrument, read.Side).ShouldBe((detail, refusal.Instrument, IdeaSide.Long));
    }

    [Test]
    public async Task Refusals_are_counted_by_auto_and_the_old_ones_are_deleted()
    {
        await AddAsync(Refusal(_first, Guid.NewGuid(), Now.AddDays(-8), IdeaTradeRefusal.NotEnoughMoney));
        await AddAsync(Refusal(_first, Guid.NewGuid(), Now.AddHours(-1), IdeaTradeRefusal.DailyStop));
        await AddAsync(Refusal(_first, Guid.NewGuid(), Now.AddHours(-2), IdeaTradeRefusal.DailyStop));
        await AddAsync(Refusal(_second, Guid.NewGuid(), Now.AddHours(-3), IdeaTradeRefusal.PairTaken));

        IReadOnlyDictionary<Guid, int> counts = await _repository.CountSinceAsync(Today, CancellationToken.None);
        (counts[_first.Id], counts[_second.Id], counts.Count).ShouldBe((2, 1, 2));
        (await _repository.DeleteBeforeAsync(Now.AddDays(-7), CancellationToken.None)).ShouldBe(1);
        (await _repository.GetPageAsync(Now.AddDays(-30), null, 0, 10, CancellationToken.None)).Total.ShouldBe(3);
    }

    private Task AddAsync(IdeaAutoRefusal refusal)
    {
        return _executor.ExecuteAsync(
            async token =>
            {
                await _repository.AddAsync(refusal, token);

                return 0;
            },
            CancellationToken.None);
    }

    private static IdeaAutoRefusal Refusal(IdeaAuto auto, Guid idea, DateTimeOffset at, params IdeaTradeRefusal[] reasons)
    {
        return new IdeaAutoRefusal(Guid.NewGuid(), idea, auto.Id, IdeaAutoMode.Live, at, new Instrument(MarketCategory.Linear, "SUIUSDT"), IdeaSetup.Impulse, Interval.FifteenMinutes, IdeaType.TwoR, IdeaSide.Long, reasons, null);
    }
}