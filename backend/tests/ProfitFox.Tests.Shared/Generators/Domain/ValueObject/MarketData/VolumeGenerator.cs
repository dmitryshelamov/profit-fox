using AutoFixture;

using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Shared.Generators.Domain.ValueObject.MarketData;

public static class VolumeGenerator
{
    private static readonly Fixture Fixture = new Fixture();

    public static Volume Generate()
    {
        return new Volume(Fixture.Create<decimal>());
    }
}