namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The probability of backtest overfitting of a grid of exit rules: of the <see cref="Splits" /> that counted, the <see cref="Overfit" />
///     ones, where the best rule of one half was at the middle or below on the other.
/// </summary>
public sealed record Overfitting(int Splits, int Overfit)
{
    public decimal Probability => (decimal)Overfit / Splits;
}