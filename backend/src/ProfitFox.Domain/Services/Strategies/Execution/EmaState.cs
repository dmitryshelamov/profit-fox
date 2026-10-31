namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>Exponential moving average seeded with the simple average of the first <c>period</c> values.</summary>
internal sealed class EmaState
{
    private int _count;
    private decimal _sum;

    public decimal? Value { get; private set; }

    public void Add(decimal value, decimal period)
    {
        _count++;

        if (Value is { } previous)
        {
            Value = previous + 2m / (period + 1m) * (value - previous);

            return;
        }

        _sum += value;

        if (_count >= period)
        {
            Value = _sum / _count;
        }
    }
}