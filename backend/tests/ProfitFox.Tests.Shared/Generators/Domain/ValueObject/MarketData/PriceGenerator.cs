using AutoFixture;

using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Shared.Generators.Domain.ValueObject.MarketData;

public static class PriceGenerator
{
    private static readonly Fixture Fixture = new Fixture();

    public static Price Generate()
    {
        return new Price(Fixture.Create<decimal>());
    }
}