using System.Text.Json;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entities.Backtests;

/// <summary>
///     A backtest to put in the queue: <c>funding-short</c> version 3, a category of ideas, the strategy of H0001 written
///     out, on 4h with the thresholds given, history from 2023-01-01 to 2026-06-01, every pair, unless the test gives what
///     it checks.
/// </summary>
public static class BacktestGenerator
{
    public const string FundingShort =
        """
        {
          "formatVersion": 1,
          "parameters": [{ "name": "threshold", "type": "number", "value": 0.0005 }],
          "nodes": [
            { "id": "p", "kind": "parameters" },
            { "id": "rate", "kind": "funding-rate" },
            { "id": "high", "kind": "compare", "settings": { "operation": "ge" } },
            { "id": "first", "kind": "first-time" },
            { "id": "enter", "kind": "enter", "settings": { "side": "short" }, "inputs": { "size": 100 } },
            { "id": "position", "kind": "position" },
            { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } }
          ],
          "wires": [
            { "from": "rate.value", "to": "high.a" },
            { "from": "p.threshold", "to": "high.b" },
            { "from": "high.result", "to": "first.value" },
            { "from": "first.event", "to": "enter.when" },
            { "from": "enter.order", "to": "position.orders" },
            { "from": "position.position", "to": "close-after.position" },
            { "from": "close-after.order", "to": "position.orders" }
          ]
        }
        """;

    public static Backtest Generate(
        string? id = null,
        Guid? strategy = null,
        string name = "funding-short",
        int version = 3,
        IReadOnlyList<string>? thresholds = null,
        IReadOnlyList<TimeSpan>? timeframes = null,
        BacktestSource? source = null)
    {
        using var document = JsonDocument.Parse(FundingShort);
        var settings = new BacktestSettings(
            thresholds is null ? [] : [new ParameterValues("threshold", thresholds)],
            timeframes ?? [TimeSpan.FromHours(4)],
            new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            Pairs: null,
            0.00055,
            0.0002);
        IReadOnlyList<BacktestSet> sets = BacktestGrid.Of(settings);
        return new Backtest
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            StrategyId = strategy ?? Guid.NewGuid(),
            StrategyName = name,
            StrategyVersion = version,
            Kind = StrategyKind.Idea,
            Graph = StrategyGraph.ReadForm(document.RootElement).Value!,
            Settings = settings,
            Sets = [.. sets.Select(set => new BacktestSetOutcome(set, Numbers: null))],
            SetCount = sets.Count,
            Source = source
        };
    }
}
