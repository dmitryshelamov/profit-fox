using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.IdeaAutos;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.IdeaAutos;

public class IdeaAutoTests
{
    // Scenario "Новый автомат": a new auto is third in a list of two, switched off, with the conditions it is given.
    [Test]
    public void New_auto_is_off_at_the_end_of_the_list_with_the_conditions_it_is_given()
    {
        var account = Guid.NewGuid();
        var conditions = new IdeaAutoConditions(
            [IdeaSetup.Impulse],
            [IdeaType.TwoR, IdeaType.Pullback],
            [Interval.FiveMinutes],
            IdeaSide.Long,
            MarketCategory.Linear,
            IdeaBadge.Flat,
            1.2m,
            100);

        var auto = IdeaAuto.Create(Guid.NewGuid(), "Импульс лонг", account, 300m, conditions, 2);

        (auto.Name, auto.AccountId, auto.Amount, auto.Mode, auto.Position, auto.DeletedAt).ShouldBe(("Импульс лонг", account, 300m, IdeaAutoMode.Off, 2, null));
        auto.Conditions.ShouldBe(conditions);
    }
}