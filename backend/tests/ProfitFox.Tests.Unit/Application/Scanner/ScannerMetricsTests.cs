using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

using ProfitFox.Application.Scanner;
using ProfitFox.Domain.Entities.Market.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner;

/// <summary>Requirement "Счётчик ликвидаций": one per stored event, by the side of the liquidated position.</summary>
public class ScannerMetricsTests
{
    // Scenario "Пачка событий записана".
    [Test]
    public void Stored_liquidations_are_counted_by_side()
    {
        using ServiceProvider services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var collector = new MetricCollector<long>(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(), "ProfitFox.Scanner", "profitfox.scanner.liquidations");

        new ScannerMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>())
            .Liquidated(new Dictionary<LiquidationSide, int> { [LiquidationSide.Long] = 3, [LiquidationSide.Short] = 1 });

        collector.GetMeasurementSnapshot().Select(measurement => ((string)measurement.Tags["side"]!, measurement.Value))
            .ShouldBe([("long", 3L), ("short", 1L)], ignoreOrder: true);
    }
}