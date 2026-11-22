using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Queries.Preview;

/// <summary>
///     The end of exploration of a preview (spec signal-preview and spec strategy-preview «Только разведка»; change
///     add-strategy-backtest, design.md, decision 8): the start of the current UTC day of the clock of the request. History
///     from it on is never read.
/// </summary>
public static class PreviewPeriod
{
    public static DateTimeOffset End(DateTimeOffset now)
    {
        return HistoryPeriod.Today(now);
    }
}