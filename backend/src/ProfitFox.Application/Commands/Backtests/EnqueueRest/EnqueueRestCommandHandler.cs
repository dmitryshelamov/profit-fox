using MediatR;

using ProfitFox.Application.Contracts.Backtests;
using ProfitFox.Application.Contracts.Backtests.Converters;
using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Commands.Backtests.EnqueueRest;

/// <summary>
///     A new backtest of one set on the history its source did not see: the version, the expanded graph and the kind of the
///     source, the values and the timeframe of the set, the pairs and the fees of the source, from the end of the source to
///     the start of the current UTC day, with a reference to the source and its set.
/// </summary>
internal sealed class EnqueueRestCommandHandler(IBacktestRepository backtests, TimeProvider time) : IRequestHandler<EnqueueRestCommand, BacktestInternal>
{
    /// <exception cref="BacktestNotFoundException">There is no such backtest or set.</exception>
    /// <exception cref="BacktestRequestException">The backtest is not done, or it has no rest of history.</exception>
    public async Task<BacktestInternal> Handle(EnqueueRestCommand request, CancellationToken cancellationToken)
    {
        Backtest source = await backtests.FindAsync(request.Backtest, cancellationToken)
                          ?? throw new BacktestNotFoundException($"There is no backtest {request.Backtest}.");
        BacktestSet set = source.Sets.FirstOrDefault(item => item.Set.Number == request.Set)?.Set
                          ?? throw new BacktestNotFoundException($"The backtest {request.Backtest} has no set {request.Set}.");
        DateTimeOffset today = HistoryPeriod.Today(time.GetUtcNow());
        List<BacktestError> errors = [];
        if (source.Status != BacktestState.Done)
        {
            errors.Add(new BacktestError("NotDone", "прогон ещё не закончен: набор на остатке истории ставится у готового прогона"));
        }

        if (source.Settings.End >= today)
        {
            errors.Add(new BacktestError(BacktestError.NoRest, "остатка истории нет: прогон считал до начала текущих суток") { Field = "end" });
        }

        if (errors.Count > 0)
        {
            throw new BacktestRequestException(errors);
        }

        BacktestSettings settings = source.Settings with
        {
            Values = [.. set.Values.Select(value => new ParameterValues(value.Key, [value.Value]))],
            Timeframes = [set.Timeframe],
            Start = source.Settings.End,
            End = today
        };
        IReadOnlyList<BacktestSet> sets = BacktestGrid.Of(settings);
        var rest = new Backtest
        {
            Id = Guid.NewGuid().ToString("N"),
            StrategyId = source.StrategyId,
            StrategyName = source.StrategyName,
            StrategyVersion = source.StrategyVersion,
            Kind = source.Kind,
            Graph = source.Graph,
            Settings = settings,
            Sets = [.. sets.Select(item => new BacktestSetOutcome(item, Numbers: null))],
            SetCount = sets.Count,
            Source = new BacktestSource(source.Id, set.Number)
        };
        return (await backtests.EnqueueAsync(rest, cancellationToken)).ToInternal();
    }
}
