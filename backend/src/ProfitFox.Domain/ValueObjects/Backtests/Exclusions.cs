namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The orders that gave no trade, by the reason (change add-strategy-backtest, design.md, decision 5): no plan, a hole
///     in the minutes, no trade in the minutes, a hole in funding, and a time that ends after the end of history.
/// </summary>
public sealed record Exclusions(int Plan = 0, int Coverage = 0, int Funding = 0, int NoTrade = 0, int Period = 0)
{
    public static readonly Exclusions None = new Exclusions();

    public int Total => Plan + Coverage + Funding + NoTrade + Period;

    public static Exclusions operator +(Exclusions a, Exclusions b)
    {
        return new Exclusions(a.Plan + b.Plan, a.Coverage + b.Coverage, a.Funding + b.Funding, a.NoTrade + b.NoTrade, a.Period + b.Period);
    }
}
