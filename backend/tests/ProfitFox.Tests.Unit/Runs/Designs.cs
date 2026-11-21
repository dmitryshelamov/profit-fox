using ProfitFox.Domain.Services.Hypotheses;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Tests.Unit.Hypotheses;

namespace ProfitFox.Tests.Unit.Runs;

/// <summary>
///     H0001 on a period, for the tests.
/// </summary>
internal static class Designs
{
    public static readonly Hypothesis H0001 = HypothesisReader.Parse(HypothesisReaderTests.H0001Text);

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
