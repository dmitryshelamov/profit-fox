namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>The last <c>period</c> values of an input.</summary>
internal sealed class WindowState : IIndicatorState
{
    private readonly Queue<decimal> _values = new Queue<decimal>();
    private decimal _sum;

    public void Add(decimal value, decimal period)
    {
        _values.Enqueue(value);
        _sum += value;

        while (_values.Count > period)
        {
            _sum -= _values.Dequeue();
        }
    }

    public decimal? Average(decimal period)
    {
        return _values.Count >= period ? _sum / _values.Count : null;
    }

    /// <summary>Of the whole window: the mean in one pass, the squares of the distances from it in the second.</summary>
    public decimal? Deviation(decimal period)
    {
        if (Average(period) is not { } mean)
        {
            return null;
        }

        decimal squares = _values.Sum(value => (value - mean) * (value - mean));

        return DecimalSquareRoot.Of(squares / _values.Count);
    }
}