using System.Globalization;

using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Shared.Generators.Domain.ValueObject.Market;

/// <summary>A row of the market snapshot of a <c>linear</c> pair at 2024-03-10T12:00Z; every metric is empty unless the test gives them.</summary>
public static class MarketSnapshotRowGenerator
{
    public static readonly PairMetrics NoMetrics = new PairMetrics(null, null, null, null, null, null, null, null, null, null, null, null, null);

    public static MarketSnapshotRow Generate(string symbol = "AAAUSDT", MarketCategory category = MarketCategory.Linear, PairMetrics? metrics = null, bool incomplete = false)
    {
        return new MarketSnapshotRow(
            new Instrument(category, symbol),
            DateTimeOffset.Parse("2024-03-10T12:00:00Z", CultureInfo.InvariantCulture),
            metrics ?? NoMetrics,
            incomplete);
    }
}