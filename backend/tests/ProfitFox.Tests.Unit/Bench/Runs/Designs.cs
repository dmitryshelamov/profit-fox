using ProfitFox.Bench.Domain.Hypotheses;
using ProfitFox.Bench.Domain.Runs;
using ProfitFox.Tests.Unit.Bench.Hypotheses;

namespace ProfitFox.Tests.Unit.Bench.Runs;

/// <summary>
///     H0001 on a period, for the tests.
/// </summary>
internal static class Designs
{
    public static readonly Hypothesis H0001 = HypothesisReaderTests.Read(HypothesisReaderTests.H0001Folder);

    public static RunDesign Exploration(Hypothesis? hypothesis = null)
    {
        (DateTimeOffset start, DateTimeOffset end) = Periods.Span(Period.Exploration, DateTimeOffset.UtcNow);
        return new RunDesign(hypothesis ?? H0001, Period.Exploration, start, end);
    }

    public static DateTimeOffset Utc(int year, int month, int day, int hour = 0)
    {
        return new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero);
    }
}
