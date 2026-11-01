using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Backtests;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>
///     Requirement "Версия движка": the results of the reference strategies on the hourly candle fixture are fingerprinted, and every
///     fingerprint belongs to one engine version. A change of the results without a new engine version fails here.
/// </summary>
public class EngineVersionTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-01T00:00:00Z");

    // The instrument and fees of the reference backtests: linear BTCUSDT at the base rates of Bybit.
    private static readonly TradingConditions Linear = Conditions.Reference with { Fees = new FeeSchedule(0.0002m, 0.00055m, MarketCategory.Linear) };

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

    /// <summary>SHA-256 of the details of every reference strategy, one JSON line each in a fixed order.</summary>
    private static string Fingerprint()
    {
        IReadOnlyList<Candle> candles = [.. CandleFixtures.Hourly().Select(Candle)];
        var two = new Dictionary<string, NodeValue> { ["fast"] = Values.Number(2m), ["slow"] = Values.Number(3m) };
        var defaults = new Dictionary<string, NodeValue>();
        IEnumerable<string> lines =
        [
            Details("ema", two, candles),
            Details("ema-short", two, candles),
            Details("dca", defaults, candles),
            Details("ladder", defaults, candles)
        ];

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }

    private static string Details(string strategy, IReadOnlyDictionary<string, NodeValue> values, IReadOnlyList<Candle> candles)
    {
        var snapshot = StrategySnapshot.Of(StrategyJson.ParseStrategy(StrategyFixtures.Element(strategy)), BuiltInComposites.Catalog);
        var details = new DetailsObserver(Linear.Balance);
        new BacktestReplay(snapshot, values, Linear, 0m, LimitFillMode.Breakout, Interval.OneHour).Run(Start, candles, details);

        return JsonSerializer.Serialize(details.Details());
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