using System.Globalization;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Tests.Integration.Application.Commands.Strategies;

/// <summary>
///     Change add-setup-strategies, task 4.8: spec setup-strategies «Стратегии сетапов собираются из каталога» — the twelve
///     strategies and the nineteen signals of <c>Fixtures/Setups</c> are built of the kinds of the catalog and pass the
///     checks of a category of ideas, and a backtest of <c>impulse</c> goes over its threshold <c>move</c>.
/// </summary>
[NonParallelizable]
public sealed class SetupCatalogTests
{
    private static readonly string[] Strategies =
    [
        "accumulation", "breakout", "drop", "early-drop", "early-impulse", "exhaustion", "false-breakout", "impulse", "new-extreme",
        "quiet-volume", "rsi-extreme", "squeeze"
    ];

    private static readonly string[] Signals =
    [
        "accumulation", "breakout-down", "breakout-up", "distribution", "drop", "early-drop", "early-impulse", "exhaustion-bottom",
        "exhaustion-top", "false-breakout-long", "false-breakout-short", "high-volatility", "impulse", "new-high", "new-low",
        "quiet-candle", "rsi-overbought-exit", "rsi-oversold-exit", "squeeze"
    ];

    [SetUp]
    public Task Reset()
    {
        return BenchDatabase.Reset();
    }

    [Test]
    public void The_files_are_the_twelve_strategies_and_the_nineteen_signals()
    {
        Assert.That(SetupFixtures.Strategies, Is.EqualTo(Strategies));
        Assert.That(SetupFixtures.Signals, Is.EqualTo(Signals));
    }

    [TestCaseSource(nameof(Signals))]
    public void Each_signal_is_a_signal_state_with_its_parameters(string name)
    {
        Signal signal = SetupFixtures.Signal(name);

        Assert.That(signal.IsState, Is.True, "a signal-state with a boolean output");
        Assert.That(signal.Graph.Outputs.Single().Type, Is.EqualTo(PinType.Boolean));
        Assert.That((string)SetupFixtures.Body($"{name}.signal.json")["name"]!, Is.EqualTo(name));
    }

    [TestCaseSource(nameof(Strategies))]
    public async Task Each_strategy_passes_the_checks_of_a_category_of_ideas(string name)
    {
        SavedSetup saved = await SetupFixtures.SaveAsync(BenchServices.Sender, BenchServices.Get<ISignalRepository>(), name);

        Assert.That((saved.Strategy.Name, saved.Strategy.Kind, saved.Strategy.Version), Is.EqualTo((name, StrategyKind.Idea, 1)));
        Assert.That(saved.Trading.ByPlan, Is.True);
        Assert.That(
            saved.Strategy.Graph.Parameters.Select(parameter => parameter.Name),
            Is.SupersetOf(new[] { "stopBuffer", "targetR", "lifeCandles", "size" }));
        Assert.That(saved.Strategy.Signals.Select(use => use.Name), Is.EquivalentTo(SetupFixtures.SignalsOf(SetupFixtures.Body($"{name}.strategy.json"))));
    }

    // «Порог — параметр»: three impulses of 2.7, 3.2 and 3.7 ATR in a week; the thresholds 2.5, 3.0 and 3.5 take three, two
    // and one of them.
    [Test]
    public async Task A_backtest_of_impulse_over_move_gives_three_sets_with_their_own_trades()
    {
        SavedSetup impulse = await SetupFixtures.SaveAsync(BenchServices.Sender, BenchServices.Get<ISignalRepository>(), "impulse");
        SetupTape tape = SetupTapes.AtrOfTwo().Moves(2400, 0.9m, 0.9m, 0.9m, 0.9m, 0.9m, 0.9m);
        tape.Steady(40, tape.Last.Close, 2).Moves(2400, 1, 1, 1, 1, 1, 1.4m);
        tape.Steady(40, tape.Last.Close, 2).Moves(2400, 1.2m, 1.2m, 1.2m, 1.2m, 1.2m, 1.4m);
        tape.Steady((7 * 24) - tape.Candles.Count, tape.Last.Close, 2);
        DateTimeOffset end = SetupTape.Start.AddDays(7);
        var settings = new BacktestSettings([new ParameterValues("move", ["2.5", "3.0", "3.5"])], [TimeSpan.FromHours(1)], SetupTape.Start, end, Pairs: null, 0.00055, 0.0002);

        IReadOnlyList<BacktestPlan> plans = BacktestPlans.Of(impulse.Expanded, StrategyKind.Idea, settings, BacktestGrid.Of(settings));
        IReadOnlyList<PairSetResult> results = await PairBacktest.RunAsync(
            "BTCUSDT",
            StrategyKind.Idea,
            plans,
            new Dictionary<TimeSpan, IReadOnlyList<HistoryCandle>> { [TimeSpan.FromHours(1)] = [.. tape.Candles.Select(History)] },
            new HistoryRead<HistoryPayment>([new TimeRange(SetupTape.Start, end.AddSeconds(1))], [.. Payments(end)]),
            (from, to, _) => Task.FromResult(Minutes(tape, from, to)),
            CancellationToken.None);

        Assert.That(plans, Has.Count.EqualTo(3));
        Assert.That(results.Select(result => (result.Numbers.Orders, result.Numbers.Trades)), Is.EqualTo(new[] { (3, 3), (2, 2), (1, 1) }));
    }

    private static HistoryCandle History(ClosedCandle candle)
    {
        return new HistoryCandle(candle.OpenTime, Text(candle.Open), Text(candle.High), Text(candle.Low), Text(candle.Close), Text(candle.Volume), Turnover: null, BuyVolume: null);
    }

    private static IEnumerable<HistoryPayment> Payments(DateTimeOffset end)
    {
        for (DateTimeOffset time = SetupTape.Start; time <= end; time = time.AddHours(8))
        {
            yield return new HistoryPayment(time, "0.0001");
        }
    }

    // Each minute of an hour at the close of its candle.
    private static HistoryRead<HistoryCandle> Minutes(SetupTape tape, DateTimeOffset from, DateTimeOffset to)
    {
        List<HistoryCandle> minutes = [];
        foreach (ClosedCandle candle in tape.Candles.Where(candle => candle.CloseTime > from && candle.OpenTime < to))
        {
            for (DateTimeOffset minute = candle.OpenTime; minute < candle.CloseTime; minute = minute.AddMinutes(1))
            {
                if (minute >= from && minute < to)
                {
                    string close = Text(candle.Close);
                    minutes.Add(new HistoryCandle(minute, close, close, close, close, "1", Turnover: null, BuyVolume: null));
                }
            }
        }

        return new HistoryRead<HistoryCandle>([new TimeRange(from, to)], minutes);
    }

    private static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
