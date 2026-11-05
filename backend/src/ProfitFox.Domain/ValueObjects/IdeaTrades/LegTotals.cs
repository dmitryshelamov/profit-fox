namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>What the exchange counted of one order of a trade so far: the quantity held or released, its value and the fee, both in the quote asset.</summary>
public sealed record LegTotals(decimal Quantity, decimal Value, decimal Fee)
{
    public static readonly LegTotals None = new LegTotals(0m, 0m, 0m);
}