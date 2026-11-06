using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.IdeaAutos;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Fixtures.IdeaAutos;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.IdeaAutos;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner.Ideas;

/// <summary>
///     What the autos do in a minute of the scanner (requirements of idea-autos and paper-idea-trades), on the reference minutes of
///     <see cref="IdeaAutoFixtures" />: the idea was born in the minute of 13:40 that closed at 100, a long with the stop at 98 and the target at 104.
/// </summary>
public class LiveAutosTests
{
    private static readonly BacktestSettings Fees = new BacktestSettings { DefaultFees = new Dictionary<string, FeeRateSettings> { ["spot"] = new FeeRateSettings { Limit = 0.1m, Market = 0.1m }, ["linear"] = new FeeRateSettings { Limit = 0.02m, Market = 0.055m } } };
    private static readonly Instrument Wif = IdeaGenerator.Wif;
    private static readonly DateTimeOffset Fired = new(2026, 11, 7, 13, 40, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Fired.AddMinutes(1);
    private static readonly Guid Account = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly WorkerIdentity Scanner = new WorkerIdentity(Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"));

    private IIdeaAutoRepository _autos = null!;
    private IPaperIdeaTradeRepository _paper = null!;
    private IIdeaTradeRepository _trades = null!;
    private IIdeaTradingSettingsRepository _settings = null!;
    private IIdeaBucketRepository _buckets = null!;
    private ICandleRepository _candles = null!;
    private IIdeaAutoEntryRepository _entries = null!;
    private IIdeaAutoRefusalRepository _refusals = null!;
    private FakeTimeProvider _time = null!;
    private ScannerMarket _market = null!;
    private LiveAutos _live = null!;

    [SetUp]
    public void SetUp()
    {
        _autos = Substitute.For<IIdeaAutoRepository>();
        _paper = Substitute.For<IPaperIdeaTradeRepository>();
        _trades = Substitute.For<IIdeaTradeRepository>();
        _settings = Substitute.For<IIdeaTradingSettingsRepository>();
        _buckets = Substitute.For<IIdeaBucketRepository>();
        _candles = Substitute.For<ICandleRepository>();
        _entries = Substitute.For<IIdeaAutoEntryRepository>();
        _refusals = Substitute.For<IIdeaAutoRefusalRepository>();
        _time = new FakeTimeProvider(End);
        _market = new ScannerMarket();
        _autos.GetEnabledAsync(Arg.Any<CancellationToken>()).Returns([]);
        _autos.GetEveryAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, IdeaAuto>());
        _paper.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([]);
        _trades.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([]);
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(IdeaTradingSettings.Default with { MaxAmount = 500m });
        _buckets.GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        _market.Keep([new ScannerPair(Wif, true, CoveredUntil: End, TickSize: 0.01m, QuantityStep: 0.01m, MinQuantity: 0.01m, MinAmount: 5m)]);
        _live = new LiveAutos(_autos, _paper, _trades, _settings, _buckets, _candles, _entries, _refusals, new IdeasEngineVersion(IdeasEngineVersion.Current), _market, Fees, Scanner, _time);
    }

    // Scenario "Учебная сделка вместо ордера".
    [Test]
    public async Task Paper_auto_opens_a_paper_trade_of_the_idea_at_the_close_of_its_minute()
    {
        IdeaAuto auto = Enable(Auto("Пробои 5м", IdeaAutoMode.Paper, amount: 50m));
        Idea idea = Born();

        AutoMinute minute = (await RunAsync([idea]))!;

        minute.Owner.ShouldBe(Scanner.Id);
        PaperIdeaTrade paper = minute.Opened.ShouldHaveSingleItem();
        (paper.AutoId, paper.Trade.IdeaId, paper.Trade.AccountId, paper.Trade.EntryPrice, paper.Trade.Quantity, paper.Trade.Status).ShouldBe((auto.Id, idea.Id, Account, 100m, 0.5m, IdeaTradeStatus.Open));
        (paper.Trade.Promise!.Cases, paper.SteppedUntil).ShouldBe((null, End));
        minute.Entries.ShouldBeEmpty();
        minute.Refusals.ShouldBeEmpty();
        minute.Stepped.ShouldBeEmpty();
    }

    // Scenario "Вход по рынку" of the autos with money: the request carries the account, the amount and the lowest reward to risk of this minute.
    [Test]
    public async Task Live_auto_writes_a_request_with_its_settings_of_the_minute_and_nothing_else()
    {
        IdeaAuto auto = Enable(Auto("Импульс лонг", IdeaAutoMode.Live, position: 3, amount: 300m));
        Idea idea = Born();

        AutoMinute minute = (await RunAsync([Born(IdeaSetup.Squeeze), idea]))!;

        IdeaAutoEntry entry = minute.Entries.ShouldHaveSingleItem();
        (entry.IdeaId, entry.AutoId, entry.AccountId, entry.Amount, entry.MinRiskReward, entry.Rank, entry.Fired, entry.Minute, entry.CreatedAt).ShouldBe((idea.Id, auto.Id, Account, 300m, 1.2m, 3, 1, End, End));
        minute.Opened.ShouldBeEmpty();
        minute.Refusals.ShouldBeEmpty();
    }

    // Requirement "Только новые идеи" and the switched-off auto: with no auto on, nothing is read and nothing is written.
    [Test]
    public async Task No_auto_on_writes_nothing_and_reads_no_buckets()
    {
        AutoMinute? minute = await RunAsync([Born()]);

        minute.ShouldBeNull();
        await _buckets.DidNotReceiveWithAnyArgs().GetAsync(default, default);
    }

    // Scenario "Не тот ТФ": an idea that does not fit leaves no refusal.
    [Test]
    public async Task Idea_that_does_not_fit_the_conditions_leaves_no_trace()
    {
        Enable(Auto("Пробои 5м", IdeaAutoMode.Paper, amount: 50m));

        AutoMinute? minute = await RunAsync([Born(interval: Interval.FiveMinutes)]);

        minute.ShouldBeNull();
    }

    [Test]
    public async Task Badge_and_cases_of_the_bucket_of_the_idea_are_the_ones_the_conditions_look_at()
    {
        IdeaAuto auto = Enable(Auto("Пробои 5м", IdeaAutoMode.Paper, amount: 50m) with { Conditions = Conditions() with { Badge = IdeaBadge.Up, MinCases = 100 } });
        Idea idea = Born();
        var key = new IdeaBucketKey(idea.Setup, idea.Track.Plan.Type, idea.Interval, idea.Track.Plan.Side, MarketCategory.Linear);
        var statistics = new BucketStatistics(400, 0, 0, 0, 0.4m, 0.3m, 0.5m, 0.2m, 0.1m, 0.3m);
        _buckets.GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([new IdeaBucket(key, 90, statistics)]);
        _autos.GetEnabledAsync(Arg.Any<CancellationToken>()).Returns([auto]);

        AutoMinute minute = (await RunAsync([idea]))!;

        IdeaPromise promise = minute.Opened.Single().Trade.Promise!;
        (promise.Badge, promise.Cases, promise.Expectancy).ShouldBe((IdeaBadge.Up, 400, 0.2m));
    }

    // Scenario "R:R ниже минимума" for a paper auto: the refusal is written once, with the numbers.
    [Test]
    public async Task Idea_that_fits_and_fails_the_check_is_written_as_a_refusal_of_the_paper_auto()
    {
        IdeaAuto auto = Enable(Auto("Пробои 5м", IdeaAutoMode.Paper, amount: 50m) with { Conditions = Conditions() with { MinRiskReward = 5m } });
        Idea idea = Born();

        AutoMinute minute = (await RunAsync([idea]))!;

        IdeaAutoRefusal refusal = minute.Refusals.ShouldHaveSingleItem();
        (refusal.IdeaId, refusal.AutoId, refusal.Mode, refusal.At, refusal.Instrument, refusal.Setup, refusal.Interval, refusal.Type, refusal.Side).ShouldBe(
            (idea.Id, auto.Id, IdeaAutoMode.Paper, End, Wif, IdeaSetup.Impulse, Interval.FifteenMinutes, IdeaType.TwoR, IdeaSide.Long));
        refusal.Reasons.ShouldBe([IdeaTradeRefusal.RiskRewardTooLow]);
        refusal.Detail!.Limit.ShouldBe(5m);
        minute.Opened.ShouldBeEmpty();
    }

    [Test]
    public async Task Pair_whose_rules_are_not_known_yet_is_refused_for_the_rules()
    {
        _market.Keep([new ScannerPair(Wif, true, CoveredUntil: End, TickSize: 0.01m)]);
        Enable(Auto("Пробои 5м", IdeaAutoMode.Paper, amount: 50m));

        AutoMinute minute = (await RunAsync([Born()]))!;

        minute.Refusals.ShouldHaveSingleItem().Reasons.ShouldBe([IdeaTradeRefusal.RulesUnknown]);
    }

    // Scenario "Вторая идея той же пары": two ideas of the pair in one minute, the auto takes the first.
    [Test]
    public async Task Second_idea_of_the_same_pair_in_the_minute_is_refused_for_the_pair()
    {
        Enable(Auto("Пробои 5м", IdeaAutoMode.Paper, amount: 50m));
        Idea first = Born();
        Idea second = Born(IdeaSetup.Impulse, Interval.OneHour);

        AutoMinute minute = (await RunAsync([first, second]))!;

        minute.Opened.Single().Trade.IdeaId.ShouldBe(first.Id);
        IdeaAutoRefusal refusal = minute.Refusals.ShouldHaveSingleItem();
        (refusal.IdeaId, refusal.Reasons.Single(), refusal.Detail!.Holder!.Kind, refusal.Detail.Holder.Paper).ShouldBe((second.Id, IdeaTradeRefusal.PairTaken, PairHolderKind.SameAuto, true));
    }

    [Test]
    public async Task Paper_trade_kept_from_the_minutes_before_holds_the_pair()
    {
        IdeaAuto auto = Enable(Auto("Пробои 5м", IdeaAutoMode.Paper, amount: 50m));
        await RunAndCommitAsync([Born()]);

        AutoMinute minute = (await RunAsync([Born(IdeaSetup.Impulse, Interval.OneHour)], End.AddMinutes(1)))!;

        minute.Refusals.ShouldHaveSingleItem().Detail!.Holder!.Since.ShouldBe(End);
        minute.Opened.ShouldBeEmpty();
        auto.Mode.ShouldBe(IdeaAutoMode.Paper);
    }

    [Test]
    public async Task Open_real_trade_of_the_auto_holds_the_pair_for_its_paper_trades_too()
    {
        IdeaAuto auto = Enable(Auto("Пробои 5м", IdeaAutoMode.Paper, amount: 50m));
        var size = new IdeaTradeSize(0.5m, 50m, 1m, 2m, 2m);
        var real = IdeaTrade.Create(Guid.NewGuid(), Born(), Account, 100m, size, Fired, autoId: auto.Id);
        _trades.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([real]);

        AutoMinute minute = (await RunAsync([Born()]))!;

        minute.Refusals.ShouldHaveSingleItem().Detail!.Holder!.Paper.ShouldBeFalse();
    }

    // Requirement "Порядок автоматов": the autos take the idea by their place in the list.
    [Test]
    public async Task Autos_take_the_idea_in_the_order_of_the_list()
    {
        IdeaAuto lower = Auto("Нижний", IdeaAutoMode.Live, position: 1, amount: 100m);
        IdeaAuto top = Auto("Верхний", IdeaAutoMode.Live, position: 0, amount: 200m);
        _autos.GetEnabledAsync(Arg.Any<CancellationToken>()).Returns([top, lower]);

        AutoMinute minute = (await RunAsync([Born()]))!;

        minute.Entries.Select(entry => (entry.AutoId, entry.Rank)).ShouldBe([(top.Id, 0), (lower.Id, 1)]);
    }

    // Decision 2: what storage refused is not kept: the paper trade of an idea it did not take does not hold the pair.
    [Test]
    public async Task Paper_trade_of_an_idea_that_storage_refused_is_not_kept()
    {
        Enable(Auto("Пробои 5м", IdeaAutoMode.Paper, amount: 50m));
        Idea idea = Born();
        AutoMinute minute = (await RunAsync([idea]))!;

        _live.Commit(minute, []);
        AutoMinute next = (await RunAsync([Born()], End.AddMinutes(1)))!;

        next.Opened.ShouldHaveSingleItem();
    }

    [Test]
    public async Task Start_over_reads_the_open_paper_trades_again_from_storage()
    {
        Enable(Auto("Пробои 5м", IdeaAutoMode.Paper, amount: 50m));
        await RunAndCommitAsync([Born()]);
        _live.StartOver();

        AutoMinute next = (await RunAsync([Born()], End.AddMinutes(1)))!;

        next.Opened.ShouldHaveSingleItem();
        await _paper.Received(2).GetOpenAsync(Arg.Any<CancellationToken>());
    }

    // Scenario "Цель" in the minutes after: the trade closes on the first minute that touches the target and is kept no more.
    [Test]
    public async Task Open_paper_trade_is_stepped_by_the_minutes_after_the_birth_and_leaves_with_its_exit()
    {
        Enable(Auto("Пробои 5м", IdeaAutoMode.Paper, amount: 50m));
        await RunAndCommitAsync([Born()]);
        Load("paper-to-target", through: 4);

        AutoMinute minute = (await RunAsync([], End.AddMinutes(3)))!;
        _live.Commit(minute, []);

        PaperIdeaTrade stepped = minute.Stepped.ShouldHaveSingleItem();
        (stepped.Trade.Status, stepped.Trade.StatusAt, stepped.Trade.Result).ShouldBe((IdeaTradeStatus.ClosedAtTarget, Fired.AddMinutes(3), 1.9439m));
        (await RunAsync([], End.AddMinutes(4))).ShouldBeNull();
    }

    // Requirement "Режим автомата": the auto is off or gone, the trade it opened goes to its exit.
    [Test]
    public async Task Open_paper_trade_of_an_auto_that_is_off_is_still_stepped()
    {
        PaperIdeaTrade open = Opened(Auto("Пробои 5м", IdeaAutoMode.Off, amount: 50m));
        _paper.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([open]);
        Load("paper-to-stop", through: 3);

        AutoMinute minute = (await RunAsync([], End.AddMinutes(2)))!;

        minute.Stepped.ShouldHaveSingleItem().Trade.Status.ShouldBe(IdeaTradeStatus.ClosedAtStop);
    }

    // Requirement "Учебные сделки после перерыва сканера": the minutes after a gap are not stepped until the gap is closed.
    [Test]
    public async Task Pair_with_a_gap_steps_the_paper_trade_only_to_the_end_of_its_coverage()
    {
        _paper.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([Opened(Auto("Пробои 5м", IdeaAutoMode.Paper, amount: 50m))]);
        IReadOnlyList<IdeaCandle> minutes = IdeaAutoFixtures.Minutes("paper-to-target");
        _market.Loaded(Wif, Fired, Fired.AddMinutes(2), minutes.Take(2).Select(Candle));
        _market.Closed(Wif, Candle(minutes[3]));

        AutoMinute minute = (await RunAsync([], End.AddMinutes(3)))!;

        PaperIdeaTrade stepped = minute.Stepped.ShouldHaveSingleItem();
        (stepped.Trade.Status, stepped.SteppedUntil).ShouldBe((IdeaTradeStatus.Open, Fired.AddMinutes(2)));
    }

    // Scenario "Стоп, пока сканер лежал": the minutes the memory no longer holds come from storage first.
    [Test]
    public async Task Minutes_older_than_the_memory_are_read_from_storage_first()
    {
        _paper.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([Opened(Auto("Пробои 5м", IdeaAutoMode.Paper, amount: 50m))]);
        IReadOnlyList<IdeaCandle> minutes = IdeaAutoFixtures.Minutes("paper-to-target");
        _market.Keep([new ScannerPair(Wif, true, CoveredUntil: Fired.AddMinutes(4), TickSize: 0.01m, QuantityStep: 0.01m, MinQuantity: 0.01m, MinAmount: 5m)]);
        _market.Loaded(Wif, Fired.AddMinutes(2), Fired.AddMinutes(4), minutes.Skip(2).Select(Candle));
        _candles.GetAsync(Wif, Interval.OneMinute, Fired.AddMinutes(1), Fired.AddMinutes(2), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([Candle(minutes[1])]);

        AutoMinute minute = (await RunAsync([], End.AddMinutes(3)))!;

        minute.Stepped.ShouldHaveSingleItem().Trade.Status.ShouldBe(IdeaTradeStatus.ClosedAtTarget);
    }

    // A pair whose memory holds no minute at all (it was just listed again): the whole stretch comes from storage.
    [Test]
    public async Task Pair_whose_memory_holds_no_minutes_is_stepped_by_the_stored_ones()
    {
        _paper.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([Opened(Auto("Пробои 5м", IdeaAutoMode.Paper, amount: 50m))]);
        _market.Keep([new ScannerPair(Wif, true, CoveredUntil: Fired.AddMinutes(4), TickSize: 0.01m, QuantityStep: 0.01m, MinQuantity: 0.01m, MinAmount: 5m)]);
        _candles.GetAsync(Wif, Interval.OneMinute, End, Fired.AddMinutes(4), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([.. IdeaAutoFixtures.Minutes("paper-to-target").Skip(1).Select(Candle)]);

        AutoMinute minute = (await RunAsync([], End.AddMinutes(3)))!;

        minute.Stepped.ShouldHaveSingleItem().Trade.Status.ShouldBe(IdeaTradeStatus.ClosedAtTarget);
    }

    [Test]
    public async Task Pair_that_is_not_covered_yet_is_not_stepped()
    {
        _paper.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([Opened(Auto("Пробои 5м", IdeaAutoMode.Paper, amount: 50m))]);
        _market.Keep([new ScannerPair(Wif, true)]);

        (await RunAsync([], End.AddMinutes(3))).ShouldBeNull();
    }

    // Decision 12: the refusals are kept 7 days.
    [Test]
    public async Task Refusals_older_than_seven_days_are_deleted()
    {
        await _live.RemoveOldRefusalsAsync(CancellationToken.None);

        await _refusals.Received(1).DeleteBeforeAsync(End.AddDays(-7), Arg.Any<CancellationToken>());
    }

    // Decision 6: a request nobody took within a minute of its writing becomes the refusal «Не успел войти».
    [Test]
    public async Task Requests_older_than_a_minute_are_turned_into_refusals()
    {
        await _live.ExpireEntriesAsync(CancellationToken.None);

        await _entries.Received(1).ExpireAsync(End.AddSeconds(-60), End, Arg.Any<CancellationToken>());
    }

    private IdeaAuto Enable(IdeaAuto auto)
    {
        _autos.GetEnabledAsync(Arg.Any<CancellationToken>()).Returns([auto]);

        return auto;
    }

    private static IdeaAuto Auto(string name, IdeaAutoMode mode, int position = 0, decimal amount = 50m)
    {
        return IdeaAutoGenerator.Generate(Account, name, position, mode, amount, Conditions());
    }

    private static IdeaAutoConditions Conditions()
    {
        return new IdeaAutoConditions([IdeaSetup.Impulse], [IdeaType.TwoR], [Interval.FifteenMinutes, Interval.OneHour], null, null, null, 1.2m, 0);
    }

    // An idea of a long born at 13:40: entry 100, stop 98 and target 104.
    private static Idea Born(IdeaSetup setup = IdeaSetup.Impulse, Interval interval = Interval.FifteenMinutes)
    {
        Idea generated = IdeaGenerator.Generate(Fired, setup: setup, interval: interval);

        return generated with { Track = generated.Track with { Plan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m) } };
    }

    // The paper trade an auto opened in the minute of 13:40, to be read from storage.
    private static PaperIdeaTrade Opened(IdeaAuto auto)
    {
        Idea idea = Born();
        var birth = new IdeaCandle(Fired, 99.9m, 100.2m, 99.8m, 100m, 1m);
        IdeaTradeSize size = ProfitFox.Domain.Services.IdeaTrades.IdeaTradeSizing.Calculate(idea.Track.Plan, new FeeRates(0.02m, 0.055m), auto.Amount, new InstrumentRules(0.01m, 0.01m, 0.01m, 5m));

        return PaperFill.Open(Guid.NewGuid(), idea, auto.Id, auto.AccountId, size, null, birth, new FeeRates(0.02m, 0.055m));
    }

    private Task<AutoMinute?> RunAsync(IReadOnlyList<Idea> born, DateTimeOffset? end = null)
    {
        var births = new Dictionary<Instrument, IdeaCandle> { [Wif] = new IdeaCandle(Fired, 99.9m, 100.2m, 99.8m, 100m, 1m) };

        return _live.RunAsync(end ?? End, born, births, CancellationToken.None);
    }

    private async Task RunAndCommitAsync(IReadOnlyList<Idea> born)
    {
        _live.Commit(await RunAsync(born), born);
    }

    private void Load(string fixture, int through)
    {
        _market.Loaded(Wif, Fired, Fired.AddMinutes(through), IdeaAutoFixtures.Minutes(fixture).Take(through).Select(Candle));
    }

    private static Candle Candle(IdeaCandle minute)
    {
        return new Candle(minute.OpenTime, new Price(minute.Open), new Price(minute.High), new Price(minute.Low), new Price(minute.Close), new Volume(1m), new Volume(minute.Volume));
    }
}