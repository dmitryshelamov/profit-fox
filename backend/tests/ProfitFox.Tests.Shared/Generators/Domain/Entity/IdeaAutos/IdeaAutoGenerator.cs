using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.IdeaAutos;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entity.IdeaAutos;

/// <summary>An auto that takes <c>Impulse</c> ideas of 15 minutes and 1 hour, of the types «Цель 2R» and «Откат», on both sides and markets, with any badge.</summary>
public static class IdeaAutoGenerator
{
    public static IdeaAuto Generate(Guid accountId, string name = "Импульс лонг", int position = 0, IdeaAutoMode mode = IdeaAutoMode.Off, decimal amount = 300m, IdeaAutoConditions? conditions = null)
    {
        conditions ??= new IdeaAutoConditions(
            [IdeaSetup.Impulse],
            [IdeaType.TwoR, IdeaType.Pullback],
            [Interval.FifteenMinutes, Interval.OneHour],
            null,
            null,
            null,
            1.2m,
            0);

        return new IdeaAuto(Guid.NewGuid(), name, accountId, amount, mode, conditions, position);
    }
}