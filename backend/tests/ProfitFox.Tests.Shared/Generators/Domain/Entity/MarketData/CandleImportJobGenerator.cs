using AutoFixture;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Tests.Shared.Generators.Base;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.ValueObject.MarketData;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

public static class CandleImportJobGenerator
{
    private static readonly Fixture Fixture = new();

    /// <summary>A job with a non-empty period and no start or finish time; set those when the status needs them.</summary>
    public static CandleImportJob Generate(CandleImportStatus status)
    {
        var from = DateTimeOffsetGenerator.Generate();
        return new CandleImportJob(
            Guid.NewGuid(),
            InstrumentGenerator.Generate(),
            IntervalGenerator.Generate(),
            from,
            from.AddHours(Fixture.Create<byte>() + 1),
            status,
            0,
            null,
            DateTimeOffsetGenerator.Generate(),
            null,
            null);
    }
}
