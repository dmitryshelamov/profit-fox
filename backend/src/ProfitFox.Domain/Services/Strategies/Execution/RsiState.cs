namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>
///     Relative strength index: the first <c>period</c> changes seed the average gain and loss with their simple averages, every
///     later change smooths them as Wilder did.
/// </summary>
internal sealed class RsiState : IIndicatorState
{
    private decimal? _previous;
    private int _changes;
    private decimal _gains;
    private decimal _losses;
    private decimal? _averageGain;
    private decimal? _averageLoss;

    public decimal? Value =>
        _averageGain is { } gain && _averageLoss is { } loss ? gain + loss == 0m ? 50m : 100m * gain / (gain + loss) : null;

    public void Add(decimal value, decimal period)
    {
        if (_previous is { } previous)
        {
            decimal change = value - previous;
            decimal gain = Math.Max(change, 0m);
            decimal loss = Math.Max(-change, 0m);
            _changes++;

            if (_averageGain is { } averageGain && _averageLoss is { } averageLoss)
            {
                _averageGain = (averageGain * (period - 1m) + gain) / period;
                _averageLoss = (averageLoss * (period - 1m) + loss) / period;
            }
            else
            {
                _gains += gain;
                _losses += loss;

                if (_changes >= period)
                {
                    _averageGain = _gains / _changes;
                    _averageLoss = _losses / _changes;
                }
            }
        }

        _previous = value;
    }
}