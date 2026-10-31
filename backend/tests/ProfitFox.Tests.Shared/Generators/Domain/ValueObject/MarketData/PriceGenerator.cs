using AutoFixture;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Shared.Generators.Domain.ValueObject.MarketData;

public static class PriceGenerator
{
    private static readonly Fixture Fixture = new();

    public static Price Generate() => new(Fixture.Create<decimal>());
}
