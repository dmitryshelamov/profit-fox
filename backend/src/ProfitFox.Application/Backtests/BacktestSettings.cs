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

    /// <summary>Base fee rates of the exchange and the slippage of the ideas in percent by market category code (<c>spot</c>, <c>linear</c>, <c>moex</c>).</summary>
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
                fees => new FeeRates(fees.Value.Limit, fees.Value.Market, fees.Value.Slippage ?? 0m)));
    }

    /// <summary>The codes of the market categories without base rates in the settings; a backtest on such a category would have no fees to count with.</summary>
    public IReadOnlyList<string> CategoriesWithoutFees()
    {
        return [.. Enum.GetValues<MarketCategory>().Select(category => category.ToCode()).Where(code => !DefaultFees.ContainsKey(code))];
    }

    /// <summary>Whether both categories of Bybit have the slippage of the ideas set, not below zero; the ideas of <c>moex</c> are a later change.</summary>
    public bool HasIdeaSlippage()
    {
        return new[] { "spot", "linear" }.All(category => DefaultFees.TryGetValue(category, out FeeRateSettings? fees) && fees.Slippage >= 0m);
    }
}