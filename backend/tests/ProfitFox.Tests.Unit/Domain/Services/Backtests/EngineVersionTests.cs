using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Backtests;
using ProfitFox.Tests.Shared.Fixtures.Strategies;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Backtests;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>
///     Requirement "Версия движка": the results of grids of the reference strategies on the hourly candle fixture are fingerprinted, and every
///     fingerprint belongs to one engine version. A change of the results without a new engine version fails here.
/// </summary>
public class EngineVersionTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-01T00:00:00Z");

    // Scenario "Изменение расчёта без новой версии".
    [Test]
    public void Reference_results_have_the_fingerprint_of_the_current_engine_version()
    {
        (int Version, string Fingerprint) latest = EngineVersionFixtures.History()[^1];

        latest.Version.ShouldBe(EngineVersion.Current, "The last entry of engine-versions.json must be the current engine version.");
        Fingerprint().ShouldBe(
            latest.Fingerprint,
            "The reference results changed: raise EngineVersion.Current and add an entry with the new fingerprint to engine-versions.json.");
    }

    [Test]
    public void Each_engine_version_has_results_of_its_own()
    {
        IReadOnlyList<(int Version, string Fingerprint)> history = EngineVersionFixtures.History();

        history.Select(entry => entry.Version).ShouldBe(history.Select(entry => entry.Version).Order().Distinct());
        history.Select(entry => entry.Fingerprint).Distinct().Count().ShouldBe(history.Count);
    }

    /// <summary>
    ///     SHA-256 of the details of every combination of a small grid of each reference strategy, one JSON line each in a fixed order,
    ///     through the code a Worker runs: the grid numbers the combinations and the backtest turns its percent settings into fractions.
    ///     Each grid runs with the base fees of Bybit, once filling limits on a breakout without slippage and once on a touch with it.
    /// </summary>
    private static string Fingerprint()
    {
        IReadOnlyList<Candle> candles = [.. CandleFixtures.Hourly().Select(Candle)];
        var emaGrid = new Dictionary<string, ParameterSpec>
        {
            ["fast"] = ParameterSpec.Between(Values.Number(2m), Values.Number(3m), Values.Number(1m)),
            ["slow"] = ParameterSpec.Between(Values.Number(3m), Values.Number(4m), Values.Number(1m))
        };
        var ladderGrid = new Dictionary<string, ParameterSpec>
        {
            ["priceDropForBuy"] = ParameterSpec.Between(Values.Number(0.01m), Values.Number(0.02m), Values.Number(0.01m))
        };
        (string Strategy, IReadOnlyDictionary<string, ParameterSpec> Grid)[] grids =
        [
            ("ema", emaGrid),
            ("ema-short", emaGrid),
            ("dca", new Dictionary<string, ParameterSpec>()),
            ("ladder", ladderGrid)
        ];
        SimulationSettings[] simulations =
        [
            new SimulationSettings(1000m, 0.02m, 0.055m, 0m, LimitFillMode.Breakout),
            new SimulationSettings(1000m, 0.02m, 0.055m, 0.05m, LimitFillMode.Touch)
        ];
        IEnumerable<string> lines =
            from grid in grids
            from simulation in simulations
            from line in Details(grid.Strategy, grid.Grid, simulation, candles)
            select line;

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }

    private static IEnumerable<string> Details(
        string strategy,
        IReadOnlyDictionary<string, ParameterSpec> parameters,
        SimulationSettings simulation,
        IReadOnlyList<Candle> candles)
    {
        var snapshot = StrategySnapshot.Of(StrategyJson.ParseStrategy(StrategyFixtures.Element(strategy)), BuiltInComposites.Catalog);
        Backtest backtest = BacktestGenerator.Generate(BacktestStatus.Running) with
        {
            Instrument = new Instrument(MarketCategory.Linear, "BTCUSDT"),
            Interval = Interval.OneHour,
            From = Start,
            To = Start.AddHours(candles.Count),
            Parameters = parameters,
            Simulation = simulation,
            Rules = Conditions.Reference.Rules
        };
        ParameterGrid grid = backtest.Grid();

        for (long combination = 0; combination < grid.Count!.Value; combination++)
        {
            IReadOnlyDictionary<string, NodeValue> values = grid.Combination(combination);
            var details = new DetailsObserver(simulation.Balance);
            backtest.Replay(snapshot, values).Run(Start, candles, details);

            yield return $"{strategy} {combination} {JsonSerializer.Serialize(details.Details())}";
        }
    }

    private static Candle Candle(string[] row)
    {
        return new Candle(
            DateTimeOffset.Parse(row[0], CultureInfo.InvariantCulture),
            new Price(decimal.Parse(row[1], CultureInfo.InvariantCulture)),
            new Price(decimal.Parse(row[2], CultureInfo.InvariantCulture)),
            new Price(decimal.Parse(row[3], CultureInfo.InvariantCulture)),
            new Price(decimal.Parse(row[4], CultureInfo.InvariantCulture)),
            new Volume(decimal.Parse(row[5], CultureInfo.InvariantCulture)),
            new Volume(decimal.Parse(row[6], CultureInfo.InvariantCulture)));
    }
}