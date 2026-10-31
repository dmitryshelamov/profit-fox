using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.Strategies;

public class GraphTests
{
    [Test]
    public void Number_on_an_input_keeps_its_exact_decimal_value()
    {
        Graph graph = new GraphBuilder()
            .Node("mul1", SimpleNodeKind.Multiply, ("a", Values.Number(0.1m)), ("b", Values.Number(3m)))
            .Build();

        graph.Nodes.Single().Inputs["a"].ShouldBe(new NumberValue(0.1m));
    }
}