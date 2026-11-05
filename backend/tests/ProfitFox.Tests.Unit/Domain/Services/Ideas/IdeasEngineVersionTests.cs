using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Fixtures.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Decision 6: the cases the engine gives on the reference sets are fingerprinted, and every fingerprint belongs to one version of the
///     rules. A change of the cases without a new version fails here.
/// </summary>
public class IdeasEngineVersionTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-02-29T23:30:00Z");
    private static readonly DateTimeOffset Three = DateTimeOffset.Parse("2024-03-01T03:00:00Z");
    private static readonly FeeRates Fees = new FeeRates(0.02m, 0.055m);

    // Task 5.3.
    [Test]
    public void Reference_cases_have_the_fingerprint_of_the_current_version()
    {
        (int Version, string Fingerprint) latest = IdeasEngineVersionFixtures.History()[^1];

        latest.Version.ShouldBe(IdeasEngineVersion.Current, "The last entry of Fixtures/Ideas/engine-versions.json must be the current version of the rules.");
        Fingerprint().ShouldBe(
            latest.Fingerprint,
            "The reference cases changed: raise IdeasEngineVersion.Current and add an entry with the new fingerprint to Fixtures/Ideas/engine-versions.json.");
    }

    [Test]
    public void Each_version_has_cases_of_its_own()
    {
        IReadOnlyList<(int Version, string Fingerprint)> history = IdeasEngineVersionFixtures.History();

        history.Select(entry => entry.Version).ShouldBe(history.Select(entry => entry.Version).Order().Distinct());
        history.Select(entry => entry.Fingerprint).Distinct().Count().ShouldBe(history.Count);
    }

    /// <summary>
    ///     SHA-256 of every case, one JSON line each in a fixed order, of the four setup sets on linear and spot with the default rules and with
    ///     a stop buffer of 1, with a spread and a percentile of volatility.
    /// </summary>
    private static string Fingerprint()
    {
        string[] sets = ["impulse", "early", "drop", "early-drop"];
        IdeaRuleSet[] rules = [new IdeasSettings().Rules(), new IdeasSettings { StopBuffer = 1m }.Rules()];
        IEnumerable<string> lines =
            from set in sets
            from category in new[] { MarketCategory.Linear, MarketCategory.Spot }
            from ruleSet in rules
            from line in Cases(IdeaFixtures.Setup(set), new Instrument(category, "WIFUSDT"), ruleSet)
            select $"{set} {category} {line}";

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }

    private static IEnumerable<string> Cases(IReadOnlyList<IdeaCandle> minutes, Instrument instrument, IdeaRuleSet rules)
    {
        var engine = new IdeaEngine(rules, IdeasEngineVersion.Current);
        var closed = rules.Thresholds.Keys.ToDictionary(
            interval => interval,
            interval => IdeaCandles.FromMinutes(interval, minutes, Start, interval.CandleOpen(Three)) as IReadOnlyList<IdeaCandle>);
        engine.Warm(instrument, closed, [.. minutes.Where(minute => minute.OpenTime < Three)], [], IdeaInputs.None);
        var inputs = new IdeaInputs(0.01m, rules.Thresholds.Keys.ToDictionary(interval => interval, _ => 0.0186m), 0.01m);
        decimal close = minutes[^1].Close;
        IEnumerable<IdeaCandle> flat = Enumerable.Range(0, 1440).Select(index => new IdeaCandle(minutes[^1].OpenTime.AddMinutes(1 + index), close, close, close, close, 0m));

        foreach (IdeaCandle minute in minutes.Where(minute => minute.OpenTime >= Three).Concat(flat))
        {
            foreach (Idea idea in engine.Step(instrument, minute, inputs, true).Changed.Where(idea => !idea.IsOpen))
            {
                yield return JsonSerializer.Serialize(new { idea.Setup, idea.Interval, idea.Track, idea.Move, idea.VolumeRatio, Result = CaseResult.R(idea.Track, Fees) });
            }
        }
    }
}