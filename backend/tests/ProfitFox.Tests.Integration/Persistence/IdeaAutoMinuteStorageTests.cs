using System.Text;

using Npgsql;

using ProfitFox.Application.Scanner;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.IdeaAutos;
using ProfitFox.Domain.Services.IdeaTrades;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;
using ProfitFox.Infrastructure.Persistence.Postgres.Transactions;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.IdeaAutos;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Decision 2: what the autos did in a minute is written in the transaction of the ideas of the minute, under the lease of the stream.</summary>
public class IdeaAutoMinuteStorageTests
{
    private static readonly DateTimeOffset Fired = DateTimeOffset.Parse("2026-11-07T13:40:00Z");
    private static readonly FeeRates Fees = new FeeRates(0.02m, 0.055m);
    private static readonly Guid Owner = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private NpgsqlDataSource _dataSource = null!;
    private IdeaRepository _ideas = null!;
    private PaperIdeaTradeRepository _paper = null!;
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
        _paper = new PaperIdeaTradeRepository(_dataSource);
        _entries = new IdeaAutoEntryRepository(_dataSource, accessor);
        _refusals = new IdeaAutoRefusalRepository(_dataSource, accessor);
        var account = new ExchangeAccount(Guid.NewGuid(), "Main", "key", 100, new HashSet<MarketCategory> { MarketCategory.Linear }, Fired);
        await new ExchangeAccountRepository(_dataSource).AddAsync(account, new EncryptedSecret([1], Encoding.UTF8.GetBytes("cipher"), [2]), CancellationToken.None);
        _account = account.Id;
        _auto = await new IdeaAutoRepository(_dataSource, new PostgresTransactionAccessor()).CreateAsync(IdeaAutoGenerator.Generate(_account, mode: IdeaAutoMode.Paper), Fired, CancellationToken.None);
        Assert.That(await new ScannerStateRepository(_dataSource).ClaimAsync(Owner, Fired, Fired.AddMinutes(5), CancellationToken.None));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Paper_trade_request_and_refusal_of_the_minute_are_written_with_the_idea()
    {
        Idea idea = IdeaGenerator.Generate(Fired);
        Idea refused = IdeaGenerator.Generate(Fired, setup: IdeaSetup.Drop);
        PaperIdeaTrade paper = Paper(idea);
        IdeaAutoEntry entry = Entry(idea);
        IdeaAutoRefusal refusal = Refusal(refused);

        await _ideas.SaveAsync([idea, refused], [], [], new AutoMinute(Owner, [paper], [], [entry], [refusal]), CancellationToken.None);

        (await _paper.GetOpenAsync(CancellationToken.None)).Single().ShouldBe(paper);
        (await _entries.GetNextAsync(CancellationToken.None)).ShouldBe(entry);
        Shape((await _refusals.GetPageAsync(Fired, null, 0, 10, CancellationToken.None)).Items.Single()).ShouldBe(Shape(refusal));
    }

    // Scenario "Чужая аренда": the scanner that lost the stream writes nothing at all, not even the ideas.
    [Test]
    public async Task Lease_of_another_scanner_rolls_everything_back()
    {
        Idea idea = IdeaGenerator.Generate(Fired);

        await Should.ThrowAsync<ScannerLeaseLostException>(
            () => _ideas.SaveAsync([idea], [], [], new AutoMinute(Guid.NewGuid(), [Paper(idea)], [], [Entry(idea)], [Refusal(idea)]), CancellationToken.None));

        (await _ideas.GetOpenAsync(CancellationToken.None)).ShouldBeEmpty();
        (await _paper.GetAllAsync(CancellationToken.None)).ShouldBeEmpty();
        (await _entries.GetNextAsync(CancellationToken.None)).ShouldBeNull();
        (await _refusals.GetPageAsync(Fired, null, 0, 10, CancellationToken.None)).Total.ShouldBe(0);
    }

    // Decision 2: an idea storage refused (one open idea per key) leaves nothing of the autos.
    [Test]
    public async Task Idea_that_storage_refused_leaves_no_paper_trade_no_request_and_no_refusal()
    {
        await _ideas.SaveAsync([IdeaGenerator.Generate(Fired)], [], CancellationToken.None);
        Idea twin = IdeaGenerator.Generate(Fired.AddMinutes(1));
        Idea other = IdeaGenerator.Generate(Fired.AddMinutes(1), setup: IdeaSetup.Drop);

        IReadOnlyList<Idea> added = await _ideas.SaveAsync(
            [twin, other],
            [],
            [],
            new AutoMinute(Owner, [Paper(twin), Paper(other)], [], [Entry(twin), Entry(other)], [Refusal(twin), Refusal(other)]),
            CancellationToken.None);

        added.ShouldBe([other]);
        (await _paper.GetAllAsync(CancellationToken.None)).Select(paper => paper.Trade.IdeaId).ShouldBe([other.Id]);
        (await _entries.GetNextAsync(CancellationToken.None))!.IdeaId.ShouldBe(other.Id);
        (await _refusals.GetPageAsync(Fired, null, 0, 10, CancellationToken.None)).Items.Select(item => item.IdeaId).ShouldBe([other.Id]);
    }

    [Test]
    public async Task Stepped_paper_trade_is_written_over_the_one_stored()
    {
        Idea idea = IdeaGenerator.Generate(Fired);
        PaperIdeaTrade paper = Paper(idea);
        await _ideas.SaveAsync([idea], [], [], new AutoMinute(Owner, [paper], [], [], []), CancellationToken.None);
        var minute = new IdeaCandle(Fired.AddMinutes(1), 100m, 104.1m, 99.5m, 104m, 10m);
        PaperIdeaTrade stepped = PaperFill.Step(paper, minute, Fees);

        await _ideas.SaveAsync([], [], [], new AutoMinute(Owner, [], [stepped], [], []), CancellationToken.None);

        (await _paper.GetAllAsync(CancellationToken.None)).Single().ShouldBe(stepped);
        (await _paper.GetOpenAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    // Scenario "Идея дошла до цели" for a paper trade: the outcome of the idea is written on it once, like on a real trade.
    [Test]
    public async Task Outcome_of_the_idea_reaches_the_paper_trade_once_and_the_catch_up_finds_it()
    {
        Idea idea = IdeaGenerator.Generate(Fired);
        await _ideas.SaveAsync([idea], [], [], new AutoMinute(Owner, [Paper(idea)], [], [], []), CancellationToken.None);
        Idea closed = idea with { Track = idea.Track with { Status = IdeaStatus.Target, StatusAt = Fired.AddHours(2), EnteredAt = Fired, ExitPrice = 115m } };
        var outcome = new TradedIdeaOutcome(IdeaStatus.Target, Fired.AddHours(2), 2m);

        (await _ideas.GetClosedOfTradesWithoutOutcomeAsync(CancellationToken.None)).ShouldBeEmpty();
        await _ideas.SaveAsync([], [closed], [new ClosedIdea(idea.Id, outcome)], CancellationToken.None);
        await _ideas.SaveAsync([], [], [new ClosedIdea(idea.Id, new TradedIdeaOutcome(IdeaStatus.Stop, Fired.AddHours(3), -1m))], CancellationToken.None);

        (await _paper.GetAllAsync(CancellationToken.None)).Single().Trade.IdeaOutcome.ShouldBe(outcome);
    }

    [Test]
    public async Task Closed_idea_of_a_paper_trade_without_an_outcome_is_found_by_the_catch_up()
    {
        Idea idea = IdeaGenerator.Generate(Fired, IdeaStatus.Target, statusAt: Fired.AddHours(2));
        await _ideas.SaveAsync([idea], [], [], new AutoMinute(Owner, [Paper(idea)], [], [], []), CancellationToken.None);

        (await _ideas.GetClosedOfTradesWithoutOutcomeAsync(CancellationToken.None)).ShouldBe([idea]);
    }

    private static object Shape(IdeaAutoRefusal refusal)
    {
        return (refusal.Id, refusal.IdeaId, refusal.AutoId, refusal.Mode, refusal.At, refusal.Instrument, refusal.Setup, refusal.Interval, refusal.Type, refusal.Side, string.Join(',', refusal.Reasons), refusal.Detail);
    }

    private PaperIdeaTrade Paper(Idea idea)
    {
        var birth = new IdeaCandle(Fired, 100m, 100.2m, 99.8m, 100m, 1m);
        IdeaPlan plan = idea.Track.Plan with { Entry = 100m, Stop = 98m, Target = 104m };
        Idea planned = idea with { Track = idea.Track with { Plan = plan } };
        IdeaTradeSize size = IdeaTradeSizing.Calculate(plan, Fees, 50m, new InstrumentRules(0.01m, 0.01m, 0.01m, 5m));

        return PaperFill.Open(Guid.NewGuid(), planned, _auto.Id, _account, size, new IdeaPromise(IdeaBadge.Flat, 0.3m, 0.01m, 120, 0.35m, 1.8m), birth, Fees);
    }

    private IdeaAutoEntry Entry(Idea idea)
    {
        return new IdeaAutoEntry(Guid.NewGuid(), idea.Id, _auto.Id, _account, 300m, 1.2m, 0, 1, Fired.AddMinutes(1), Fired.AddMinutes(1).AddSeconds(10));
    }

    private IdeaAutoRefusal Refusal(Idea idea)
    {
        var detail = RefusalDetail.Pair(new PairHolder(PairHolderKind.OtherAuto, "Импульс лонг", true, false, "Bybit · ideas", Fired));

        return new IdeaAutoRefusal(
            Guid.NewGuid(),
            idea.Id,
            _auto.Id,
            IdeaAutoMode.Live,
            Fired.AddMinutes(1),
            idea.Instrument,
            idea.Setup,
            idea.Interval,
            idea.Track.Plan.Type,
            idea.Track.Plan.Side,
            [IdeaTradeRefusal.PairTaken, IdeaTradeRefusal.DailyStop],
            detail);
    }
}