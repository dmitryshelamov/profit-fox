using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>A bucket setup × type × interval × side × category with its statistics over the last <see cref="Days" /> days (90 or 30).</summary>
public sealed record IdeaBucketResponse(string Setup, string Type, string Interval, string Side, string Category, int Days, BucketStatisticsResponse Statistics)
{
    public static IdeaBucketResponse Of(IdeaBucket bucket)
    {
        IdeaBucketKey key = bucket.Key;

        return new IdeaBucketResponse(
            key.Setup.ToCode(),
            key.Type.ToCode(),
            key.Interval.ToCode(),
            key.Side.ToCode(),
            key.Category.ToCode(),
            bucket.Days,
            BucketStatisticsResponse.Of(bucket.Statistics)!);
    }
}