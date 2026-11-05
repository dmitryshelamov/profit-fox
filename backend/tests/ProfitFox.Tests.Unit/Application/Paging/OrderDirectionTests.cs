using ProfitFox.Application.Paging;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Paging;

public class OrderDirectionTests
{
    [TestCase("asc", OrderDirection.Ascending)]
    [TestCase("desc", OrderDirection.Descending)]
    public void A_direction_is_read_by_its_code(string code, OrderDirection expected)
    {
        OrderDirections.TryParse(code, out OrderDirection direction).ShouldBeTrue();
        direction.ShouldBe(expected);
    }

    [TestCase("up")]
    [TestCase("ASC")]
    public void Another_code_is_not_read(string code)
    {
        OrderDirections.TryParse(code, out _).ShouldBeFalse();
    }
}