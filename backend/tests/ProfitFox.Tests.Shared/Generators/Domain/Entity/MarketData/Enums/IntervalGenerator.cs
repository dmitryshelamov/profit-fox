using AutoFixture;

using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData.Enums;

public static class IntervalGenerator
{
    private static readonly Fixture Fixture = new();

    public static Interval Generate() => Fixture.Create<Interval>();
}
