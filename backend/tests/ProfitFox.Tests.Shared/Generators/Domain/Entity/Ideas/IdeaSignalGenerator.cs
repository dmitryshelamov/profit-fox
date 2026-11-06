using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

/// <summary>
///     Signals of the replay: a long entered at the market at 100 with the stop at 98 at the open after it fired, the closes of the grid at 100,
///     the path known for 14 days. A target touches +2 R at 30 minutes, a stop at 10; «Откат» without an entry waits in vain.
/// </summary>
public static class IdeaSignalGenerator
{
    public static IdeaSignal Generate(
        DateTimeOffset firedAt,
        IdeaStatus outcome = IdeaStatus.Target,
        Instrument? instrument = null,
        IdeaSetup setup = IdeaSetup.Impulse,
        IdeaType type = IdeaType.TwoR,
        Interval interval = Interval.FifteenMinutes,
        int engineVersion = IdeasEngineVersion.Current)
    {
        var ladder = new int?[10];

        if (outcome == IdeaStatus.Target)
        {
            ladder[0] = 12;
            ladder[3] = 30;
        }

        SignalPath path = SignalPath.Fired(new IdeaPlan(type, IdeaSide.Long, 100m, 98m, 104m), interval, firedAt, 0.01m) with
        {
            EntryAfter = outcome == IdeaStatus.AwaitingEntry ? null : 1,
            StopAfter = outcome == IdeaStatus.Stop ? 10 : null,
            LadderAfter = ladder,
            Closes = outcome == IdeaStatus.Stop ? [] : [.. ExitRuleGrid.Of(interval).Select(_ => 100m)],
            TrackedUntil = firedAt.AddDays(14)
        };

        return new IdeaSignal(instrument ?? IdeaGenerator.Wif, setup, path, 3.5m, 2.25m, engineVersion);
    }

    /// <summary>A signal as text with the ladder and the closes written out, for comparing signals: their lists compare by reference.</summary>
    public static string Shape(IdeaSignal signal)
    {
        SignalPath path = signal.Path;

        return $"{signal with { Path = path with { LadderAfter = [], Closes = [] } }} [{string.Join(',', path.LadderAfter)}] [{string.Join(',', path.Closes)}]";
    }
}