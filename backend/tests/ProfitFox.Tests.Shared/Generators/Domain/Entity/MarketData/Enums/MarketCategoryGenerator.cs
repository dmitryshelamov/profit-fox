using AutoFixture;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData.Enums;

public static class MarketCategoryGenerator
{
    private static readonly Fixture Fixture = new();

    public static MarketCategory Generate() => Fixture.Create<MarketCategory>();
}
