using System.Diagnostics.Metrics;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Application.TradeFlow;

/// <summary>
///     The metrics of the collector on the meter <c>ProfitFox.TradeFlow</c>: the trades it took from the stream, by category. The trades of
///     the daily archive are not counted.
/// </summary>
public sealed class TradeFlowMetrics(IMeterFactory meters)
{
    private readonly Counter<long> _trades = meters.Create("ProfitFox.TradeFlow").CreateCounter<long>("profitfox.tradeflow.trades", "{trade}");

    public void Traded(MarketCategory category)
    {
        _trades.Add(1, new KeyValuePair<string, object?>("category", category.ToCode()));
    }
}