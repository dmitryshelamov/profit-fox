using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.ValueObjects.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

public class NodeFunctionsTests
{
    [TestCase(SimpleNodeKind.Add, 6, 3, 9)]
    [TestCase(SimpleNodeKind.Subtract, 6, 3, 3)]
    [TestCase(SimpleNodeKind.Multiply, 0.1, 3, 0.3)]
    [TestCase(SimpleNodeKind.Divide, 100, 8, 12.5)]
    public void Arithmetic_on_numbers(SimpleNodeKind kind, decimal a, decimal b, decimal expected)
    {
        NodeFunctions.Arithmetic(kind, new NumberValue(a), new NumberValue(b)).ShouldBe(new NumberValue(expected));
    }

    [Test]
    public void Arithmetic_with_a_missing_value_gives_no_value()
    {
        NodeFunctions.Arithmetic(SimpleNodeKind.Add, null, new NumberValue(1m)).ShouldBeNull();
        NodeFunctions.Arithmetic(SimpleNodeKind.Add, new NumberValue(1m), null).ShouldBeNull();
    }

    [Test]
    public void Division_by_zero_gives_no_value()
    {
        NodeFunctions.Arithmetic(SimpleNodeKind.Divide, new NumberValue(1m), new NumberValue(0m)).ShouldBeNull();
    }

    [TestCase(CompareOperation.Greater, 2, 1, true)]
    [TestCase(CompareOperation.Greater, 1, 1, false)]
    [TestCase(CompareOperation.GreaterOrEqual, 1, 1, true)]
    [TestCase(CompareOperation.GreaterOrEqual, 0, 1, false)]
    [TestCase(CompareOperation.Less, 0, 1, true)]
    [TestCase(CompareOperation.Less, 1, 1, false)]
    [TestCase(CompareOperation.LessOrEqual, 1, 1, true)]
    [TestCase(CompareOperation.LessOrEqual, 2, 1, false)]
    public void Compare_numbers(CompareOperation operation, decimal a, decimal b, bool expected)
    {
        NodeFunctions.Compare(operation, new NumberValue(a), new NumberValue(b)).ShouldBe(expected);
    }

    [Test]
    public void Compare_durations()
    {
        NodeFunctions.Compare(CompareOperation.GreaterOrEqual, new DurationValue(TimeSpan.FromHours(1)), new DurationValue(TimeSpan.FromMinutes(60))).ShouldBeTrue();
        NodeFunctions.Compare(CompareOperation.Less, new DurationValue(TimeSpan.FromHours(2)), new DurationValue(TimeSpan.FromHours(1))).ShouldBeFalse();
    }

    [Test]
    public void Compare_with_a_missing_value_is_false()
    {
        NodeFunctions.Compare(CompareOperation.LessOrEqual, null, new NumberValue(1m)).ShouldBeFalse();
        NodeFunctions.Compare(CompareOperation.LessOrEqual, new NumberValue(1m), null).ShouldBeFalse();
    }
}