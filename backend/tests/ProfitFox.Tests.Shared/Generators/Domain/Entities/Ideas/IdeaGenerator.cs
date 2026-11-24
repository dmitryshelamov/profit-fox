using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas;

/// <summary>
///     An idea for the tests (change add-ideas-and-market): <c>funding-short</c> version 4 short on <c>BTCUSDT</c> 4h at 12:00
///     of 2026-11-22 for 4 hours, of 100 USDT, without a stop and a target, unless the test gives what it checks.
/// </summary>
public static class IdeaGenerator
{
    public static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    public static Idea Generate(
        Guid? strategy = null,
        string name = "funding-short",
        int version = 4,
        string symbol = "BTCUSDT",
        TimeSpan? timeframe = null,
        EntrySide side = EntrySide.Short,
        DateTimeOffset? moment = null,
        TimeSpan? after = null,
        double? stop = null,
        double? target = null,
        decimal close = 96180m,
        IReadOnlyList<string>? labels = null)
    {
        return new Idea
        {
            Id = Guid.NewGuid(),
            StrategyId = strategy ?? Guid.NewGuid(),
            StrategyName = name,
            StrategyVersion = version,
            Timeframe = timeframe ?? TimeSpan.FromHours(4),
            Symbol = symbol,
            Side = side,
            Moment = moment ?? Noon,
            Size = 100,
            After = after ?? TimeSpan.FromHours(4),
            Stop = stop,
            Target = target,
            CandleClose = close,
            Labels = labels ?? []
        };
    }
}
