using ProfitFox.Domain.Services.Strategies.Execution;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

public class DecimalSquareRootTests
{
    private static IEnumerable<TestCaseData> ExactSquares()
    {
        yield return new TestCaseData(0m, 0m);
        yield return new TestCaseData(1m, 1m);
        yield return new TestCaseData(4m, 2m);
        yield return new TestCaseData(2.25m, 1.5m);
        yield return new TestCaseData(0.0001m, 0.01m);
        yield return new TestCaseData(144m, 12m);
        yield return new TestCaseData(1000000000000m, 1000000m);
        yield return new TestCaseData(6561m, 81m);
    }

    [TestCaseSource(nameof(ExactSquares))]
    public void Square_root_of_an_exact_square_is_exact(decimal square, decimal root)
    {
        DecimalSquareRoot.Of(square).ShouldBe(root);
    }

    [Test]
    public void Square_root_of_two_is_correct_to_the_last_digits()
    {
        decimal root = DecimalSquareRoot.Of(2m);

        Math.Abs(root * root - 2m).ShouldBeLessThan(0.000000000000000000000001m);
    }
}