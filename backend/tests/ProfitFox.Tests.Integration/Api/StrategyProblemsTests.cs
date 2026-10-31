using ProfitFox.Presentation.Contracts.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class StrategyProblemsTests
{
    [Test]
    public void Unexpected_exception_is_not_turned_into_an_answer()
    {
        StrategyProblems.Of(new InvalidOperationException()).ShouldBeNull();
    }
}