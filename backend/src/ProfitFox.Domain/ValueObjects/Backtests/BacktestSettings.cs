using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The checked settings of a backtest (spec strategy-backtest «Настройки прогона»): the values of the parameters that
///     were given, in the order of the graph; the timeframes; history from <see cref="Start" /> to <see cref="End" />, the
///     end not included, both at the start of a UTC day; the pairs by symbol, or null for every listed pair; and the fees.
/// </summary>
public sealed record BacktestSettings(
    IReadOnlyList<ParameterValues> Values,
    IReadOnlyList<TimeSpan> Timeframes,
    DateTimeOffset Start,
    DateTimeOffset End,
    IReadOnlyList<string>? Pairs,
    double TakerFee,
    double MakerFee)
{
    /// <summary>
    ///     The costs of a trade with the fees of the settings and the slippage and funding of the bench.
    /// </summary>
    public TradeCosts Costs => TradeCosts.Default with { TakerFee = TakerFee, MakerFee = MakerFee };
}
