using System.Globalization;

using ProfitFox.Application.Ideas;
using ProfitFox.Application.Paging;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Ideas;

/// <summary>Requirement "Сортировка списка идей": the order of the ideas of «Идеи».</summary>
public class IdeaOrderTests
{
    private static readonly DateTimeOffset Noon = DateTimeOffset.Parse("2024-03-10T12:00:00Z", CultureInfo.InvariantCulture);

    // Each row: the column and three ideas a, b and c as they differ in it; the order of the column ascending follows.
    private static IEnumerable<TestCaseData> Columns()
    {
        yield return Case(IdeaSortColumn.FiredAt, [View("a", FiredAt(Noon.AddMinutes(-15))), View("b", FiredAt(Noon.AddDays(-1))), View("c", FiredAt(Noon.AddHours(-4)))], "bca");
        yield return Case(IdeaSortColumn.Symbol, [View("a", Symbol("WIFUSDT")), View("b", Symbol("ARMUSDT")), View("c", Symbol("SOLUSDT"))], "bca");
        yield return Case(IdeaSortColumn.Setup, [View("a", Setup(IdeaSetup.EarlyDrop)), View("b", Setup(IdeaSetup.Impulse)), View("c", Setup(IdeaSetup.Drop))], "bca");
        yield return Case(IdeaSortColumn.Interval, [View("a", OnInterval(Interval.FourHours)), View("b", OnInterval(Interval.FiveMinutes)), View("c", OnInterval(Interval.OneHour))], "bca");
        yield return Case(IdeaSortColumn.Type, [View("a", Type(IdeaType.Pullback)), View("b", Type(IdeaType.Levels)), View("c", Type(IdeaType.BeyondExtreme))], "bca");
        yield return Case(IdeaSortColumn.Entry, [View("a", Plan(entry: 184.7m)), View("b", Plan(entry: 9.5m)), View("c", Plan(entry: 197m))], "bac");
        yield return Case(IdeaSortColumn.Stop, [View("a", Plan(stop: -2m)), View("b", Plan(stop: 0.5m)), View("c", Plan(stop: -10m))], "cab");
        yield return Case(IdeaSortColumn.Target, [View("a", Plan(target: 2.6m)), View("b", Plan(target: 2.4m)), View("c", Plan(target: 2.55m))], "bca");
        yield return Case(
            IdeaSortColumn.RiskReward,
            [
                View("a", Plan(entry: 100m, stop: 95m, target: 110m), shares: new IdeaShares(0.0156m, 0.01m, 0)),
                View("b", Plan(entry: 100m, stop: 94m, target: 109.3m), shares: new IdeaShares(0.0197m, 0.01m, 0)),
                View("c", Plan(entry: 100m, stop: 99m, target: 110m), shares: new IdeaShares(0.012m, 0.01m, 0))
            ],
            "cab");
        yield return Case(
            IdeaSortColumn.Probability,
            [View("a", bucket: IdeaViewGenerator.Bucket(0m, 0.46m)), View("b", bucket: IdeaViewGenerator.Bucket(0m, 0.5m)), View("c", bucket: IdeaViewGenerator.Bucket(0m, 0.405m))],
            "cab");
        yield return Case(
            IdeaSortColumn.Expectancy,
            [View("a", bucket: IdeaViewGenerator.Bucket(0.38m)), View("b", bucket: IdeaViewGenerator.Bucket(-0.05m)), View("c", bucket: IdeaViewGenerator.Bucket(1.2m))],
            "bac");
        yield return Case(
            IdeaSortColumn.Profit,
            [View("a", shares: new IdeaShares(0.04m, 0, 0)), View("b", shares: new IdeaShares(0.0385m, 0, 0)), View("c", shares: new IdeaShares(0.1m, 0, 0))],
            "bac");
        yield return Case(IdeaSortColumn.Status, [View("a", Status(IdeaStatus.Stop)), View("b", Status(IdeaStatus.AwaitingEntry)), View("c", Status(IdeaStatus.Active))], "bca");
    }

    [TestCaseSource(nameof(Columns))]
    public void A_column_goes_by_its_value_in_both_directions(IdeaSortColumn column, IdeaView[] views, string ascending)
    {
        Names(IdeaOrder.Sort(views, column, OrderDirection.Ascending)).ShouldBe(ascending);
        Names(IdeaOrder.Sort(views, column, OrderDirection.Descending)).ShouldBe(new string([.. ascending.Reverse()]));
    }

    // The new order of the requirement: the order of the lists on the screen, not of the Russian names.
    [Test]
    public void Setup_type_and_side_go_in_the_order_of_their_lists()
    {
        IdeaView[] setups = [View("a", Setup(IdeaSetup.HighVolatility)), View("b", Setup(IdeaSetup.Drop)), View("c", Setup(IdeaSetup.EarlyImpulse)), View("d", Setup(IdeaSetup.Impulse))];
        IdeaView[] types = [View("a", Type(IdeaType.Pullback)), View("b", Type(IdeaType.TwoR)), View("c", Type(IdeaType.Levels))];
        IdeaView[] sides = [View("a", Side(IdeaSide.Short)), View("b", Side(IdeaSide.Long))];

        Names(IdeaOrder.Sort(setups, IdeaSortColumn.Setup, OrderDirection.Ascending)).ShouldBe("dcba");
        Names(IdeaOrder.Sort(types, IdeaSortColumn.Type, OrderDirection.Ascending)).ShouldBe("cba");
        Names(IdeaOrder.Sort(sides, IdeaSortColumn.Side, OrderDirection.Ascending)).ShouldBe("ba");
    }

    // Requirement "Сортировка списка идей": the candle setups go after «Высокая волатильность» in the order of the list on the screen.
    [Test]
    public void Candle_setups_go_after_the_setups_of_a_move_in_the_order_of_the_list()
    {
        IdeaSetup[] order =
        [
            IdeaSetup.Impulse, IdeaSetup.EarlyImpulse, IdeaSetup.Drop, IdeaSetup.EarlyDrop, IdeaSetup.HighVolatility, IdeaSetup.Breakout, IdeaSetup.FalseBreakout,
            IdeaSetup.Squeeze, IdeaSetup.Exhaustion, IdeaSetup.RsiExtreme, IdeaSetup.NewExtreme, IdeaSetup.QuietVolume,
            IdeaSetup.Accumulation
        ];
        IdeaView[] views = [.. order.Reverse().Select((setup, index) => View(((char)('a' + index)).ToString(), Setup(setup)))];

        Names(IdeaOrder.Sort(views, IdeaSortColumn.Setup, OrderDirection.Ascending)).ShouldBe("mlkjihgfedcba");
    }

    // Scenario "Сортировка по типу": «Стоп-вход» after «Откат».
    [Test]
    public void Stop_entry_goes_after_the_pullback()
    {
        IdeaView[] views = [View("a", Type(IdeaType.StopEntry)), View("b", Type(IdeaType.Pullback)), View("c", Type(IdeaType.Levels))];

        Names(IdeaOrder.Sort(views, IdeaSortColumn.Type, OrderDirection.Ascending)).ShouldBe("cba");
    }

    // Scenario "Сортировка по сетапу": «Накопление / распределение» after «Объём без движения».
    [Test]
    public void Accumulation_goes_after_the_quiet_volume()
    {
        IdeaView[] views = [View("a", Setup(IdeaSetup.Accumulation)), View("b", Setup(IdeaSetup.QuietVolume)), View("c", Setup(IdeaSetup.NewExtreme))];

        Names(IdeaOrder.Sort(views, IdeaSortColumn.Setup, OrderDirection.Ascending)).ShouldBe("cba");
    }

    [Test]
    public void Symbols_are_compared_ordinally()
    {
        IdeaView[] views = [View("a", Symbol("B_B")), View("b", Symbol("BAB")), View("c", Symbol("B1B"))];

        Names(IdeaOrder.Sort(views, IdeaSortColumn.Symbol, OrderDirection.Ascending)).ShouldBe("cba");
    }

    // Scenario "Вероятность без данных".
    [TestCase(OrderDirection.Ascending, "bc")]
    [TestCase(OrderDirection.Descending, "cb")]
    public void Ideas_without_a_probability_stay_under_the_others(OrderDirection direction, string expectedHead)
    {
        IdeaView[] views =
        [
            View("f", bucket: IdeaViewGenerator.Bucket(0m, 0.9m, cases: 29)),
            View("b", bucket: IdeaViewGenerator.Bucket(0m, 0.46m)),
            View("n"),
            View("c", bucket: IdeaViewGenerator.Bucket(0m, 0.51m))
        ];

        string sorted = Names(IdeaOrder.Sort(views, IdeaSortColumn.Probability, direction));

        sorted[..2].ShouldBe(expectedHead);
        sorted[2..].ShouldBe("fn");
    }

    [TestCase(OrderDirection.Ascending)]
    [TestCase(OrderDirection.Descending)]
    public void Ideas_without_a_bucket_stay_under_the_others_by_the_expectancy(OrderDirection direction)
    {
        IdeaView[] views = [View("n"), View("l", bucket: IdeaViewGenerator.Bucket(-0.2m)), View("h", bucket: IdeaViewGenerator.Bucket(0.4m))];

        Names(IdeaOrder.Sort(views, IdeaSortColumn.Expectancy, direction)).ShouldEndWith("n");
    }

    // Equal values: the expectancy descending, then the newest first, then the id; a bucket without cases after the others.
    [TestCase(OrderDirection.Ascending)]
    [TestCase(OrderDirection.Descending)]
    public void Equal_values_go_by_expectancy_then_newest_first_then_id(OrderDirection direction)
    {
        IdeaView[] views =
        [
            View("a", FiredAt(Noon.AddHours(-1)), bucket: IdeaViewGenerator.Bucket(0.1m)),
            View("b", FiredAt(Noon.AddHours(-1)), bucket: IdeaViewGenerator.Bucket(0.1m)),
            View("c", FiredAt(Noon.AddHours(-2)), bucket: IdeaViewGenerator.Bucket(0.1m)),
            View("d", FiredAt(Noon.AddHours(-3)), bucket: IdeaViewGenerator.Bucket(0.3m)),
            View("e", FiredAt(Noon.AddHours(-3)))
        ];

        // The R:R after the fees is the same for all five.
        Names(IdeaOrder.Sort(views, IdeaSortColumn.RiskReward, direction)).ShouldBe("dabce");
    }

    [Test]
    public void Without_a_column_the_ideas_go_by_the_expectancy_highest_first()
    {
        IdeaView[] views = [View("n"), View("l", bucket: IdeaViewGenerator.Bucket(-0.2m)), View("h", bucket: IdeaViewGenerator.Bucket(0.4m))];

        Names(IdeaOrder.ByExpectancy(views)).ShouldBe("hln");
    }

    [TestCase("firedAt", IdeaSortColumn.FiredAt)]
    [TestCase("symbol", IdeaSortColumn.Symbol)]
    [TestCase("setup", IdeaSortColumn.Setup)]
    [TestCase("interval", IdeaSortColumn.Interval)]
    [TestCase("type", IdeaSortColumn.Type)]
    [TestCase("side", IdeaSortColumn.Side)]
    [TestCase("entry", IdeaSortColumn.Entry)]
    [TestCase("stop", IdeaSortColumn.Stop)]
    [TestCase("target", IdeaSortColumn.Target)]
    [TestCase("riskReward", IdeaSortColumn.RiskReward)]
    [TestCase("probability", IdeaSortColumn.Probability)]
    [TestCase("expectancy", IdeaSortColumn.Expectancy)]
    [TestCase("profit", IdeaSortColumn.Profit)]
    [TestCase("status", IdeaSortColumn.Status)]
    public void A_column_is_read_by_its_code(string code, IdeaSortColumn expected)
    {
        IdeaOrder.TryParseColumn(code, out IdeaSortColumn column).ShouldBeTrue();
        column.ShouldBe(expected);
    }

    [Test]
    public void An_unknown_column_is_not_read()
    {
        IdeaOrder.TryParseColumn("move", out _).ShouldBeFalse();
    }

    private static TestCaseData Case(IdeaSortColumn column, IdeaView[] views, string ascending)
    {
        return new TestCaseData(column, views, ascending).SetName($"A {column} column goes by its value");
    }

    // Three ideas that differ only in what the test changes; the name is the first byte of the id.
    private static IdeaView View(string name, Func<Idea, Idea>? change = null, BucketStatistics? bucket = null, IdeaShares? shares = null)
    {
        Idea idea = IdeaGenerator.Generate(Noon.AddMinutes(-30));

        return IdeaViewGenerator.Generate((change ?? (same => same))(idea) with { Id = new Guid(name[0], 0, 0, new byte[8]) }, bucket, shares);
    }

    private static Func<Idea, Idea> FiredAt(DateTimeOffset firedAt)
    {
        return idea => idea with { Track = idea.Track with { FiredAt = firedAt } };
    }

    private static Func<Idea, Idea> Symbol(string symbol)
    {
        return idea => idea with { Instrument = new Instrument(MarketCategory.Linear, symbol) };
    }

    private static Func<Idea, Idea> Setup(IdeaSetup setup)
    {
        return idea => idea with { Setup = setup };
    }

    private static Func<Idea, Idea> OnInterval(Interval interval)
    {
        return idea => idea with { Interval = interval };
    }

    private static Func<Idea, Idea> Type(IdeaType type)
    {
        return idea => idea with { Track = idea.Track with { Plan = idea.Track.Plan with { Type = type } } };
    }

    private static Func<Idea, Idea> Side(IdeaSide side)
    {
        return idea => idea with { Track = idea.Track with { Plan = idea.Track.Plan with { Side = side } } };
    }

    private static Func<Idea, Idea> Status(IdeaStatus status)
    {
        return idea => idea with { Track = idea.Track with { Status = status } };
    }

    private static Func<Idea, Idea> Plan(decimal? entry = null, decimal? stop = null, decimal? target = null)
    {
        return idea => idea with { Track = idea.Track with { Plan = idea.Track.Plan with { Entry = entry ?? idea.Track.Plan.Entry, Stop = stop ?? idea.Track.Plan.Stop, Target = target ?? idea.Track.Plan.Target } } };
    }

    private static string Names(IEnumerable<IdeaView> views)
    {
        return new string([.. views.Select(view => (char)view.Idea.Id.ToByteArray()[0])]);
    }
}