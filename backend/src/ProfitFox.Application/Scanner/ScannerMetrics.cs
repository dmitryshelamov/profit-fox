using System.Diagnostics.Metrics;

using ProfitFox.Application.Market;
using ProfitFox.Domain.Entities.Market.Enums;

namespace ProfitFox.Application.Scanner;

/// <summary>The metrics of the scanner on the meter <c>ProfitFox.Scanner</c>: the liquidations storage added, by the side of the position.</summary>
public sealed class ScannerMetrics(IMeterFactory meters)
{
    private readonly Counter<long> _liquidations = meters.Create("ProfitFox.Scanner").CreateCounter<long>("profitfox.scanner.liquidations", "{liquidation}");

    public void Liquidated(IReadOnlyDictionary<LiquidationSide, int> inserted)
    {
        foreach ((LiquidationSide side, int count) in inserted)
        {
            _liquidations.Add(count, new KeyValuePair<string, object?>("side", side.ToCode()));
        }
    }
}