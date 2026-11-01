namespace ProfitFox.Application.Backtests;

/// <summary>
///     The inputs of the backtest this Worker computed last, so that its next chunk does not read the candles again. A Worker computes
///     one chunk at a time, so the cache holds one backtest and needs no lock.
/// </summary>
public sealed class BacktestInputCache
{
    private BacktestInputs? _last;

    public async Task<BacktestInputs> GetAsync(Guid backtestId, Func<Task<BacktestInputs>> load)
    {
        if (_last?.BacktestId != backtestId)
        {
            _last = await load();
        }

        return _last;
    }
}