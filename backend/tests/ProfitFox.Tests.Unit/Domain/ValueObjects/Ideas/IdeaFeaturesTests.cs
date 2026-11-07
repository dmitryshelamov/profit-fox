using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.Ideas;

/// <summary>Requirement "Признаки идей": a strong move from 4 ATR and a high volume from 4 medians, known on the minute a signal fired.</summary>
public class IdeaFeaturesTests
{
    // Scenarios "Сильное движение на обычном объёме", "Ровно на границе" and "Ниже границ".
    [TestCase(4.2, 2.5, true, false)]
    [TestCase(4, 4, true, true)]
    [TestCase(3.99, 3.99, false, false)]
    public void Features_of_a_signal_are_its_move_and_volume_against_the_bounds(decimal move, decimal volumeRatio, bool strongMove, bool highVolume)
    {
        IdeaFeatures.Of(move, volumeRatio).ShouldBe(new IdeaFeatures(strongMove, highVolume));
    }
}