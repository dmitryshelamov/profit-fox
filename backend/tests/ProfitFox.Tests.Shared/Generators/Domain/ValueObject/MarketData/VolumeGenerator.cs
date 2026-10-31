using AutoFixture;

using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Shared.Generators.Domain.ValueObject.MarketData;

public static class VolumeGenerator
{
    private static readonly Fixture Fixture = new();

    public static Volume Generate() => new(Fixture.Create<decimal>());
}
