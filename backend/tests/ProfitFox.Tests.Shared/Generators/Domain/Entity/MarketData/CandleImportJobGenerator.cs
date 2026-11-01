using AutoFixture;

using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Tests.Shared.Generators.Base;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.ValueObject.MarketData;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

public static class CandleImportJobGenerator
{
    private static readonly Fixture Fixture = new Fixture();

    /// <summary>A job with a non-empty period, no start or finish time and no lost leases; set those when the status needs them.</summary>
    public static CandleImportJob Generate(CandleImportStatus status)
    {
        DateTimeOffset from = DateTimeOffsetGenerator.Generate();

        return new CandleImportJob(
            Guid.NewGuid(),
            InstrumentGenerator.Generate(),
            IntervalGenerator.Generate(),
            from,
            from.AddHours(Fixture.Create<byte>() + 1),
            status,
            ImportedCandles: 0,
            Error: null,
            DateTimeOffsetGenerator.Generate(),
            StartedAt: null,
            FinishedAt: null,
            LostLeases: 0);
    }
}