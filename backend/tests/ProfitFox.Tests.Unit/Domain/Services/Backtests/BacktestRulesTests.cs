using System.Text.Json;

using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Tests.Unit.Domain.Services.Graph;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>
///     Change add-strategy-backtest, task 3.3: spec strategy-backtest «Настройки прогона», «История прогона», «Пары прогона»
///     and «Наборы значений» — the settings of a backtest and every error of a request at once.
/// </summary>
public sealed class BacktestRulesTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 22, 15, 30, 0, TimeSpan.Zero);
    private static readonly string[] Listed = ["BTCUSDT", "ETHUSDT", "SOLUSDT"];

    private static readonly GraphDefinition FundingShort = Read(Graphs.ChangedStrategy(graph =>
    {
        graph["parameters"]![0]!["minimum"] = 0;
        graph["parameters"]![0]!["maximum"] = 0.01;
    }));

    private static readonly GraphDefinition Ladder = Read(Graphs.Ladder);

    [Test]
    public void Without_settings_a_category_of_ideas_reads_every_pair_from_the_start_of_the_data_to_today_on_4h()
    {
        BacktestSettings settings = Settings(new BacktestRequest(), FundingShort, StrategyKind.Idea);

        Assert.That(settings.End, Is.EqualTo(new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero)), "the end by default is the start of the current day");
        Assert.That(settings.Start, Is.EqualTo(HistoryPeriod.DataStart));
        Assert.That(settings.Pairs, Is.Null, "every listed pair");
        Assert.That(settings.Timeframes, Is.EqualTo(new[] { TimeSpan.FromHours(4) }));
        Assert.That(settings.Values, Is.Empty);
        Assert.That((settings.TakerFee, settings.MakerFee), Is.EqualTo((0.00055, 0.0002)));
    }

    [Test]
    public void The_settings_keep_what_was_asked()
    {
        var request = new BacktestRequest(
            new Dictionary<string, IReadOnlyList<string>> { ["threshold"] = ["0.001"] },
            ["4h"],
            "2025-01-01",
            "2026-06-01",
            ["BTCUSDT", "ETHUSDT"],
            TakerFee: 0.0006,
            MakerFee: 0.0001);

        BacktestSettings settings = Settings(request, FundingShort, StrategyKind.Idea);

        Assert.That(settings.Values.Single(), Is.EqualTo(new ParameterValues("threshold", ["0.001"])).Using<ParameterValues>(Same));
        Assert.That((settings.Start, settings.End), Is.EqualTo((Utc(2025, 1, 1), Utc(2026, 6, 1))));
        Assert.That(settings.Pairs, Is.EqualTo(new[] { "BTCUSDT", "ETHUSDT" }));
        Assert.That((settings.Costs.TakerFee, settings.Costs.MakerFee, settings.Costs.Slippage), Is.EqualTo((0.0006, 0.0001, 0.0005)));
    }

    [Test]
    public void Two_errors_at_once()
    {
        BacktestCheck check = Check(new BacktestRequest(Timeframes: ["2m"], Pairs: ["FOOUSDT"]), FundingShort, StrategyKind.Idea);

        Assert.That(check.Settings, Is.Null);
        Assert.That(
            check.Errors.Select(error => (error.Code, error.Field, error.Pair)),
            Is.EqualTo(new[] { (BacktestError.InvalidTimeframe, (string?)"timeframes", (string?)null), (BacktestError.UnknownInstrument, "pairs", "FOOUSDT") }));
    }

    /// <summary>
    ///     Change add-ideas-and-market, task 2.1: spec strategy-backtest «Прогон на 15m».
    /// </summary>
    [Test]
    public void A_backtest_on_15m_and_1h_has_both_timeframes()
    {
        BacktestCheck check = Check(new BacktestRequest(Timeframes: ["15m", "1h"]), FundingShort, StrategyKind.Idea);

        Assert.That(check.Errors, Is.Empty);
        Assert.That(check.Settings!.Timeframes, Is.EqualTo(new[] { TimeSpan.FromMinutes(15), TimeSpan.FromHours(1) }));
    }

    [Test]
    public void An_unknown_timeframe_names_the_five()
    {
        BacktestError error = Check(new BacktestRequest(Timeframes: ["2m"]), FundingShort, StrategyKind.Idea).Errors.Single();

        Assert.That(error.Reason, Does.Contain("2m").And.Contain("5m, 15m, 1h, 4h, 1d"));
    }

    [Test]
    public void An_end_in_the_future_is_refused()
    {
        BacktestError error = Check(new BacktestRequest(End: "2026-12-01"), FundingShort, StrategyKind.Idea).Errors.Single();

        Assert.That((error.Code, error.Field), Is.EqualTo((BacktestError.EndAfterToday, (string?)"end")));
    }

    [Test]
    public void The_end_may_be_today()
    {
        Assert.That(Settings(new BacktestRequest(End: "2026-11-22"), FundingShort, StrategyKind.Idea).End, Is.EqualTo(Utc(2026, 11, 22)));
    }

    [Test]
    public void A_ladder_without_a_start_reads_26_weeks()
    {
        BacktestSettings settings = Settings(new BacktestRequest(End: "2026-11-22", Pairs: ["BTCUSDT"]), Ladder, StrategyKind.Bot);

        Assert.That(settings.Start, Is.EqualTo(Utc(2026, 5, 24)));
    }

    [TestCase("2026-06-01T05:00:00Z")]
    [TestCase("2026-06-01T00:00:00+03:00")]
    [TestCase("1 June")]
    public void A_date_off_the_start_of_a_day_is_refused(string end)
    {
        BacktestError error = Check(new BacktestRequest(End: end), FundingShort, StrategyKind.Idea).Errors.Single();

        Assert.That((error.Code, error.Field), Is.EqualTo((BacktestError.InvalidDate, (string?)"end")));
    }

    [Test]
    public void An_end_not_after_the_start_is_refused()
    {
        BacktestError error = Check(new BacktestRequest(Start: "2026-06-01", End: "2026-06-01"), FundingShort, StrategyKind.Idea).Errors.Single();

        Assert.That(error.Code, Is.EqualTo(BacktestError.EmptyHistory));
    }

    [Test]
    public void A_ladder_without_pairs_is_refused()
    {
        BacktestError error = Check(new BacktestRequest(), Ladder, StrategyKind.Bot).Errors.Single();

        Assert.That((error.Code, error.Field), Is.EqualTo((BacktestError.PairsRequired, (string?)"pairs")));
    }

    [Test]
    public void A_ladder_takes_at_most_20_pairs()
    {
        string[] pairs = [.. Enumerable.Range(1, 21).Select(number => $"P{number}USDT")];

        BacktestCheck check = Check(new BacktestRequest(Pairs: pairs), Ladder, StrategyKind.Bot, listed: pairs);

        Assert.That(check.Errors.Single().Code, Is.EqualTo(BacktestError.TooManyPairs));
        Assert.That(Check(new BacktestRequest(Pairs: pairs[..20]), Ladder, StrategyKind.Bot, listed: pairs).Errors, Is.Empty);
    }

    [Test]
    public void An_unknown_pair_is_refused_on_that_pair()
    {
        BacktestError error = Check(new BacktestRequest(Pairs: ["BTCUSDT", "FOOUSDT"]), FundingShort, StrategyKind.Idea).Errors.Single();

        Assert.That((error.Code, error.Pair), Is.EqualTo((BacktestError.UnknownInstrument, (string?)"FOOUSDT")));
    }

    [Test]
    public void An_unknown_parameter_and_a_value_of_another_type_are_refused()
    {
        var request = new BacktestRequest(new Dictionary<string, IReadOnlyList<string>> { ["threshold"] = ["0.001", "4h", "0.02"], ["thresold"] = ["1"] });

        BacktestCheck check = Check(request, FundingShort, StrategyKind.Idea);

        Assert.That(
            check.Errors.Select(error => (error.Code, error.Parameter)),
            Is.EqualTo(new[] { ("UnknownParameter", (string?)"thresold"), ("ParameterTypeMismatch", "threshold"), ("ParameterOutOfBounds", "threshold") }));
        Assert.That(check.Errors[1].Reason, Does.StartWith("threshold = 4h"));
    }

    [Test]
    public void More_than_200_sets_are_refused()
    {
        string[] twenty = [.. Enumerable.Range(1, 20).Select(value => (value / 10000m).ToString(System.Globalization.CultureInfo.InvariantCulture))];
        var request = new BacktestRequest(
            new Dictionary<string, IReadOnlyList<string>> { ["priceRiseForSell"] = twenty, ["priceDropForBuy"] = [.. twenty[..4]] },
            ["1h", "4h", "1d"],
            Pairs: ["BTCUSDT"]);

        BacktestCheck check = Check(request, Ladder, StrategyKind.Bot);

        Assert.That(check.Errors.Single().Code, Is.EqualTo(BacktestError.TooManySets), "20 × 4 × 3 = 240 sets");
        Assert.That(Check(request with { Timeframes = ["1h", "4h"] }, Ladder, StrategyKind.Bot).Errors, Is.Empty, "160 sets");
    }

    [Test]
    public void A_fee_outside_of_a_fraction_is_refused()
    {
        BacktestCheck check = Check(new BacktestRequest(TakerFee: 1, MakerFee: -0.1), FundingShort, StrategyKind.Idea);

        Assert.That(check.Errors.Select(error => (error.Code, error.Field)), Is.EqualTo(new[] { (BacktestError.InvalidFee, (string?)"takerFee"), (BacktestError.InvalidFee, "makerFee") }));
    }

    private static BacktestSettings Settings(BacktestRequest request, GraphDefinition graph, StrategyKind kind)
    {
        BacktestCheck check = Check(request, graph, kind);
        Assert.That(check.Errors, Is.Empty);
        return check.Settings!;
    }

    private static BacktestCheck Check(BacktestRequest request, GraphDefinition graph, StrategyKind kind, IReadOnlyCollection<string>? listed = null)
    {
        return BacktestRules.Check(request, graph, kind, listed ?? Listed, Now);
    }

    private static GraphDefinition Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return StrategyGraph.ReadForm(document.RootElement).Value!;
    }

    private static DateTimeOffset Utc(int year, int month, int day)
    {
        return new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
    }

    private static bool Same(ParameterValues a, ParameterValues b)
    {
        return a.Name == b.Name && a.Values.SequenceEqual(b.Values);
    }
}
