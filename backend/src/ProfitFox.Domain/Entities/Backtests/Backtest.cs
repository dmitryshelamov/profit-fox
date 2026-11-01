using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Entities.Backtests;

/// <summary>
///     A run of a bot on stored candles of <c>[From, To)</c> at the strategy version it was created on, one result per combination of
///     parameter values. It goes <c>Pending</c> → <c>LoadingCandles</c> → <c>Running</c> and ends <c>Succeeded</c>, <c>Failed</c> or
///     <c>Cancelled</c>. <see cref="StalledRestarts" /> counts restarts in a row that stopped it running before it saved a result.
/// </summary>
public sealed record Backtest(
    Guid Id,
    Guid BotId,
    Guid StrategyId,
    int StrategyVersion,
    Instrument Instrument,
    Interval Interval,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyDictionary<string, ParameterSpec> Parameters,
    SimulationSettings Simulation,
    InstrumentRules Rules,
    SearchMode SearchMode,
    BacktestStatus Status,
    string? Error,
    Guid? ImportJobId,
    DateTimeOffset? ActualStart,
    IReadOnlyList<CandleGap> Gaps,
    long LoadedCandles,
    long ExpectedCandles,
    long CalculatedCombinations,
    long TotalCombinations,
    decimal? Speed,
    int StalledRestarts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt)
{
    public const string InterruptedByRestart = "Interrupted by service restart.";

    // A backtest that keeps stopping the service before it saves anything would otherwise restart it forever.
    private const int MaxStalledRestarts = 3;

    public Backtest StartLoading(DateTimeOffset now)
    {
        return this with { Status = BacktestStatus.LoadingCandles, StartedAt = now };
    }

    public Backtest WaitForImport(Guid jobId)
    {
        return this with { ImportJobId = jobId };
    }

    public Backtest Loaded(long loaded, long expected)
    {
        return this with { LoadedCandles = loaded, ExpectedCandles = expected };
    }

    /// <param name="actualStart">Open time of the first stored candle of the period, if there is one.</param>
    /// <param name="gaps"></param>
    public Backtest StartRunning(DateTimeOffset? actualStart, IReadOnlyList<CandleGap> gaps)
    {
        return this with { Status = BacktestStatus.Running, ActualStart = actualStart, Gaps = gaps };
    }

    /// <param name="calculated"></param>
    /// <param name="speed">Candles a second; <c>null</c> before any time has passed.</param>
    public Backtest Progress(long calculated, decimal? speed)
    {
        return this with { CalculatedCombinations = calculated, Speed = speed, StalledRestarts = 0 };
    }

    public Backtest Succeed(DateTimeOffset now)
    {
        return this with { Status = BacktestStatus.Succeeded, FinishedAt = now };
    }

    public Backtest Fail(string reason, DateTimeOffset now)
    {
        return this with { Status = BacktestStatus.Failed, Error = reason, FinishedAt = now };
    }

    /// <exception cref="BacktestFinishedException">The backtest has already finished.</exception>
    public Backtest Cancel(DateTimeOffset now)
    {
        if (Status is BacktestStatus.Succeeded or BacktestStatus.Failed or BacktestStatus.Cancelled)
        {
            throw new BacktestFinishedException();
        }

        return this with { Status = BacktestStatus.Cancelled, FinishedAt = now };
    }

    /// <summary>A running backtest the previous process left counts one more stalled restart; the third one fails it.</summary>
    public Backtest Restarted(DateTimeOffset now)
    {
        Backtest restarted = this with { StalledRestarts = StalledRestarts + 1 };

        return restarted.StalledRestarts == MaxStalledRestarts ? restarted.Fail(InterruptedByRestart, now) : restarted;
    }
}