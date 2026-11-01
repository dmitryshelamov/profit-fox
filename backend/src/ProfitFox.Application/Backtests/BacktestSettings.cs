using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Application.Backtests;

/// <summary>The <c>Backtests</c> section of the service settings.</summary>
public sealed class BacktestSettings
{
    public long MaxCombinations { get; init; }

    /// <summary>Threads one backtest computes combinations on; without it, every core but one, at least one.</summary>
    public int? MaxParallelism { get; init; }

    /// <summary>Base fee rates of the exchange in percent by market category code (<c>spot</c>, <c>linear</c>).</summary>
    public Dictionary<string, FeeRateSettings> DefaultFees { get; init; } = [];

    public int Threads => MaxParallelism ?? Math.Max(Environment.ProcessorCount - 1, 1);

    public BacktestPolicy Policy()
    {
        return new BacktestPolicy(
            MaxCombinations,
            DefaultFees.ToDictionary(
                fees =>
                {
                    MarketDataCodes.TryParseCategory(fees.Key, out MarketCategory category);

                    return category;
                },
                fees => new FeeRates(fees.Value.Limit, fees.Value.Market)));
    }
}