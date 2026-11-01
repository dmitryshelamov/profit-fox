using AutoFixture;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Base;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.ValueObject.MarketData;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entity.Backtests;

public static class BacktestGenerator
{
    private static readonly Fixture Fixture = new Fixture();

    /// <summary>A backtest of twelve combinations with no progress, start or finish time; set those when the status needs them.</summary>
    public static Backtest Generate(BacktestStatus status)
    {
        DateTimeOffset from = DateTimeOffsetGenerator.Generate();

        return new Backtest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Fixture.Create<byte>() + 1,
            InstrumentGenerator.Generate(),
            IntervalGenerator.Generate(),
            from,
            from.AddDays(Fixture.Create<byte>() + 1),
            new Dictionary<string, ParameterSpec>(),
            new SimulationSettings(1000m, 0.1m, 0.1m, 0m, LimitFillMode.Breakout),
            new InstrumentRules(0.01m, 0.001m, 0.001m, 1m),
            SearchMode.Grid,
            status,
            Error: null,
            ImportJobId: null,
            ActualStart: null,
            Gaps: [],
            LoadedCandles: 0,
            ExpectedCandles: 0,
            CalculatedCombinations: 0,
            TotalCombinations: 12,
            Speed: null,
            StalledRestarts: 0,
            DateTimeOffsetGenerator.Generate(),
            StartedAt: null,
            FinishedAt: null);
    }
}