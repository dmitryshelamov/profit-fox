using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.IdeaTrades;

/// <summary>Requirement "Исход идеи на сделке" with the base fees of Bybit linear: maker 0.02 %, taker 0.055 %.</summary>
public class TradedIdeaOutcomeTests
{
    private static readonly FeeRates Fees = new FeeRates(0.02m, 0.055m);
    private static readonly DateTimeOffset Fired = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    // Scenario "Идея дошла до цели": entry 105, target 115, stop 100 gives 2 R less the taker fees, (10 − 0.121) / 5.
    [Test]
    public void Idea_that_reached_its_target_gives_its_status_time_and_result_in_r()
    {
        Idea idea = IdeaGenerator.Generate(Fired, IdeaStatus.Target, statusAt: Fired.AddHours(2));

        TradedIdeaOutcome.Of(idea.Track, Fees).ShouldBe(new TradedIdeaOutcome(IdeaStatus.Target, Fired.AddHours(2), 1.9758m));
    }

    // Scenario "Откат не вошёл": no entry, no result.
    [Test]
    public void Pullback_that_expired_without_an_entry_has_no_result()
    {
        IdeaTrack track = IdeaGenerator.Generate(Fired, IdeaStatus.AwaitingEntry, type: IdeaType.Pullback).Track with { Status = IdeaStatus.Expired, StatusAt = Fired.AddHours(1) };

        TradedIdeaOutcome.Of(track, Fees).ShouldBe(new TradedIdeaOutcome(IdeaStatus.Expired, Fired.AddHours(1), null));
    }
}