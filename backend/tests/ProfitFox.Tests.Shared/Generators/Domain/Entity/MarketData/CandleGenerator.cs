using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Base;
using ProfitFox.Tests.Shared.Generators.Domain.ValueObject.MarketData;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

public static class CandleGenerator
{
    public static Candle Generate(DateTimeOffset? openTime = null, Price? close = null)
    {
        return new Candle(
            openTime ?? DateTimeOffsetGenerator.Generate(),
            PriceGenerator.Generate(),
            PriceGenerator.Generate(),
            PriceGenerator.Generate(),
            close ?? PriceGenerator.Generate(),
            VolumeGenerator.Generate(),
            VolumeGenerator.Generate());
    }
}