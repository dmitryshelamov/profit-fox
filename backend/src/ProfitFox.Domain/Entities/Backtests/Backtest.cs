using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;

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

    /// <summary>Statuses of a backtest that has not ended yet: it can still be cancelled.</summary>
    public static readonly IReadOnlyList<BacktestStatus> Unfinished = [BacktestStatus.Pending, BacktestStatus.LoadingCandles, BacktestStatus.Running];

    // A backtest that keeps stopping the service before it saves anything would otherwise restart it forever.
    private const int MaxStalledRestarts = 3;

    /// <summary>
    ///     A pending backtest of <paramref name="bot" /> at <paramref name="latest" />, the latest version of its strategy. A parameter the
    ///     backtest does not give takes the value of the bot, otherwise the default of that version.
    /// </summary>
    /// <exception cref="BacktestValidationException">The period, a parameter, the size of the grid or a simulation setting is invalid.</exception>
    public static Backtest Create(
        Guid id,
        Bot bot,
        StrategyVersion latest,
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyDictionary<string, ParameterSpec> specs,
        SimulationRequest simulation,
        BacktestPolicy policy,
        InstrumentRules rules,
        DateTimeOffset now)
    {
        var errors = new List<BacktestError>();
        StrategyDefinition definition = latest.Snapshot.Definition;

        if (from >= to)
        {
            errors.Add(new BacktestError(BacktestField.Period, null, "from must be earlier than to."));
        }
        else if (to > now)
        {
            errors.Add(new BacktestError(BacktestField.To, null, "to must not be in the future."));
        }
        else if (bot.Interval.LastCandleEnd(from, to) > now)
        {
            // Only closed candles are stored: one still open would be missing from the run and turn up in later details.
            errors.Add(new BacktestError(BacktestField.To, null, "to must not be later than the end of the last closed candle."));
        }

        int parameterErrors = errors.Count;
        errors.AddRange(ParameterErrors(definition.Parameters, specs));
        var parameters = definition.Parameters.ToDictionary(
            parameter => parameter.Name,
            parameter => specs.GetValueOrDefault(parameter.Name) ?? ParameterSpec.Of(bot.ParameterValues.GetValueOrDefault(parameter.Name, parameter.Default)));
        // A grid of invalid parameters has no meaningful size.
        long? combinations = errors.Count == parameterErrors ? new ParameterGrid(parameters).Count : 1;

        if (combinations is null || combinations > policy.MaxCombinations)
        {
            errors.Add(new BacktestError(
                BacktestField.Combinations,
                null,
                combinations is { } count
                    ? $"The grid has {count} combinations, more than the limit of {policy.MaxCombinations}."
                    : $"The grid has more combinations than the limit of {policy.MaxCombinations}."));
        }

        FeeRates fees = policy.DefaultFees[bot.Instrument.Category];
        var settings = new SimulationSettings(
            simulation.Balance ?? bot.Balance,
            simulation.LimitFeePercent ?? fees.LimitPercent,
            simulation.MarketFeePercent ?? fees.MarketPercent,
            simulation.SlippagePercent ?? 0m,
            simulation.LimitFillMode ?? LimitFillMode.Breakout);

        if (settings.Balance <= 0m)
        {
            errors.Add(new BacktestError(BacktestField.Balance, null, "Balance must be greater than zero."));
        }

        if (settings.LimitFeePercent < 0m)
        {
            errors.Add(new BacktestError(BacktestField.LimitFee, null, "Fee rate must not be negative."));
        }

        if (settings.MarketFeePercent < 0m)
        {
            errors.Add(new BacktestError(BacktestField.MarketFee, null, "Fee rate must not be negative."));
        }

        // A better fill than the open, or a buy at no price, is no slippage.
        if (settings.SlippagePercent is < 0m or >= 100m)
        {
            errors.Add(new BacktestError(BacktestField.Slippage, null, "Slippage must be from 0 up to 100 percent."));
        }

        if (errors.Count > 0)
        {
            throw new BacktestValidationException(errors);
        }

        return new Backtest(
            id,
            bot.Id,
            bot.StrategyId,
            latest.Number,
            bot.Instrument,
            bot.Interval,
            from,
            to,
            parameters,
            settings,
            rules,
            SearchMode.Grid,
            BacktestStatus.Pending,
            Error: null,
            ImportJobId: null,
            ActualStart: null,
            Gaps: [],
            LoadedCandles: 0,
            ExpectedCandles: 0,
            CalculatedCombinations: 0,
            combinations!.Value,
            Speed: null,
            StalledRestarts: 0,
            now,
            StartedAt: null,
            FinishedAt: null);
    }

    /// <summary>Whether the bot no longer trades what the backtest ran: a newer strategy version, another instrument or interval.</summary>
    public bool BotChanged(Bot bot, int latestVersion)
    {
        return latestVersion > StrategyVersion || bot.Instrument != Instrument || bot.Interval != Interval;
    }

    public ParameterGrid Grid()
    {
        return new ParameterGrid(Parameters);
    }

    /// <summary>A replay of one combination with the settings and the instrument rules of the backtest, fees and slippage as fractions.</summary>
    public BacktestReplay Replay(StrategySnapshot snapshot, IReadOnlyDictionary<string, NodeValue> values)
    {
        var fees = new FeeSchedule(Simulation.LimitFeePercent / 100m, Simulation.MarketFeePercent / 100m, Instrument.Category);

        return new BacktestReplay(
            snapshot,
            values,
            new TradingConditions(Simulation.Balance, Rules, fees),
            Simulation.SlippagePercent / 100m,
            Simulation.LimitFillMode,
            Interval);
    }

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

    /// <summary>A value is checked as a value of a bot, a range by its own rules; every spec must name a parameter of the strategy.</summary>
    private static IEnumerable<BacktestError> ParameterErrors(IReadOnlyList<StrategyParameter> parameters, IReadOnlyDictionary<string, ParameterSpec> specs)
    {
        foreach (StrategyParameter parameter in parameters)
        {
            string? reason = !specs.TryGetValue(parameter.Name, out ParameterSpec? spec) ? null
                : spec.Range is { } range ? range.Reject(parameter)
                : parameter.Reject(spec.Value!) is { } rejected ? $"Value {rejected}" : null;

            if (reason is not null)
            {
                yield return new BacktestError(BacktestField.Parameter, parameter.Name, reason);
            }
        }

        foreach (string unknown in specs.Keys.Where(name => parameters.All(parameter => parameter.Name != name)))
        {
            yield return new BacktestError(BacktestField.Parameter, unknown, "No such parameter.");
        }
    }

    /// <summary>A running backtest the previous process left counts one more stalled restart; the third one fails it.</summary>
    public Backtest Restarted(DateTimeOffset now)
    {
        Backtest restarted = this with { StalledRestarts = StalledRestarts + 1 };

        return restarted.StalledRestarts == MaxStalledRestarts ? restarted.Fail(InterruptedByRestart, now) : restarted;
    }
}