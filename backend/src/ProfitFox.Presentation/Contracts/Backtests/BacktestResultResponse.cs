using System.Globalization;
using System.Text.Json.Nodes;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Backtests;

namespace ProfitFox.Presentation.Contracts.Backtests;

/// <param name="Combination"></param>
/// <param name="ParameterValues">Every parameter of the strategy with the value of the combination.</param>
/// <param name="Metrics"></param>
/// <param name="CalculationTime">As <c>hh:mm:ss.fffffff</c>.</param>
public sealed record BacktestResultResponse(long Combination, JsonObject ParameterValues, MetricsResponse Metrics, string CalculationTime)
{
    public static BacktestResultResponse Of(BacktestResult result)
    {
        return new BacktestResultResponse(
            result.Combination,
            StrategyJson.WriteValues(result.ParameterValues),
            MetricsResponse.Of(result.Metrics),
            result.CalculationTime.ToString("c", CultureInfo.InvariantCulture));
    }
}