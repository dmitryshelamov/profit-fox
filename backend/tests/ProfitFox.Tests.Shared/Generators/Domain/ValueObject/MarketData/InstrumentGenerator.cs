using AutoFixture;

using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData.Enums;

namespace ProfitFox.Tests.Shared.Generators.Domain.ValueObject.MarketData;

public static class InstrumentGenerator
{
    private static readonly Fixture Fixture = new Fixture();

    public static Instrument Generate()
    {
        return new Instrument(MarketCategoryGenerator.Generate(), Fixture.Create<string>());
    }
}