using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

using ProfitFox.Application.TradeFlow;
using ProfitFox.Domain.Entities.MarketData.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.TradeFlow;

/// <summary>Requirement "Счётчик сделок потока": one per trade the collector took from the stream, by category.</summary>
public class TradeFlowMetricsTests
{
    // Scenario "Сделки приняты".
    [Test]
    public void Trades_are_counted_by_category()
    {
        using ServiceProvider services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var collector = new MetricCollector<long>(services.GetRequiredService<IMeterFactory>(), "ProfitFox.TradeFlow", "profitfox.tradeflow.trades");
        var metrics = new TradeFlowMetrics(services.GetRequiredService<IMeterFactory>());

        for (var i = 0; i < 300; i++)
        {
            metrics.Traded(MarketCategory.Linear);
        }

        for (var i = 0; i < 40; i++)
        {
            metrics.Traded(MarketCategory.Spot);
        }

        collector.GetMeasurementSnapshot().GroupBy(measurement => (string)measurement.Tags["category"]!).Select(group => (group.Key, group.Sum(measurement => measurement.Value)))
            .ShouldBe([("linear", 300L), ("spot", 40L)], ignoreOrder: true);
    }
}