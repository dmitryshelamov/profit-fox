using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner.Ideas;

/// <summary>The types × intervals whose exit rule changed, between the live ideas and the lane of statistics.</summary>
public class IdeaRecomputesTests
{
    [Test]
    public void Each_type_and_interval_is_taken_once()
    {
        var recomputes = new IdeaRecomputes();
        recomputes.Add(IdeaType.Pullback, Interval.FifteenMinutes);
        recomputes.Add(IdeaType.Pullback, Interval.FifteenMinutes);

        recomputes.Take().ShouldBe([(IdeaType.Pullback, Interval.FifteenMinutes)]);
        recomputes.Take().ShouldBeEmpty();
    }

    // Review of tune-idea-rules: the next whole recompute may be due already when a wait starts; the wait then ends at once.
    [Test]
    public async Task Wait_past_its_end_ends_at_once()
    {
        var recomputes = new IdeaRecomputes();

        await recomputes.WaitAsync(TimeSpan.FromSeconds(-5), new FakeTimeProvider(), CancellationToken.None);
    }

    [Test]
    public async Task Wait_ends_when_a_type_and_interval_is_added()
    {
        var recomputes = new IdeaRecomputes();
        Task wait = recomputes.WaitAsync(TimeSpan.FromHours(1), new FakeTimeProvider(), CancellationToken.None);

        recomputes.Add(IdeaType.TwoR, Interval.OneHour);

        await wait.WaitAsync(TimeSpan.FromSeconds(5));
    }
}